using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TrustPlatform.Api.SignApi;

public sealed class SignApiRequestException : Exception
{
    public SignApiRequestException(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        string? contentType,
        string body,
        string? safeMessage = null,
        string? safeCode = null,
        string? safeDetail = null,
        string? providerPath = null)
        : base(safeMessage ?? "The SIGN API request failed.")
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        ContentType = contentType;
        Body = body;
        SafeMessage = safeMessage;
        SafeCode = safeCode;
        SafeDetail = safeDetail;
        ProviderPath = providerPath;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ReasonPhrase { get; }

    public string? ContentType { get; }

    public string Body { get; }

    public string? SafeMessage { get; }

    public string? SafeCode { get; }

    public string? SafeDetail { get; }

    public string? ProviderPath { get; }
}

public sealed class SignApiClient(
    HttpClient httpClient,
    SignApiTokenService tokenService,
    IOptions<SignApiOptions> options,
    ILogger<SignApiClient>? logger = null)
{
    private static readonly JsonSerializerOptions DeserializationOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativePath,
        HttpContent? content = null,
        CancellationToken cancellationToken = default,
        Action<HttpRequestMessage>? configureRequest = null)
    {
        var requestUri = GetRequestUri(options.Value.BaseUrl, relativePath);
        var accessToken = await tokenService.GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        configureRequest?.Invoke(request);

        return await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderCertificate>> GetCertificatesAsync(
        string xSignerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(xSignerId) || ContainsControlCharacters(xSignerId))
        {
            throw new SignApiConfigurationException("The configured X-signer-id is missing.");
        }

        using var response = await SendAsync(
            HttpMethod.Get,
            "certificates",
            cancellationToken: cancellationToken,
            configureRequest: request =>
            {
                request.Headers.Remove("X-signer-id");
                request.Headers.TryAddWithoutValidation("X-signer-id", xSignerId);
            });

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateRequestException(response, body);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        try
        {
            var certificates = JsonSerializer.Deserialize<List<ProviderCertificate>>(body, DeserializationOptions);
            return certificates ?? [];
        }
        catch (JsonException exception)
        {
            throw new SignApiRequestException(
                HttpStatusCode.BadGateway,
                response.ReasonPhrase,
                response.Content.Headers.ContentType?.ToString(),
                body,
                "The provider certificate list response was invalid.",
                "INVALID_RESPONSE",
                exception.Message,
                "certificates");
        }
    }

    public async Task<ProviderCertificate> GetCertificateAsync(
        string certificateId,
        string xSignerId,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeCertificateId(certificateId))
        {
            throw new ArgumentException("The configured certificate ID is invalid.", nameof(certificateId));
        }

        if (string.IsNullOrWhiteSpace(xSignerId) || ContainsControlCharacters(xSignerId))
        {
            throw new SignApiConfigurationException("The configured X-signer-id is missing.");
        }

        var relativePath = $"certificates/{Uri.EscapeDataString(certificateId)}";
        var requestUri = GetRequestUri(options.Value.BaseUrl, relativePath);
        logger?.LogInformation(
            "Operation=configured-certificate Method=GET RequestUri={RequestUri} CertificateIdConfigured={CertificateIdConfigured} XSignerIdConfigured={XSignerIdConfigured}",
            requestUri,
            !string.IsNullOrWhiteSpace(certificateId),
            !string.IsNullOrWhiteSpace(xSignerId));

        using var response = await SendAsync(
            HttpMethod.Get,
            relativePath,
            cancellationToken: cancellationToken,
            configureRequest: request =>
            {
                request.Headers.Remove("X-signer-id");
                request.Headers.TryAddWithoutValidation("X-signer-id", xSignerId);
            });

        logger?.LogInformation(
            "Operation=configured-certificate ProviderStatus={ProviderStatus} Reason={Reason}",
            response.StatusCode,
            response.ReasonPhrase);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateRequestException(response, body);
        }

        try
        {
            var certificate = JsonSerializer.Deserialize<ProviderCertificate>(body, DeserializationOptions);
            if (certificate is null)
            {
                throw new SignApiRequestException(
                    HttpStatusCode.BadGateway,
                    response.ReasonPhrase,
                    response.Content.Headers.ContentType?.ToString(),
                    body,
                    "The provider certificate detail response was invalid.",
                    "INVALID_RESPONSE",
                    "The provider response was null.",
                    relativePath);
            }

            return certificate;
        }
        catch (JsonException exception)
        {
            throw new SignApiRequestException(
                HttpStatusCode.BadGateway,
                response.ReasonPhrase,
                response.Content.Headers.ContentType?.ToString(),
                body,
                "The provider certificate detail response was invalid.",
                "INVALID_RESPONSE",
                exception.Message,
                relativePath);
        }
    }

    public async Task<IReadOnlyList<ProviderCertificateChainItem>> GetCertificateChainAsync(
        string certificateId,
        string signerId,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeCertificateId(certificateId))
        {
            throw new ArgumentException("The certificate ID is invalid.", nameof(certificateId));
        }

        if (string.IsNullOrWhiteSpace(signerId) || ContainsControlCharacters(signerId))
        {
            throw new SignApiConfigurationException("The SIGN API signer ID is missing.");
        }

        var relativePath = $"certificates/{Uri.EscapeDataString(certificateId)}/chain";
        logger?.LogInformation(
            "Operation=certificate-chain Method=GET CertificateIdConfigured={CertificateIdConfigured}",
            !string.IsNullOrWhiteSpace(certificateId));

        using var response = await SendAsync(
            HttpMethod.Get,
            relativePath,
            cancellationToken: cancellationToken,
            configureRequest: request =>
            {
                request.Headers.Remove("X-signer-id");
                request.Headers.TryAddWithoutValidation("X-signer-id", signerId);
            });

        logger?.LogInformation(
            "Operation=certificate-chain ProviderStatus={ProviderStatus}",
            response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateRequestException(response, body);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new SignApiRequestException(
                HttpStatusCode.BadGateway,
                response.ReasonPhrase,
                response.Content.Headers.ContentType?.ToString(),
                body,
                "The provider certificate chain response was empty.",
                "INVALID_RESPONSE",
                "The provider returned an empty response.",
                relativePath);
        }

        try
        {
            var chain = JsonSerializer.Deserialize<List<ProviderCertificateChainItem>>(body, DeserializationOptions);
            if (chain is null)
            {
                throw new SignApiRequestException(
                    HttpStatusCode.BadGateway,
                    response.ReasonPhrase,
                    response.Content.Headers.ContentType?.ToString(),
                    body,
                    "The provider certificate chain response was invalid.",
                    "INVALID_RESPONSE",
                    "The provider response was null.",
                    relativePath);
            }

            if (JsonDocument.Parse(body).RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new SignApiRequestException(
                    HttpStatusCode.BadGateway,
                    response.ReasonPhrase,
                    response.Content.Headers.ContentType?.ToString(),
                    body,
                    "The provider certificate chain response was invalid.",
                    "INVALID_RESPONSE",
                    "The provider response was not an array.",
                    relativePath);
            }

            return chain;
        }
        catch (JsonException exception)
        {
            throw new SignApiRequestException(
                HttpStatusCode.BadGateway,
                response.ReasonPhrase,
                response.Content.Headers.ContentType?.ToString(),
                body,
                "The provider certificate chain response was invalid.",
                "INVALID_RESPONSE",
                exception.Message,
                relativePath);
        }
    }

    private static SignApiRequestException CreateRequestException(HttpResponseMessage response, string body)
    {
        var problem = TryParseProviderProblem(body);
        return new SignApiRequestException(
            response.StatusCode,
            response.ReasonPhrase,
            response.Content.Headers.ContentType?.ToString(),
            body,
            problem.Message ?? "The SIGN API request failed.",
            problem.Code,
            problem.Detail,
            response.RequestMessage?.RequestUri?.PathAndQuery);
    }

    private static SignApiProviderProblem TryParseProviderProblem(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new SignApiProviderProblem();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new SignApiProviderProblem();
            }

            string? message = null;
            string? code = null;
            string? detail = null;
            if (root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
            {
                message = messageElement.GetString();
            }

            if (root.TryGetProperty("error", out var errorElement))
            {
                if (message is null && errorElement.ValueKind == JsonValueKind.String)
                {
                    message = errorElement.GetString();
                }

                if (code is null && errorElement.ValueKind == JsonValueKind.Object && errorElement.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String)
                {
                    code = codeElement.GetString();
                }
            }

            if (root.TryGetProperty("code", out var codeProperty) && codeProperty.ValueKind == JsonValueKind.String)
            {
                code ??= codeProperty.GetString();
            }

            if (root.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind == JsonValueKind.String)
            {
                detail = detailElement.GetString();
            }

            return new SignApiProviderProblem(message, code, detail);
        }
        catch (JsonException)
        {
            return new SignApiProviderProblem();
        }
    }

    private static bool IsSafeCertificateId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl))
        {
            return false;
        }

        return value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '+' or '_' or '-');
    }

    private static bool ContainsControlCharacters(string value) =>
        value.Any(char.IsControl);

    private sealed record SignApiProviderProblem(string? Message = null, string? Code = null, string? Detail = null);

    private static Uri GetRequestUri(string baseUrl, string relativePath)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new SignApiConfigurationException(
                "The SIGN API base URL must be a configured HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(relativePath) ||
            relativePath.StartsWith("/", StringComparison.Ordinal) ||
            Uri.TryCreate(relativePath, UriKind.Absolute, out _) ||
            relativePath.Contains('\\') ||
            relativePath.Contains('?') ||
            relativePath.Contains('#') ||
            relativePath.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("A safe relative SIGN API path is required.", nameof(relativePath));
        }

        var basePath = baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";
        var normalizedBaseUri = new Uri(basePath, UriKind.Absolute);
        var requestUri = new Uri(normalizedBaseUri, relativePath);
        if (!string.Equals(requestUri.Authority, normalizedBaseUri.Authority, StringComparison.OrdinalIgnoreCase) ||
            !requestUri.AbsolutePath.StartsWith(normalizedBaseUri.AbsolutePath, StringComparison.Ordinal))
        {
            throw new ArgumentException("The SIGN API path must remain within the configured base URL.", nameof(relativePath));
        }

        return requestUri;
    }
}
