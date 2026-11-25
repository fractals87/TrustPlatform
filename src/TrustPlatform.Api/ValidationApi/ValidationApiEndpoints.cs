using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace TrustPlatform.Api.ValidationApi;

public static class ValidationApiEndpoints
{
    private const int MaximumProviderErrorBodyLength = 16_384;
    private const int MaximumErrorTextLength = 1_000;
    private const int MaximumResponseExcerptLength = 5_000;

    private static readonly HashSet<string> AllowedFileExtensions = new(
        [".pdf", ".p7m", ".xml", ".p7s"],
        StringComparer.OrdinalIgnoreCase);

    public static void MapValidationApiEndpoints(this WebApplication app)
    {
        var isDevelopment = app.Environment.IsDevelopment();
        app.MapPost("/api/validation/signature", async (
            HttpRequest request,
            ValidationApiClient validationApiClient,
            ValidationApiTokenService tokenService,
            IOptions<ValidationApiOptions> options,
            CancellationToken cancellationToken) =>
        {
            IFormCollection form;
            try
            {
                form = await request.ReadFormAsync(cancellationToken);
            }
            catch (InvalidDataException exception) when (
                exception.Message.Contains("length limit", StringComparison.OrdinalIgnoreCase))
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }
            catch (InvalidDataException)
            {
                return Results.BadRequest(new { error = "The uploaded form is invalid." });
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest(new { error = "Upload exactly one signed document." });
            }

            if (form.Files.Count != 1)
            {
                return Results.BadRequest(new { error = "Upload exactly one signed document." });
            }

            var file = form.Files[0];
            if (file.Length == 0)
            {
                return Results.BadRequest(new { error = "The selected file is empty." });
            }

            var maxFileSize = options.Value.Upload.MaxFileSizeBytes;
            if (maxFileSize <= 0 || maxFileSize > int.MaxValue - 65_536)
            {
                return CreateSafeError(
                    isDevelopment,
                    request.HttpContext.TraceIdentifier,
                    StatusCodes.Status503ServiceUnavailable,
                    "Validation configuration is invalid.");
            }

            if (file.Length > maxFileSize)
            {
                return Results.BadRequest(new
                {
                    error = $"The file exceeds the {GetSizeLabel(maxFileSize)} TrustPlatform upload limit.",
                });
            }

            var fileName = SanitizeFileName(file.FileName);
            if (!AllowedFileExtensions.Contains(Path.GetExtension(fileName)))
            {
                return Results.BadRequest(new
                {
                    error = "Supported TrustPlatform PoC formats are .pdf, .p7m, .xml, and .p7s.",
                });
            }

            byte[] documentBytes;
            await using (var stream = file.OpenReadStream())
            {
                try
                {
                    documentBytes = await ReadBoundedAsync(stream, maxFileSize, cancellationToken);
                }
                catch (UploadTooLargeException)
                {
                    return Results.BadRequest(new
                    {
                        error = $"The file exceeds the {GetSizeLabel(maxFileSize)} TrustPlatform upload limit.",
                    });
                }
            }

            var documentData = Convert.ToBase64String(documentBytes);
            var providerRequest = new ValidationProviderRequest(
                new ValidationSignedDocument(documentData, fileName),
                new ValidationOptions(
                    ValidationLevel: "ARCHIVAL_DATA",
                    ExtractOriginals: false,
                    IncludeEtsiValidationReport: false));

            using var providerContent = JsonContent.Create(providerRequest);
            var correlationId = request.HttpContext.TraceIdentifier;
            string accessToken;
            try
            {
                accessToken = await tokenService.GetAccessTokenAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ValidationApiConfigurationException)
            {
                return CreateSafeError(
                    isDevelopment,
                    correlationId,
                    StatusCodes.Status503ServiceUnavailable,
                    "Validation API configuration is missing.");
            }
            catch (ValidationApiAuthenticationException)
            {
                return CreateSafeError(
                    isDevelopment,
                    correlationId,
                    StatusCodes.Status502BadGateway,
                    "Validation API authentication failed.");
            }

            ValidationApiResponse response;
            try
            {
                response = await validationApiClient.SendAsync(
                    HttpMethod.Post,
                    string.Empty,
                    accessToken,
                    providerContent,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return CreateSafeError(
                    isDevelopment,
                    correlationId,
                    StatusCodes.Status502BadGateway,
                    "The validation provider could not be reached.");
            }
            catch (IOException)
            {
                return CreateSafeError(
                    isDevelopment,
                    correlationId,
                    StatusCodes.Status502BadGateway,
                    "The validation provider response could not be read.");
            }
            catch (ValidationApiConfigurationException)
            {
                return CreateSafeError(
                    isDevelopment,
                    correlationId,
                    StatusCodes.Status503ServiceUnavailable,
                    "Validation API configuration is missing.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = CreateProviderError(
                    response,
                    isDevelopment,
                    correlationId,
                    accessToken,
                    options.Value.Authentication,
                    documentData);
                return Results.Json(error, statusCode: error.Status);
            }

            try
            {
                var result = JsonSerializer.Deserialize<ValidationResponse>(
                    response.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result is null)
                {
                    return CreateInvalidProviderResponse(
                        response,
                        isDevelopment,
                        correlationId,
                        accessToken,
                        options.Value.Authentication,
                        documentData,
                        "The provider response was JSON null.");
                }

                return Results.Json(SanitizeValidationResponse(result));
            }
            catch (JsonException exception)
            {
                return CreateInvalidProviderResponse(
                    response,
                    isDevelopment,
                    correlationId,
                    accessToken,
                    options.Value.Authentication,
                    documentData,
                    exception.Message);
            }
        })
            .DisableAntiforgery()
            .WithName("ValidateDocument");

        app.MapPost("/api/diagnostics/validation-auth", async (
            ValidationApiTokenService tokenService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await tokenService.TestAuthenticationAsync(cancellationToken));
            }
            catch (ValidationApiConfigurationException)
            {
                return Results.Json(
                    new ValidationApiAuthenticationDiagnostic(
                        Service: "VALIDATION API",
                        Authenticated: false,
                        TokenAcquired: false,
                        TokenType: null,
                        ExpiresIn: null,
                        Scope: null,
                        Cached: false,
                        Error: "Validation API authentication is not configured."),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (ValidationApiAuthenticationException)
            {
                return Results.Json(
                    new ValidationApiAuthenticationDiagnostic(
                        Service: "VALIDATION API",
                        Authenticated: false,
                        TokenAcquired: false,
                        TokenType: null,
                        ExpiresIn: null,
                        Scope: null,
                        Cached: false,
                        Error: "Validation API authentication failed."),
                    statusCode: StatusCodes.Status502BadGateway);
            }
        })
            .WithName("TestValidationApiAuthentication");
    }

