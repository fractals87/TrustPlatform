using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TrustPlatform.Api.SignApi;

namespace TrustPlatform.Api.Signing;

/// <summary>Multipart input for the supported automatic CAdES signing operation.</summary>
public sealed record AutomaticCadesSigningRequest
{
    /// <summary>The single PDF document to sign.</summary>
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }

    /// <summary>The certificate identifier used to construct the provider signing path.</summary>
    [FromForm(Name = "certificateId")]
    public string? CertificateId { get; init; }

    /// <summary>The signing identifier sent in the provider's X-signer-id header.</summary>
    [FromForm(Name = "xSignerId")]
    public string? XSignerId { get; init; }

    /// <summary>The signing PIN; it is forwarded only to the provider.</summary>
    [FromForm(Name = "pin")]
    public string? Pin { get; init; }

    /// <summary>The Infocert-SAT value; it is forwarded only as a provider header.</summary>
    [FromForm(Name = "sat")]
    public string? Sat { get; init; }

    /// <summary>The provider packaging value.</summary>
    [FromForm(Name = "packaging")]
    public string? Packaging { get; init; }

    /// <summary>The provider CAdES signature-level value.</summary>
    [FromForm(Name = "signatureLevel")]
    public string? SignatureLevel { get; init; }

    public override string ToString() => "Automatic CAdES signing request";
}

/// <summary>Safe upload and provider-option metadata for the signing form.</summary>
public sealed record SigningOptionsResponse(
    long MaxFileSizeBytes,
    IReadOnlyList<string> AllowedExtensions,
    IReadOnlyList<string> AllowedContentTypes,
    IReadOnlyList<string> PackagingValues,
    IReadOnlyList<string> SignatureLevelValues);

/// <summary>Local-development defaults for the Automatic + CAdES form.</summary>
public sealed record AutomaticCadesDevelopmentDefaultsResponse(
    string CertificateId,
    string XSignerId,
    string Pin,
    string InfocertSat,
    string Packaging,
    string SignatureLevel);

/// <summary>A sanitized signing error returned to the browser.</summary>
public sealed record SigningErrorResponse(string Error);

internal sealed record AutomaticCadesSigningResult(
    byte[] Content,
    string ContentType,
    string FileName,
    string RequestId);

internal sealed class AutomaticCadesSigningException(int statusCode, string safeMessage)
    : Exception(safeMessage)
{
    public int StatusCode { get; } = statusCode;
}

internal interface IAutomaticCadesSigningService
{
    Task<AutomaticCadesSigningResult> SignAsync(
        AutomaticCadesSigningRequest request,
        CancellationToken cancellationToken);
}

