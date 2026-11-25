using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace TrustPlatform.Api.ValidationApi;

public sealed record ValidationApiResponse(
    HttpStatusCode StatusCode,
    string? ReasonPhrase,
    string? ContentType,
    string Body)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
}

public sealed class ValidationApiClient(
    HttpClient httpClient,
    IOptions<ValidationApiOptions> options)
{
    public async Task<ValidationApiResponse> SendAsync(
        HttpMethod method,
        string relativePath,
        string accessToken,
        HttpContent? content = null,
        CancellationToken cancellationToken = default,
        Action<HttpRequestMessage>? configureRequest = null)
    {
        var requestUri = GetRequestUri(options.Value.BaseUrl, relativePath);

        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        configureRequest?.Invoke(request);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var statusCode = response.StatusCode;
        var reasonPhrase = response.ReasonPhrase;
        var contentType = response.Content.Headers.ContentType?.ToString();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ValidationApiResponse(statusCode, reasonPhrase, contentType, body);
    }

    private static Uri GetRequestUri(string baseUrl, string relativePath)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new ValidationApiConfigurationException(
                "The Validation API base URL must be a configured HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return baseUri;
        }

        if (relativePath.StartsWith("/", StringComparison.Ordinal) ||
            Uri.TryCreate(relativePath, UriKind.Absolute, out _) ||
            relativePath.Contains('\\') ||
            relativePath.Contains('?') ||
            relativePath.Contains('#') ||
            relativePath.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("A safe relative Validation API path is required.", nameof(relativePath));
        }

        var basePath = baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";
        var normalizedBaseUri = new Uri(basePath, UriKind.Absolute);
        var requestUri = new Uri(normalizedBaseUri, relativePath);
        if (!string.Equals(requestUri.Authority, normalizedBaseUri.Authority, StringComparison.OrdinalIgnoreCase) ||
            !requestUri.AbsolutePath.StartsWith(normalizedBaseUri.AbsolutePath, StringComparison.Ordinal))
        {
            throw new ArgumentException("The Validation API path must remain within the configured base URL.", nameof(relativePath));
        }

        return requestUri;
    }
}