    private static ValidationResponse SanitizeValidationResponse(ValidationResponse response)
    {
        var report = response.ValidationReport;
        var safeReport = report is null
            ? null
            : report with
            {
                SignatureValidationPolicyDescription = NormalizeWhitespace(report.SignatureValidationPolicyDescription),
                DocumentFileName = report.DocumentFileName is null
                    ? null
                    : SanitizeFileName(report.DocumentFileName),
            };

        return response with { ValidationReport = safeReport };
    }

    private static ValidationApiErrorResponse CreateProviderError(
        ValidationApiResponse response,
        bool isDevelopment,
        string correlationId,
        string accessToken,
        ValidationApiAuthenticationOptions authentication,
        string documentData)
    {
        const string title = "Validation provider request failed";
        const int gatewayStatus = StatusCodes.Status502BadGateway;
        if (!isDevelopment)
        {
            return new ValidationApiErrorResponse(title, gatewayStatus, correlationId);
        }

        var redactions = new[]
        {
            accessToken,
            authentication.Username,
            authentication.Password,
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{authentication.Username}:{authentication.Password}")),
            documentData,
        };
        var contentType = LimitText(RedactSensitiveValues(response.ContentType, redactions), 200);
        var reasonPhrase = RedactSensitiveValues(SanitizeErrorText(response.ReasonPhrase, 200), redactions);
        var body = LimitText(response.Body, MaximumProviderErrorBodyLength) ?? string.Empty;

        string? providerCode = null;
        string? message;
        string? detail = null;
        if (IsHtml(response.ContentType, body))
        {
            message = "The validation provider or an upstream gateway rejected the request.";
        }
        else if (IsJson(response.ContentType, body))
        {
            (providerCode, message, detail) = ParseJsonError(body, redactions);
            if (IsHtml(null, message ?? string.Empty))
            {
                message = "The validation provider or an upstream gateway rejected the request.";
                detail = null;
            }
            else
            {
                message ??= "The validation provider rejected the request.";
            }
        }
        else
        {
            message = SanitizeErrorText(body, MaximumErrorTextLength);
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "The validation provider rejected the request.";
            }
            else
            {
                message = RedactSensitiveValues(message, redactions);
            }
        }