internal sealed class AutomaticCadesSigningService(
    SignApiClient signApiClient,
    SignApiTokenService tokenService,
    IOptions<SignApiOptions> options) : IAutomaticCadesSigningService
{
    private const string PdfContentType = "application/pdf";
    private const string SignedContentType = "application/pkcs7-mime";
    private static readonly string[] PackagingValues = ["ENVELOPED", "ENVELOPING"];
    private static readonly string[] SignatureLevelValues = ["BASELINE-B"];
    private const int MaxCertificateIdLength = 128;
    private const int MaxSignerIdLength = 256;
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    public async Task<AutomaticCadesSigningResult> SignAsync(
        AutomaticCadesSigningRequest request,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        ValidateRequest(request, settings.Upload.MaxFileSizeBytes);
        var document = await ReadAndValidatePdfAsync(
            request.File!,
            settings.Upload.MaxFileSizeBytes,
            cancellationToken);

        var applicationId = settings.Defaults.ApplicationId;
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status503ServiceUnavailable,
                "Automatic CAdES signing is not configured.");
        }

        try
        {
            _ = await tokenService.GetAccessTokenAsync(cancellationToken);
        }
        catch (SignApiConfigurationException)
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status503ServiceUnavailable,
                "SIGN API authentication is not configured.");
        }
        catch (SignApiAuthenticationException)
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status502BadGateway,
                "SIGN API authentication failed.");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var providerRequest = new AutomaticCadesProviderRequest(
            applicationId,
            request.Pin!,
            [
                new CadesSignatureRequest(
                    requestId,
                    new CadesDocumentRequest(
                        Convert.ToBase64String(document),
                        PdfContentType),
                    request.Packaging!,
                    request.SignatureLevel!),
            ]);

        using var providerContent = JsonContent.Create(providerRequest);
        HttpResponseMessage response;
        try
        {
            response = await signApiClient.SendAsync(
                HttpMethod.Post,
                $"certificates/{Uri.EscapeDataString(request.CertificateId!)}/sign",
                providerContent,
                cancellationToken,
                providerMessage =>
                {
                    if (!providerMessage.Headers.TryAddWithoutValidation(
                            "X-signer-id",
                            request.XSignerId) ||
                        !providerMessage.Headers.TryAddWithoutValidation(
                            "Infocert-SAT",
                            request.Sat))
                    {
                        throw new AutomaticCadesSigningException(
                            StatusCodes.Status400BadRequest,
                            "The X-signer-id or Infocert-SAT value is invalid.");
                    }
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status502BadGateway,
                "The signing service could not be reached.");
        }
        catch (SignApiConfigurationException)
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status503ServiceUnavailable,
                "Automatic CAdES signing is not configured.");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new AutomaticCadesSigningException(
                    StatusCodes.Status502BadGateway,
                    "The signing service rejected the signing credentials.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new AutomaticCadesSigningException(
                    StatusCodes.Status502BadGateway,
                    "The signing service could not complete the request.");
            }

            try
            {
                if (!string.Equals(
                        response.Content.Headers.ContentType?.MediaType,
                        "application/json",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new JsonException();
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var payload = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);

                var signedContent = ReadSignedDocument(payload.RootElement, requestId);
                return new AutomaticCadesSigningResult(
                    signedContent,
                    SignedContentType,
                    $"signed-document-{requestId}.p7m",
                    requestId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
            {
                throw new AutomaticCadesSigningException(
                    StatusCodes.Status502BadGateway,
                    "The signing service returned an invalid response.");
            }
        }
    }

    private static void ValidateRequest(AutomaticCadesSigningRequest request, long maxFileSizeBytes)
    {
        if (maxFileSizeBytes <= 0 ||
            maxFileSizeBytes > int.MaxValue - 65_536)
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status503ServiceUnavailable,
                "The signing upload limit is not configured correctly.");
        }

        if (request.File is null)
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status400BadRequest,
                "Select a PDF document to sign.");
        }

        if (request.File.Length == 0)
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status400BadRequest,
                "The selected file is empty.");
        }

        if (request.File.Length > maxFileSizeBytes)
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status413PayloadTooLarge,
                "The selected file exceeds the TrustPlatform upload limit.");
        }

        if (!string.Equals(
                Path.GetExtension(request.File.FileName),
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status400BadRequest,
                "Only PDF documents are supported for automatic CAdES signing.");
        }

        if (!IsValidCertificateId(request.CertificateId))
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status400BadRequest,
                "Enter a valid Certificate ID.");
        }

        if (string.IsNullOrWhiteSpace(request.XSignerId) ||
            request.XSignerId.Length > MaxSignerIdLength ||
            ContainsControlCharacters(request.XSignerId))
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status400BadRequest,
                "Enter a valid X-signer-id.");
        }

        if (string.IsNullOrWhiteSpace(request.Pin))
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status400BadRequest,
                "Enter the signing PIN.");
        }

        if (string.IsNullOrWhiteSpace(request.Sat) ||
            ContainsControlCharacters(request.Sat))
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status400BadRequest,
                "Enter a valid Infocert-SAT value.");
        }

        if (!PackagingValues.Contains(request.Packaging, StringComparer.Ordinal))
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status400BadRequest,
                "Select a supported CAdES packaging value.");
        }

        if (!SignatureLevelValues.Contains(request.SignatureLevel, StringComparer.Ordinal))
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status400BadRequest,
                "Select a supported CAdES signature level.");
        }
    }

    private static async Task<byte[]> ReadAndValidatePdfAsync(
        IFormFile file,
        long maxFileSizeBytes,
        CancellationToken cancellationToken)
    {
        await using var source = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await source.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        if (bytes.Length == 0)
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status400BadRequest,
                "The selected file is empty.");
        }

        if (bytes.Length > maxFileSizeBytes)
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status413PayloadTooLarge,
                "The selected file exceeds the TrustPlatform upload limit.");
        }

        if (bytes.Length < PdfSignature.Length ||
            !bytes.AsSpan(0, PdfSignature.Length).SequenceEqual(PdfSignature))
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status400BadRequest,
                "The selected file is not a valid PDF document.");
        }

        return bytes;
    }

    private static byte[] ReadSignedDocument(JsonElement payload, string requestId)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("applicationId", out var applicationId) ||
            applicationId.ValueKind != JsonValueKind.String ||
            !payload.TryGetProperty("signatureResult", out var signatureResult) ||
            signatureResult.ValueKind != JsonValueKind.Array ||
            signatureResult.GetArrayLength() == 0)
        {
            throw new JsonException();
        }

        JsonElement result = default;
        var matchingResults = 0;
        foreach (var candidate in signatureResult.EnumerateArray())
        {
            if (candidate.ValueKind == JsonValueKind.Object &&
                candidate.TryGetProperty("requestId", out var candidateRequestId) &&
                candidateRequestId.ValueKind == JsonValueKind.String &&
                string.Equals(candidateRequestId.GetString(), requestId, StringComparison.Ordinal))
            {
                result = candidate;
                matchingResults++;
            }
        }

        if (matchingResults != 1 ||
            !result.TryGetProperty("isOk", out var isOk) ||
            isOk.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new JsonException();
        }

        if (isOk.ValueKind == JsonValueKind.False)
        {
            throw new AutomaticCadesSigningException(
                StatusCodes.Status502BadGateway,
                "The signing service could not complete the request.");
        }

        if (
            !result.TryGetProperty("signedDocument", out var signedDocument) ||
            signedDocument.ValueKind != JsonValueKind.Object ||
            !signedDocument.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.String ||
            !signedDocument.TryGetProperty("contentType", out var contentType) ||
            contentType.ValueKind != JsonValueKind.String ||
            !string.Equals(contentType.GetString(), SignedContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException();
        }

        var signedBytes = Convert.FromBase64String(content.GetString()!);
        if (signedBytes.Length == 0)
        {
            throw new JsonException();
        }

        return signedBytes;
    }

    private static bool IsValidCertificateId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaxCertificateIdLength &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '+' or '_' or '-');

    private static bool ContainsControlCharacters(string value) =>
        value.Any(char.IsControl);

    private sealed record AutomaticCadesProviderRequest(
        [property: JsonPropertyName("applicationId")] string ApplicationId,
        [property: JsonPropertyName("pin")] string Pin,
        [property: JsonPropertyName("cadesSignatures")] IReadOnlyList<CadesSignatureRequest> Signatures);

    private sealed record CadesSignatureRequest(
        [property: JsonPropertyName("requestId")] string RequestId,
        [property: JsonPropertyName("document")] CadesDocumentRequest Document,
        [property: JsonPropertyName("packaging")] string Packaging,
        [property: JsonPropertyName("signatureLevel")] string SignatureLevel);

    private sealed record CadesDocumentRequest(
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("contentType")] string ContentType);
}

