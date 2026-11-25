using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TrustPlatform.Api.SignApi;

namespace TrustPlatform.Api.Signing;

public sealed record AutomaticPadesSigningRequest
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }

    [FromForm(Name = "certificateId")]
    public string? CertificateId { get; init; }

    [FromForm(Name = "xSignerId")]
    public string? XSignerId { get; init; }

    [FromForm(Name = "pin")]
    public string? Pin { get; init; }

    [FromForm(Name = "sat")]
    public string? Sat { get; init; }

    [FromForm(Name = "packaging")]
    public string? Packaging { get; init; }

    [FromForm(Name = "signatureLevel")]
    public string? SignatureLevel { get; init; }

    [FromForm(Name = "isVisible")]
    public string? IsVisible { get; init; }

    [FromForm(Name = "isPdfa")]
    public string? IsPdfa { get; init; }

    [FromForm(Name = "page")]
    public int? Page { get; init; }

    [FromForm(Name = "llx")]
    public int? Llx { get; init; }

    [FromForm(Name = "lly")]
    public int? Lly { get; init; }

    [FromForm(Name = "urx")]
    public int? Urx { get; init; }

    [FromForm(Name = "ury")]
    public int? Ury { get; init; }
}

public sealed record AutomaticPadesDevelopmentDefaultsResponse(
    string CertificateId,
    string XSignerId,
    string Pin,
    string InfocertSat,
    string Packaging,
    string SignatureLevel,
    bool IsVisible,
    bool IsPdfa,
    int Page,
    int Llx,
    int Lly,
    int Urx,
    int Ury);

internal sealed record AutomaticPadesSigningResult(
    byte[] Content,
    string ContentType,
    string FileName,
    string RequestId);

internal sealed class AutomaticPadesSigningException(int statusCode, string safeMessage)
    : Exception(safeMessage)
{
    public int StatusCode { get; } = statusCode;
}

internal interface IAutomaticPadesSigningService
{
    Task<AutomaticPadesSigningResult> SignAsync(
        AutomaticPadesSigningRequest request,
        CancellationToken cancellationToken);
}