        return new ValidationApiErrorResponse(
            title,
            gatewayStatus,
            correlationId,
            (int)response.StatusCode,
            reasonPhrase,
            contentType,
            SanitizeErrorText(providerCode, 200) is { Length: > 0 } code
                ? RedactSensitiveValues(code, redactions)
                : null,
            SanitizeErrorText(message, MaximumErrorTextLength) is { Length: > 0 } safeMessage
                ? RedactSensitiveValues(safeMessage, redactions)
                : "The validation provider rejected the request.",
            SanitizeErrorText(detail, MaximumErrorTextLength) is { Length: > 0 } safeDetail
                ? RedactSensitiveValues(safeDetail, redactions)
                : null);
    }

    private static IResult CreateInvalidProviderResponse(
        ValidationApiResponse response,
        bool isDevelopment,
        string correlationId,
        string accessToken,
        ValidationApiAuthenticationOptions authentication,
        string documentData,
        string parsingError)
    {
        const int gatewayStatus = StatusCodes.Status502BadGateway;
        if (!isDevelopment)
        {
            return Results.Json(
                new ValidationApiErrorResponse(
                    "Validation provider response failed",
                    gatewayStatus,
                    correlationId),
                statusCode: gatewayStatus);
        }

        var redactions = new[]
        {
            accessToken,
            authentication.Username,
            authentication.Password,
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{authentication.Username}:{authentication.Password}")),
            documentData,
        };
        var contentType = LimitText(RedactSensitiveValues(response.ContentType, redactions), 200);
        var reasonPhrase = RedactSensitiveValues(SanitizeErrorText(response.ReasonPhrase, 200), redactions);
        var error = new ValidationApiErrorResponse(
            "Validation provider response failed",
            gatewayStatus,
            correlationId,
            (int)response.StatusCode,
            reasonPhrase,
            contentType,
            Message: "The validation provider returned an invalid response.",
            ResponseShape: GetResponseShape(response.Body),
            ParsingError: RedactSensitiveValues(SanitizeErrorText(parsingError, 500), redactions),
            ResponseExcerpt: CreateSanitizedResponseExcerpt(response.Body, redactions));

        return Results.Json(error, statusCode: gatewayStatus);
    }

    private static string GetResponseShape(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return root.ValueKind.ToString();
            }

            var properties = root.EnumerateObject()
                .Take(20)
                .Select(property => $"{property.Name}: {GetJsonValueShape(property.Value)}");
            return LimitText($"JSON object; {string.Join(", ", properties)}", 1_000)!;
        }
        catch (JsonException)
        {
            return "Malformed JSON";
        }
    }

    private static string GetJsonValueShape(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object
            ? $"object ({string.Join(", ", value.EnumerateObject().Take(20).Select(property => property.Name))})"
            : value.ValueKind == JsonValueKind.Array
                ? $"array ({value.GetArrayLength()} items)"
                : value.ValueKind.ToString();

    private static string? CreateSanitizedResponseExcerpt(
        string body,
        IReadOnlyList<string> redactions)
    {
        try
        {
            var node = JsonNode.Parse(body);
            if (node is null)
            {
                return null;
            }

            SanitizeResponseNode(node, redactions, certificateObject: false, signedDocumentObject: false);
            var excerpt = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            return LimitText(excerpt, MaximumResponseExcerptLength);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void SanitizeResponseNode(
        JsonNode node,
        IReadOnlyList<string> redactions,
        bool certificateObject,
        bool signedDocumentObject)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (certificateObject && property.Key.Equals("data", StringComparison.OrdinalIgnoreCase))
                {
                    jsonObject.Remove(property.Key);
                    continue;
                }

                if (signedDocumentObject && property.Key.Equals("data", StringComparison.OrdinalIgnoreCase))
                {
                    jsonObject[property.Key] = "[redacted]";
                    continue;
                }

                if (Regex.IsMatch(property.Key, "(?i)(token|authorization|password|secret|credential)"))
                {
                    jsonObject[property.Key] = "[redacted]";
                    continue;
                }

                if (property.Value is not null)
                {
                    SanitizeResponseNode(
                        property.Value,
                        redactions,
                        certificateObject: false,
                        signedDocumentObject: property.Key.Equals("signedDocument", StringComparison.OrdinalIgnoreCase));
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null)
                {
                    SanitizeResponseNode(
                        item,
                        redactions,
                        certificateObject: node.Parent is JsonObject parent &&
                            parent.Any(property =>
                                property.Key.Equals("certificates", StringComparison.OrdinalIgnoreCase) &&
                                ReferenceEquals(property.Value, node)),
                        signedDocumentObject: false);
                }
            }
        }
        else if (node.Parent is not null &&
            node is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var text) &&
            text is not null)
        {
            var sanitized = RedactSensitiveValues(text, redactions);
            sanitized = Regex.Replace(sanitized ?? string.Empty, @"[A-Za-z0-9+/_=-]{32,}", "[redacted token-like value]");
            node.ReplaceWith(JsonValue.Create(sanitized));
        }
    }

    private static IResult CreateSafeError(
        bool isDevelopment,
        string correlationId,
        int status,
        string safeDevelopmentMessage)
    {
        var error = isDevelopment
            ? new ValidationApiErrorResponse(
                "Validation request failed",
                status,
                correlationId,
                Message: safeDevelopmentMessage)
            : new ValidationApiErrorResponse(
                "Validation request failed",
                status,
                correlationId);

        return Results.Json(error, statusCode: status);
    }

    private static (string? Code, string? Message, string? Detail) ParseJsonError(
        string body,
        IReadOnlyList<string> redactions)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null, null);
            }

            var code = GetJsonString(json.RootElement, "errorCode")
                ?? GetJsonString(json.RootElement, "code");
            var message = GetJsonString(json.RootElement, "message")
                ?? GetJsonString(json.RootElement, "title")
                ?? GetJsonString(json.RootElement, "error");
            var detail = GetJsonString(json.RootElement, "detail");

            return (
                RedactSensitiveValues(code, redactions),
                RedactSensitiveValues(message, redactions),
                RedactSensitiveValues(detail, redactions));
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static string? GetJsonString(JsonElement json, string propertyName)
    {
        foreach (var property in json.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static bool IsJson(string? mediaType, string body) =>
        mediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ||
        body.TrimStart().StartsWith('{') ||
        body.TrimStart().StartsWith('[');

    private static bool IsHtml(string? mediaType, string body)
    {
        if (mediaType?.Equals("text/html", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var trimmed = body.TrimStart();
        return trimmed.StartsWith('<') ||
               Regex.IsMatch(body, @"<\s*/?\s*[A-Za-z][^>]*>");
    }

    private static string? RedactSensitiveValues(string? value, IReadOnlyList<string> redactions)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var sanitized = value;
        foreach (var secret in redactions.Where(secret => !string.IsNullOrEmpty(secret)))
        {
            sanitized = sanitized.Replace(secret, "[redacted]", StringComparison.OrdinalIgnoreCase);
        }

        sanitized = Regex.Replace(
            sanitized,
            @"(?i)\b(Bearer|Basic)\s+[A-Za-z0-9._~+/-]+=*",
            "$1 [redacted]");
        sanitized = Regex.Replace(
            sanitized,
            @"\beyJ[A-Za-z0-9_-]*\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b",
            "[redacted token]");
        sanitized = Regex.Replace(
            sanitized,
            @"(?i)(?:[A-Za-z]:\\|/(?:tmp|var|home|users|root|app|workspace|mnt|usr|etc|opt|srv|run|proc)/)[^\s<>""|?*]*",
            "[path redacted]");
        var stackTraceStart = sanitized.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
        if (stackTraceStart < 0 && sanitized.StartsWith("at ", StringComparison.OrdinalIgnoreCase))
        {
            stackTraceStart = 0;
        }

        var stackTraceLabel = sanitized.IndexOf("stacktrace", StringComparison.OrdinalIgnoreCase);
        if (stackTraceLabel >= 0 && (stackTraceStart < 0 || stackTraceLabel < stackTraceStart))
        {
            stackTraceStart = stackTraceLabel;
        }

        if (stackTraceStart >= 0)
        {
            sanitized = sanitized[..stackTraceStart];
        }

        return SanitizeErrorText(sanitized, MaximumErrorTextLength);
    }

    private static string? SanitizeErrorText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = new string(value
            .Where(character => !char.IsControl(character) || character is '\r' or '\n' or '\t')
            .Select(character => char.IsControl(character) ? ' ' : character)
            .ToArray())
            .Trim();

        return LimitText(sanitized, maximumLength);
    }

    private static string? LimitText(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maximumLength
                ? value
                : value[..maximumLength];

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[81_920];
        long totalBytes = 0;
        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                return memory.ToArray();
            }

            totalBytes += bytesRead;
            if (totalBytes > maximumBytes)
            {
                throw new UploadTooLargeException();
            }

            await memory.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }
    }

    private static string? NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static string SanitizeFileName(string fileName)
    {
        var candidate = fileName.Replace('\\', '/').Split('/').LastOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "document";
        }

        candidate = new string(candidate
            .Select(character =>
                Path.GetInvalidFileNameChars().Contains(character) || char.IsControl(character)
                    ? '_'
                    : character)
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(candidate)
            ? "document"
            : candidate.Length > 200
                ? candidate[..200]
                : candidate;
    }

    private static string GetSizeLabel(long sizeBytes)
    {
        if (sizeBytes < 1024)
        {
            return $"{sizeBytes} bytes";
        }

        if (sizeBytes < 1024 * 1024)
        {
            return $"{sizeBytes / 1024.0:F1} KB";
        }

        return $"{sizeBytes / (1024.0 * 1024.0):F2} MB";
    }

    private sealed class UploadTooLargeException : Exception
    {
    }
}