internal static class SigningEndpoints
{
    internal const long MultipartOverheadBytes = 64 * 1024;
    private static readonly string[] AllowedExtensions = [".pdf"];
    private static readonly string[] AllowedContentTypes = ["application/pdf"];
    private static readonly string[] PackagingValues = ["ENVELOPED", "ENVELOPING"];
    private static readonly string[] SignatureLevelValues = ["BASELINE-B"];

    public static WebApplication MapSigningEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapGet("/api/signing/automatic/cades/defaults", (
                HttpContext context,
                IOptions<SignApiOptions> options) =>
            {
                var configured = options.Value.Defaults.AutomaticSigning;
                var packaging = PackagingValues.Contains(
                    configured.Packaging,
                    StringComparer.Ordinal)
                    ? configured.Packaging
                    : PackagingValues[0];
                var signatureLevel = SignatureLevelValues.Contains(
                    configured.SignatureLevel,
                    StringComparer.Ordinal)
                    ? configured.SignatureLevel
                    : SignatureLevelValues[0];

                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(new AutomaticCadesDevelopmentDefaultsResponse(
                    configured.CertificateId,
                    configured.XSignerId,
                    configured.Pin,
                    configured.InfocertSat,
                    packaging,
                    signatureLevel));
            })
            .WithName("GetAutomaticCadesDevelopmentDefaults")
            .WithSummary("Get local-development defaults for Automatic CAdES");
        }

        app.MapGet("/api/signing/options", IResult (IOptions<SignApiOptions> options) =>
        {
            var upload = options.Value.Upload;
            if (upload.MaxFileSizeBytes <= 0 ||
                upload.MaxFileSizeBytes > int.MaxValue - MultipartOverheadBytes)
            {
                return Results.Json(
                    new SigningErrorResponse("The signing upload limit is not configured correctly."),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new SigningOptionsResponse(
                upload.MaxFileSizeBytes,
                AllowedExtensions,
                AllowedContentTypes,
                PackagingValues,
                SignatureLevelValues));
        })
        .WithName("GetSigningOptions")
        .WithSummary("Get options for the supported automatic CAdES signing workflow");

        var maxFileSizeBytes = app.Services
            .GetRequiredService<IOptions<SignApiOptions>>()
            .Value.Upload.MaxFileSizeBytes;
        var requestLimit = maxFileSizeBytes is > 0 and <= int.MaxValue - MultipartOverheadBytes
            ? checked(maxFileSizeBytes + MultipartOverheadBytes)
            : 10_551_296;

        app.MapPost("/api/signing/automatic/cades", async (
            [FromForm] AutomaticCadesSigningRequest request,
            HttpRequest httpRequest,
            IAutomaticCadesSigningService signingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var form = await httpRequest.ReadFormAsync(cancellationToken);
                if (form.Files.Count != 1 ||
                    !string.Equals(form.Files[0].Name, "file", StringComparison.Ordinal))
                {
                    return Results.BadRequest(
                        new SigningErrorResponse("Upload exactly one PDF document."));
                }

                var result = await signingService.SignAsync(request, cancellationToken);
                httpRequest.HttpContext.Response.Headers.Append(
                    "X-TrustPlatform-Request-Id",
                    result.RequestId);
                httpRequest.HttpContext.Response.Headers.CacheControl = "no-store";
                httpRequest.HttpContext.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.File(
                    result.Content,
                    result.ContentType,
                    result.FileName);
            }
            catch (AutomaticCadesSigningException exception)
            {
                return Results.Json(
                    new SigningErrorResponse(exception.Message),
                    statusCode: exception.StatusCode);
            }
        })
        .WithName("SignAutomaticCades")
        .WithSummary("Sign one PDF document using automatic CAdES")
        .WithDescription("Accepts one PDF and CAdES parameters. PIN and Infocert-SAT are forwarded only to the SIGN API.")
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(requestLimit))
        .Produces(StatusCodes.Status200OK, contentType: "application/pkcs7-mime")
        .Produces<SigningErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<SigningErrorResponse>(StatusCodes.Status413PayloadTooLarge)
        .Produces<SigningErrorResponse>(StatusCodes.Status502BadGateway)
        .Produces<SigningErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}
