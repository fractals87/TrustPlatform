using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrustPlatform.Api.ValidationApi;

public sealed class ValidationApiOptions
{
    public const string SectionName = "ValidationApi";

    public string IdentityProviderUrl { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public ValidationApiAuthenticationOptions Authentication { get; set; } = new();

    public ValidationApiUploadOptions Upload { get; set; } = new();
}

public sealed class ValidationApiAuthenticationOptions
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string GrantType { get; set; } = "client_credentials";

    public string Scope { get; set; } = "validation";
}

public sealed class ValidationApiUploadOptions
{
    public long MaxFileSizeBytes { get; set; } = 10_485_760;
}

public sealed class ValidationApiConfigurationException(string message) : Exception(message);

public sealed class ValidationApiAuthenticationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed record ValidationApiAuthenticationDiagnostic(
    string Service,
    bool Authenticated,
    bool TokenAcquired,
    string? TokenType,
    int? ExpiresIn,
    string? Scope,
    bool Cached,
    string? Error);

public sealed record ValidationProviderRequest(
    [property: JsonPropertyName("signedDocument")] ValidationSignedDocument SignedDocument,
    [property: JsonPropertyName("validationOptions")] ValidationOptions ValidationOptions);

public sealed record ValidationSignedDocument(
    [property: JsonPropertyName("data")] string Data,
    [property: JsonPropertyName("name")] string Name);

public sealed record ValidationOptions(
    [property: JsonPropertyName("validationLevel")] string ValidationLevel,
    [property: JsonPropertyName("extractOriginals")] bool ExtractOriginals,
    [property: JsonPropertyName("includeEtsiValidationReport")] bool IncludeEtsiValidationReport);

public sealed record ValidationApiErrorResponse(
    string Title,
    int Status,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ProviderStatus = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProviderReasonPhrase = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProviderContentType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProviderCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResponseShape = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ParsingError = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResponseExcerpt = null);

public sealed record ValidationResponse(
    [property: JsonRequired] bool Valid,
    ValidationReport? ValidationReport);

public sealed record ValidationReport(
    string? SignatureValidationPolicyName,
    string? SignatureValidationPolicyDescription,
    string? SignatureValidationPolicyOid,
    string? QsvsPolicyOid,
    [property: JsonConverter(typeof(ProviderDateValueConverter))] string? ValidationTime,
    string? DocumentFileName,
    int? SignaturesCount,
    int? ValidSignaturesCount,
    string? Status,
    string? StatusDetails,
    IReadOnlyList<SignatureResult>? Signatures,
    IReadOnlyList<TimestampResult>? Timestamps,
    IReadOnlyList<CertificateResult>? Certificates,
    IReadOnlyList<ValidationMessage>? Messages);

public sealed record SignatureResult(
    string? SignatureId,
    string? SignatureLevel,
    string? SignatureFormat,
    string? SignatureQualification,
    [property: JsonConverter(typeof(ProviderDateValueConverter))] string? SigningTime,
    string? SignerName,
    string? Status,
    IReadOnlyList<ValidationMessage>? Messages,
    string? SigningCertificateId,
    IReadOnlyList<string>? CertificateChain,
    IReadOnlyList<string>? TimestampIds,
    SignedAttributes? SignedAttributes);

public sealed record ValidationMessage(
    string? Level,
    string? Section,
    string? Key,
    string? Value);

public sealed record SignedAttributes(
    string? ContentType,
    [property: JsonConverter(typeof(ProviderDateValueConverter))] string? ClaimedSigningTime,
    string? MessageDigest,
    string? SignatureAlgorithm);

public sealed record CertificateResult(
    string? CertificateId,
    string? SubjectDN,
    string? IssuerDN,
    string? SerialNumber,
    [property: JsonConverter(typeof(ProviderDateValueConverter))] string? NotBefore,
    [property: JsonConverter(typeof(ProviderDateValueConverter))] string? NotAfter,
    IReadOnlyList<QcType>? QcTypes);

public sealed record QcType(
    string? Description,
    string? Oid);

public sealed record TimestampResult(
    string? Id,
    string? Type,
    [property: JsonConverter(typeof(ProviderDateValueConverter))] string? DateTime,
    string? Value);

public sealed class ProviderDateValueConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString()!,
            JsonTokenType.Number when reader.TryGetInt64(out var value) =>
                value.ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Number => reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
            _ => throw new JsonException("A provider date value must be a string or number."),
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            writer.WriteNumberValue(number);
            return;
        }

        writer.WriteStringValue(value);
    }
}
