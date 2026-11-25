using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TrustPlatform.Api.ValidationApi;

public sealed class ValidationApiTokenService(
    IHttpClientFactory httpClientFactory,
    IOptions<ValidationApiOptions> options,
    TimeProvider timeProvider)
{
    public const string HttpClientName = "ValidationApiIdentityProvider";
    public const string RequiredScope = "validation";

    private static readonly TimeSpan MaximumRefreshSkew = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private CachedToken? _cachedToken;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        => (await GetCachedTokenAsync(cancellationToken)).AccessToken;

    public async Task<ValidationApiAuthenticationDiagnostic> TestAuthenticationAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await GetCachedTokenAsync(cancellationToken);
        return new ValidationApiAuthenticationDiagnostic(
            Service: "VALIDATION API",
            Authenticated: true,
            TokenAcquired: true,
            TokenType: token.TokenType,
            ExpiresIn: token.ExpiresIn,
            Scope: token.Scope,
            Cached: true,
            Error: null);
    }

    private async Task<CachedToken> GetCachedTokenAsync(CancellationToken cancellationToken)
    {
        var cachedToken = Volatile.Read(ref _cachedToken);
        var now = timeProvider.GetUtcNow();
        if (IsUsable(cachedToken, now))
        {
            return cachedToken!;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            cachedToken = Volatile.Read(ref _cachedToken);
            now = timeProvider.GetUtcNow();
            if (IsUsable(cachedToken, now))
            {
                return cachedToken!;
            }

            var token = await RequestTokenAsync(cancellationToken);
            Volatile.Write(ref _cachedToken, token);
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<CachedToken> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var tokenEndpoint = GetTokenEndpoint(settings);
        var authentication = settings.Authentication;

        if (string.IsNullOrWhiteSpace(authentication.Username) ||
            string.IsNullOrWhiteSpace(authentication.Password))
        {
            throw new ValidationApiConfigurationException(
                "Validation API credentials are not configured.");
        }

        if (!string.Equals(authentication.GrantType, "client_credentials", StringComparison.Ordinal))
        {
            throw new ValidationApiConfigurationException(
                "Validation API authentication must use the configured client_credentials grant.");
        }

        if (!string.Equals(authentication.Scope, RequiredScope, StringComparison.Ordinal))
        {
            throw new ValidationApiConfigurationException(
                $"Validation API OAuth scope must be '{RequiredScope}'.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type", authentication.GrantType),
                new KeyValuePair<string, string>("scope", RequiredScope),
                new KeyValuePair<string, string>("username", authentication.Username),
                new KeyValuePair<string, string>("password", authentication.Password),
            ]),
        };

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{authentication.Username}:{authentication.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        using var client = httpClientFactory.CreateClient(HttpClientName);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            throw new ValidationApiAuthenticationException(
                "The Validation API identity provider could not be reached.",
                exception);
        }

        using (response)
        {
            if ((int)response.StatusCode < (int)HttpStatusCode.OK ||
                (int)response.StatusCode >= (int)HttpStatusCode.MultipleChoices)
            {
                throw new ValidationApiAuthenticationException(
                    "The Validation API identity provider rejected authentication.");
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var payload = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);
                return ParseTokenResponse(payload.RootElement, timeProvider.GetUtcNow());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or OverflowException)
            {
                throw new ValidationApiAuthenticationException(
                    "The Validation API identity provider returned an invalid token response.",
                    exception);
            }
        }
    }

    private static CachedToken ParseTokenResponse(JsonElement payload, DateTimeOffset acquiredAt)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("access_token", out var accessTokenElement) ||
            accessTokenElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(accessTokenElement.GetString()) ||
            !payload.TryGetProperty("token_type", out var tokenTypeElement) ||
            tokenTypeElement.ValueKind != JsonValueKind.String ||
            !string.Equals(tokenTypeElement.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase) ||
            !payload.TryGetProperty("expires_in", out var expiresInElement) ||
            !expiresInElement.TryGetInt32(out var expiresIn) ||
            expiresIn <= 0)
        {
            throw new ValidationApiAuthenticationException(
                "The Validation API identity provider returned an invalid token response.");
        }

        var lifetime = TimeSpan.FromSeconds(expiresIn);
        var refreshSkew = TimeSpan.FromTicks(Math.Min(
            MaximumRefreshSkew.Ticks,
            lifetime.Ticks / 10));

        var scope = payload.TryGetProperty("scope", out var scopeElement) &&
                    scopeElement.ValueKind == JsonValueKind.String
            ? scopeElement.GetString() ?? string.Empty
            : string.Empty;

        return new CachedToken(
            accessTokenElement.GetString()!,
            "Bearer",
            expiresIn,
            acquiredAt + lifetime,
            refreshSkew,
            scope);
    }

    private static Uri GetTokenEndpoint(ValidationApiOptions settings)
    {
        if (!Uri.TryCreate(settings.IdentityProviderUrl, UriKind.Absolute, out var identityProviderUri) ||
            identityProviderUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(identityProviderUri.UserInfo) ||
            !string.IsNullOrEmpty(identityProviderUri.Query) ||
            !string.IsNullOrEmpty(identityProviderUri.Fragment))
        {
            throw new ValidationApiConfigurationException(
                "The Validation API identity provider URL must be a configured HTTPS URL.");
        }

        return new Uri(
            $"{settings.IdentityProviderUrl.TrimEnd('/')}/token",
            UriKind.Absolute);
    }

    private static bool IsUsable(CachedToken? token, DateTimeOffset now) =>
        token is not null && now < token.ExpiresAt - token.RefreshSkew;

    private sealed record CachedToken(
        string AccessToken,
        string TokenType,
        int ExpiresIn,
        DateTimeOffset ExpiresAt,
        TimeSpan RefreshSkew,
        string Scope);
}