internal sealed class AutomaticPadesSigningService(
    SignApiClient signApiClient,
    SignApiTokenService tokenService,
    IOptions<SignApiOptions> options) : IAutomaticPadesSigningService
{
    private const string PdfContentType = "application/pdf";
    private static readonly string[] PackagingValues = ["ENVELOPED", "ENVELOPING"];
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private const int MaxCertificateIdLength = 128;
    private const int MaxSignerIdLength = 256;

    public async Task<AutomaticPadesSigningResult> SignAsync(
        AutomaticPadesSigningRequest request,
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
            throw new AutomaticPadesSigningException(
                StatusCodes.Status503ServiceUnavailable,
                "Automatic PAdES signing is not configured.");
        }

        try
        {
            _ = await tokenService.GetAccessTokenAsync(cancellationToken);
        }
        catch (SignApiConfigurationException)
        {
            throw new AutomaticPadesSigningException(
                StatusCodes.Status503ServiceUnavailable,
                "SIGN API authentication is not configured.");
        }
        catch (SignApiAuthenticationException)
        {
            throw new AutomaticPadesSigningException(
                StatusCodes.Status502BadGateway,
                "SIGN API authentication failed.");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var signatureFields = request.IsVisible == "true"
            ? new[]
            {
                new PadesSignatureFieldRequest(
                    request.IsPdfa!,
                    new PadesSignaturePositionRequest(
                        request.Page!.Value,
                        request.Llx!.Value,
                        request.Lly!.Value,
                        request.Urx!.Value,
                        request.Ury!.Value)),
            }
            : null;

        var providerRequest = new AutomaticPadesProviderRequest(
            applicationId,
            request.Pin!,
            [
                new PadesSignatureRequest(
                    requestId,
                    new PadesDocumentRequest(Convert.ToBase64String(document), PdfContentType),
                    request.Packaging!,
                    "BASELINE-B",
                    request.IsVisible!,
                    signatureFields),
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
                    if (!providerMessage.Headers.TryAddWithoutValidation("X-signer-id", request.XSignerId) ||
                        !providerMessage.Headers.TryAddWithoutValidation("Infocert-SAT", request.Sat))
                    {
                        throw new AutomaticPadesSigningException(
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
            throw new AutomaticPadesSigningException(
                StatusCodes.Status502BadGateway,
                "The signing service could not be reached.");
        }
        catch (SignApiConfigurationException)
        {
            throw new AutomaticPadesSigningException(
                StatusCodes.Status503ServiceUnavailable,
                "Automatic PAdES signing is not configured.");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new AutomaticPadesSigningException(
                    StatusCodes.Status502BadGateway,
                    "The signing service rejected the signing credentials.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new AutomaticPadesSigningException(
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
                return new AutomaticPadesSigningResult(
                    signedContent,
                    PdfContentType,
                    $"signed-document-{requestId}.pdf",
                    requestId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
            {
                throw new AutomaticPadesSigningException(
                    StatusCodes.Status502BadGateway,
                    "The signing service returned an invalid response.");
            }
        }
    }

    private static void ValidateRequest(AutomaticPadesSigningRequest request, long maxFileSizeBytes)
    {
        if (maxFileSizeBytes <= 0 || maxFileSizeBytes > int.MaxValue - 65_536)
        {
            throw new AutomaticPadesSigningException(
                StatusCodes.Status503ServiceUnavailable,
                "The signing upload limit is not configured correctly.");
        }

        if (request.File is null)
        {
            throw new AutomaticPadesSigningException(StatusCodes.Status400BadRequest, "Select a PDF document to sign.");
        }
        if (request.File.Length == 0)
        {
            throw new AutomaticPadesSigningException(StatusCodes.Status400BadRequest, "The selected file is empty.");
        }
        if (request.File.Length > maxFileSizeBytes)
        {
            throw new AutomaticPadesSigningException(StatusCodes.Status413PayloadTooLarge, "The selected file exceeds the TrustPlatform upload limit.");
        }
        if (!string.Equals(Path.GetExtension(request.File.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new AutomaticPadesSigningException(StatusCodes.Status400BadRequest, "Only PDF documents are supported for automatic PAdES signing.");
        }
        if (!IsValidCertificateId(request.CertificateId))
        {
            throw new AutomaticPadesSigningException(StatusCodes.Status400BadRequest, "Enter a valid Certificate ID.");
        }
        if (string.IsNullOrWhiteSpace(request.XSignerId) ||
            request.XSignerId.Length > MaxSignerIdLength ||
            ContainsControlCharacters(request.XSignerId))
        {
            throw new AutomaticPadesSigningException(StatusCodes.Status400BadRequest, "Enter a valid X-signer-id.");
        }
        if (string.IsNullOrWhiteSpace(request.Pin))
        {
            throw new AutomaticPadesSigningException(StatusCodes.Status400BadRequest, "Enter the signing PIN.");
        }
        if (string.IsNullOrWhiteSpace(request.Sat) || ContainsControlCharacters(request.Sat))
        {
            throw new AutomaticPadesSigningException(StatusCodes.Status400BadRequest, "Enter a valid Infocert-SAT value.");
        }
        if (!PackagingValues.Contains(request.Packaging, StringComparer.Ordinal))
        {
            throw new AutomaticPadesSigningException(StatusCodes.Status400BadRequest, "Select a supported PAdES packaging value.");
        }
        if (!string.Equals(request.SignatureLevel, "BASELINE-B", StringComparison.Ordinal))
        {
            throw new AutomaticPadesSigningException(StatusCodes.Status400BadRequest, "Select a supported PAdES signature level.");
        }
        if (request.IsVisible is not ("true" or "false"))
        {
            throw new AutomaticPadesSigningException(StatusCodes.Status400BadRequest, "Select whether the PAdES signature is visible.");
        }

        if (request.IsVisible == "true")
        {
            if (request.IsPdfa is not ("true" or "false"))
            {
                throw new AutomaticPadesSigningException(StatusCodes.Status400BadRequest, "Select a valid PDF/A option.");
            }
            if (request.Page is null or < 1 ||
                request.Llx is null ||
                request.Lly is null ||
                request.Urx is null ||
                request.Ury is null)
            {
                throw new AutomaticPadesSigningException(StatusCodes.Status400BadRequest, "Enter valid PAdES signature field coordinates.");
            }
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
            throw new AutomaticPadesSigningException(StatusCodes.Status400BadRequest, "The selected file is empty.");
        }
        if (bytes.Length > maxFileSizeBytes)
        {
            throw new AutomaticPadesSigningException(StatusCodes.Status413PayloadTooLarge, "The selected file exceeds the TrustPlatform upload limit.");
        }
        if (bytes.Length < PdfSignature.Length ||
            !bytes.AsSpan(0, PdfSignature.Length).SequenceEqual(PdfSignature))
        {
            throw new AutomaticPadesSigningException(StatusCodes.Status400BadRequest, "The selected file is not a valid PDF document.");
        }
        if (!string.IsNullOrWhiteSpace(file.ContentType) &&
            !string.Equals(file.ContentType, PdfContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new AutomaticPadesSigningException(StatusCodes.Status400BadRequest, "Only PDF documents are supported for automatic PAdES signing.");
        }

        return bytes;
    }

    private static byte[] ReadSignedDocument(JsonElement payload, string requestId)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
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
            throw new AutomaticPadesSigningException(
                StatusCodes.Status502BadGateway,
                "The signing service could not complete the request.");
        }
        if (!result.TryGetProperty("signedDocument", out var signedDocument) ||
            signedDocument.ValueKind != JsonValueKind.Object ||
            !signedDocument.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.String ||
            !signedDocument.TryGetProperty("contentType", out var contentType) ||
            contentType.ValueKind != JsonValueKind.String ||
            !string.Equals(contentType.GetString(), PdfContentType, StringComparison.OrdinalIgnoreCase))
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

    private sealed record AutomaticPadesProviderRequest(
        [property: JsonPropertyName("applicationId")] string ApplicationId,
        [property: JsonPropertyName("pin")] string Pin,
        [property: JsonPropertyName("padesSignatures")] IReadOnlyList<PadesSignatureRequest> Signatures);

    private sealed record PadesSignatureRequest(
        [property: JsonPropertyName("requestId")] string RequestId,
        [property: JsonPropertyName("document")] PadesDocumentRequest Document,
        [property: JsonPropertyName("packaging")] string Packaging,
        [property: JsonPropertyName("signatureLevel")] string SignatureLevel,
        [property: JsonPropertyName("isVisible")] string IsVisible,
        [property: JsonPropertyName("signatureFields"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<PadesSignatureFieldRequest>? SignatureFields);

    private sealed record PadesDocumentRequest(
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("contentType")] string ContentType);

    private sealed record PadesSignatureFieldRequest(
        [property: JsonPropertyName("isPdfa")] string IsPdfa,
        [property: JsonPropertyName("position")] PadesSignaturePositionRequest Position);

    private sealed record PadesSignaturePositionRequest(
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("llx")] int Llx,
        [property: JsonPropertyName("lly")] int Lly,
        [property: JsonPropertyName("urx")] int Urx,
        [property: JsonPropertyName("ury")] int Ury);
}

internal static class AutomaticPadesEndpoints
{
    public static WebApplication MapAutomaticPadesEndpoint(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapGet("/api/signing/automatic/pades/defaults", (
                HttpContext context,
                IOptions<SignApiOptions> options) =>
            {
                var configured = options.Value.Defaults.AutomaticSigning;
                var pades = configured.Pades;
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(new AutomaticPadesDevelopmentDefaultsResponse(
                    configured.CertificateId,
                    configured.XSignerId,
                    configured.Pin,
                    configured.InfocertSat,
                    configured.Packaging,
                    "BASELINE-B",
                    pades.IsVisible,
                    pades.IsPdfa,
                    pades.Page,
                    pades.Llx,
                    pades.Lly,
                    pades.Urx,
                    pades.Ury));
            })
            .WithName("GetAutomaticPadesDevelopmentDefaults")
            .WithSummary("Get local-development defaults for Automatic PAdES");
        }

        var maxFileSizeBytes = app.Services
            .GetRequiredService<IOptions<SignApiOptions>>()
            .Value.Upload.MaxFileSizeBytes;
        var requestLimit = maxFileSizeBytes is > 0 and <= int.MaxValue - SigningEndpoints.MultipartOverheadBytes
            ? checked(maxFileSizeBytes + SigningEndpoints.MultipartOverheadBytes)
            : 10_551_296;

        app.MapPost("/api/signing/automatic/pades", async (
            [FromForm] AutomaticPadesSigningRequest request,
            HttpRequest httpRequest,
            IAutomaticPadesSigningService signingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var form = await httpRequest.ReadFormAsync(cancellationToken);
                if (form.Files.Count != 1 ||
                    !string.Equals(form.Files[0].Name, "file", StringComparison.Ordinal))
                {
                    return Results.BadRequest(new SigningErrorResponse("Upload exactly one PDF document."));
                }

                var result = await signingService.SignAsync(request, cancellationToken);
                httpRequest.HttpContext.Response.Headers.Append("X-TrustPlatform-Request-Id", result.RequestId);
                httpRequest.HttpContext.Response.Headers.CacheControl = "no-store";
                httpRequest.HttpContext.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.File(result.Content, result.ContentType, result.FileName);
            }
            catch (AutomaticPadesSigningException exception)
            {
                return Results.Json(
                    new SigningErrorResponse(exception.Message),
                    statusCode: exception.StatusCode);
            }
        })
        .WithName("SignAutomaticPades")
        .WithSummary("Sign one PDF document using automatic PAdES")
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(requestLimit))
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
        .Produces<SigningErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<SigningErrorResponse>(StatusCodes.Status413PayloadTooLarge)
        .Produces<SigningErrorResponse>(StatusCodes.Status502BadGateway)
        .Produces<SigningErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}
