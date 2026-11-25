using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace TrustPlatform.Api.SignApi;

public sealed record ProviderCertificate(
    [property: JsonPropertyName("certificate")] string? Certificate,
    [property: JsonPropertyName("ids")] IReadOnlyList<string>? Ids,
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("issuer")] string? Issuer,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("expirationDate")] string? ExpirationDate);

public sealed record ProviderCertificateChainItem(
    [property: JsonPropertyName("certificate")] string? Certificate,
    [property: JsonPropertyName("ids")] IReadOnlyList<string>? Ids,
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("issuer")] string? Issuer,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("expirationDate")] string? ExpirationDate);

public sealed record CertificateChainItem(
    [property: JsonPropertyName("certificate")] string Certificate,
    [property: JsonPropertyName("ids")] IReadOnlyList<string> Ids,
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("issuer")] string? Issuer,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("expirationDate")] string? ExpirationDate);

public sealed record CertificateListResponse(
    [property: JsonPropertyName("certificates")] IReadOnlyList<CertificateSummary> Certificates);

public sealed record CertificateSummary(
    [property: JsonPropertyName("ids")] IReadOnlyList<string> Ids,
    [property: JsonPropertyName("aliasId")] string? AliasId,
    [property: JsonPropertyName("keyId")] string? KeyId,
    [property: JsonPropertyName("dnQualifierId")] string? DnQualifierId,
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("issuer")] string? Issuer,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("expirationDate")] string? ExpirationDate,
    [property: JsonPropertyName("isExpired")] bool? IsExpired);

public sealed record CertificateDetailResponse(
    [property: JsonPropertyName("certificateId")] string CertificateId,
    [property: JsonPropertyName("ids")] IReadOnlyList<string> Ids,
    [property: JsonPropertyName("aliasId")] string? AliasId,
    [property: JsonPropertyName("keyId")] string? KeyId,
    [property: JsonPropertyName("dnQualifierId")] string? DnQualifierId,
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("issuer")] string? Issuer,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("expirationDate")] string? ExpirationDate,
    [property: JsonPropertyName("isExpired")] bool? IsExpired,
    [property: JsonPropertyName("certificateAvailable")] bool CertificateAvailable);

public sealed record CertificateQueryDefaultsResponse(
    string CertificateId,
    string XSignerId);

public sealed record CertificateListRequest(string? XSignerId);

public sealed record CertificateDetailsRequest(string? CertificateId, string? XSignerId);

public sealed record CertificateChainRequest(string? XSignerId);

public static class CertificateEndpoints
{
    public static void MapCertificateEndpoints(this WebApplication app)
    {
        app.MapGet("/api/certificates/defaults", (
            IOptions<SignApiOptions> options,
            HttpContext httpContext) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            var configured = options.Value.Defaults.AutomaticSigning;
            return Results.Ok(new CertificateQueryDefaultsResponse(
                configured.CertificateId,
                configured.XSignerId));
        })
        .WithName("GetCertificateQueryDefaults")
        .WithSummary("Get the configured certificate-query identifiers");

        app.MapPost("/api/certificates", async (
            CertificateListRequest? request,
            SignApiClient signApiClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var xSignerId = request?.XSignerId;
            if (!IsValidXSignerId(xSignerId))
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate query failed",
                    StatusCodes.Status400BadRequest,
                    "A valid X-signer-id is required.");
            }

            try
            {
                var providerCertificates = await signApiClient.GetCertificatesAsync(xSignerId!, cancellationToken);
                return Results.Ok(new CertificateListResponse(providerCertificates.Select(MapCertificateSummary).ToArray()));
            }
            catch (SignApiRequestException exception)
            {
                return CreateProviderError(
                    httpContext,
                    exception,
                    "Certificate query failed",
                    StatusCodes.Status502BadGateway,
                    "The certificate service could not complete the request.");
            }
            catch (SignApiConfigurationException)
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate query failed",
                    StatusCodes.Status503ServiceUnavailable,
                    "The certificate service is not configured.");
            }
            catch (JsonException)
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate query failed",
                    StatusCodes.Status502BadGateway,
                    "The certificate service returned an invalid response.");
            }
        })
        .WithName("RetrieveCertificates")
        .WithSummary("Retrieve the SIGN API certificates list");

        app.MapPost("/api/certificates/configured", async (
            CertificateDetailsRequest? request,
            SignApiClient signApiClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var certificateId = request?.CertificateId;
            var xSignerId = request?.XSignerId;

            if (!IsSafeCertificateId(certificateId))
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate details unavailable",
                    StatusCodes.Status400BadRequest,
                    "A valid certificate ID is required.");
            }

            if (!IsValidXSignerId(xSignerId))
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate details unavailable",
                    StatusCodes.Status400BadRequest,
                    "A valid X-signer-id is required.");
            }

            try
            {
                var certificate = await signApiClient.GetCertificateAsync(certificateId!, xSignerId!, cancellationToken);
                return Results.Ok(MapCertificateDetail(certificateId!, certificate));
            }
            catch (SignApiRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                return Results.Json(
                    new
                    {
                        title = "The requested certificate was not found.",
                        status = StatusCodes.Status404NotFound,
                        correlationId = httpContext.TraceIdentifier,
                        message = "The requested certificate could not be retrieved.",
                    },
                    statusCode: StatusCodes.Status404NotFound);
            }
            catch (SignApiRequestException exception)
            {
                return CreateProviderError(
                    httpContext,
                    exception,
                    "Certificate details unavailable",
                    StatusCodes.Status502BadGateway,
                    "The certificate service could not complete the request.");
            }
            catch (SignApiConfigurationException)
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate details unavailable",
                    StatusCodes.Status503ServiceUnavailable,
                    "The certificate service is not configured.");
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new
                {
                    title = "The configured certificate ID is invalid.",
                    status = StatusCodes.Status400BadRequest,
                    correlationId = httpContext.TraceIdentifier,
                });
            }
            catch (JsonException)
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate details unavailable",
                    StatusCodes.Status502BadGateway,
                    "The certificate service returned an invalid response.");
            }
        })
        .WithName("RetrieveCertificateDetails")
        .WithSummary("Retrieve certificate details by certificate ID");

        app.MapGet("/api/certificates/{certificateId}/chain", async (
            string certificateId,
            IOptions<SignApiOptions> options,
            SignApiClient signApiClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!IsSafeCertificateId(certificateId))
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate chain unavailable",
                    StatusCodes.Status400BadRequest,
                    "A valid certificate ID is required.");
            }

            var defaults = options.Value.Defaults.AutomaticSigning;
            var signerId = defaults.XSignerId;
            if (string.IsNullOrWhiteSpace(signerId) || signerId.Any(char.IsControl))
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate chain unavailable",
                    StatusCodes.Status503ServiceUnavailable,
                    "The certificate chain service is not configured.");
            }

            try
            {
                var chain = await signApiClient.GetCertificateChainAsync(
                    certificateId,
                    signerId,
                    cancellationToken);
                return Results.Ok(chain.Select(MapCertificateChainItem).ToArray());
            }
            catch (SignApiRequestException exception)
            {
                return CreateProviderError(
                    httpContext,
                    exception,
                    "Certificate chain unavailable",
                    StatusCodes.Status502BadGateway,
                    "The certificate chain service could not complete the request.");
            }
            catch (SignApiConfigurationException)
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate chain unavailable",
                    StatusCodes.Status503ServiceUnavailable,
                    "The certificate chain service is not configured.");
            }
            catch (TaskCanceledException)
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate chain unavailable",
                    StatusCodes.Status504GatewayTimeout,
                    "The certificate chain service timed out.");
            }
            catch (HttpRequestException)
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate chain unavailable",
                    StatusCodes.Status502BadGateway,
                    "The certificate chain service could not be reached.");
            }
            catch (ArgumentException)
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate chain unavailable",
                    StatusCodes.Status400BadRequest,
                    "A valid certificate ID is required.");
            }
        })
        .WithName("RetrieveCertificateChain")
        .WithSummary("Retrieve the complete certificate chain by certificate ID");

        app.MapPost("/api/certificates/{certificateId}/chain", async (
            string certificateId,
            CertificateChainRequest? request,
            SignApiClient signApiClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!IsSafeCertificateId(certificateId))
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate chain unavailable",
                    StatusCodes.Status400BadRequest,
                    "A valid certificate ID is required.");
            }

            var signerId = request?.XSignerId;
            if (!IsValidXSignerId(signerId))
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate chain unavailable",
                    StatusCodes.Status400BadRequest,
                    "A valid X-signer-id is required.");
            }

            try
            {
                var chain = await signApiClient.GetCertificateChainAsync(
                    certificateId,
                    signerId!,
                    cancellationToken);
                return Results.Ok(chain.Select(MapCertificateChainItem).ToArray());
            }
            catch (SignApiRequestException exception)
            {
                return CreateProviderError(
                    httpContext,
                    exception,
                    "Certificate chain unavailable",
                    StatusCodes.Status502BadGateway,
                    "The certificate chain service could not complete the request.");
            }
            catch (SignApiConfigurationException)
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate chain unavailable",
                    StatusCodes.Status503ServiceUnavailable,
                    "The certificate chain service is not configured.");
            }
            catch (TaskCanceledException)
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate chain unavailable",
                    StatusCodes.Status504GatewayTimeout,
                    "The certificate chain service timed out.");
            }
            catch (HttpRequestException)
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate chain unavailable",
                    StatusCodes.Status502BadGateway,
                    "The certificate chain service could not be reached.");
            }
            catch (ArgumentException)
            {
                return CreateSafeError(
                    httpContext,
                    "Certificate chain unavailable",
                    StatusCodes.Status400BadRequest,
                    "A valid certificate ID is required.");
            }
        })
        .WithName("RetrieveCertificateChainWithSigner")
        .WithSummary("Retrieve the complete certificate chain using the requested signer ID");
    }

    private static CertificateSummary MapCertificateSummary(ProviderCertificate certificate)
    {
        var ids = certificate.Ids ?? [];
        return new CertificateSummary(
            ids,
            ExtractId(ids, "urn:alias:"),
            ExtractId(ids, "urn:key:"),
            ExtractId(ids, "urn:dnqqualifier:"),
            certificate.Subject,
            certificate.Issuer,
            certificate.Status,
            certificate.ExpirationDate,
            ComputeIsExpired(certificate.ExpirationDate));
    }

    private static CertificateDetailResponse MapCertificateDetail(string certificateId, ProviderCertificate certificate)
    {
        var ids = certificate.Ids ?? [];
        return new CertificateDetailResponse(
            certificateId,
            ids,
            ExtractId(ids, "urn:alias:"),
            ExtractId(ids, "urn:key:"),
            ExtractId(ids, "urn:dnqqualifier:"),
            certificate.Subject,
            certificate.Issuer,
            certificate.Status,
            certificate.ExpirationDate,
            ComputeIsExpired(certificate.ExpirationDate),
            !string.IsNullOrWhiteSpace(certificate.Certificate));
    }

    private static CertificateChainItem MapCertificateChainItem(ProviderCertificateChainItem item)
    {
        var expirationDate = string.IsNullOrWhiteSpace(item.ExpirationDate)
            ? null
            : DateTimeOffset.TryParse(item.ExpirationDate, out _)
                ? item.ExpirationDate
                : null;

        return new CertificateChainItem(
            item.Certificate ?? string.Empty,
            item.Ids ?? [],
            item.Subject,
            item.Issuer,
            NormalizeCertificateStatus(item.Status),
            expirationDate);
    }

    private static string NormalizeCertificateStatus(string? status)
    {
        if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return "active";
        }
        if (string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase))
        {
            return "expired";
        }
        if (string.Equals(status, "revoked", StringComparison.OrdinalIgnoreCase))
        {
            return "revoked";
        }
        if (string.Equals(status, "suspended", StringComparison.OrdinalIgnoreCase))
        {
            return "suspended";
        }

        return "unknown";
    }

    private static string? ExtractId(IReadOnlyList<string> ids, string prefix)
    {
        if (ids.Count == 0)
        {
            return null;
        }

        foreach (var id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id) && id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return id[prefix.Length..];
            }
        }

        return null;
    }

    private static bool? ComputeIsExpired(string? expirationDate)
    {
        if (string.IsNullOrWhiteSpace(expirationDate))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(expirationDate, out var parsed))
        {
            return null;
        }

        return parsed.UtcDateTime < DateTime.UtcNow;
    }

    private static bool IsSafeCertificateId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Length > 128 || value.Any(char.IsControl))
        {
            return false;
        }

        return value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '+' or '_' or '-');
    }

    private static bool IsValidXSignerId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 256 &&
        !value.Any(char.IsControl);

    private static IResult CreateSafeError(
        HttpContext context,
        string title,
        int statusCode,
        string message)
    {
        return Results.Json(
            new
            {
                title,
                status = statusCode,
                correlationId = context.TraceIdentifier,
                message,
            },
            statusCode: statusCode);
    }

    private static IResult CreateProviderError(
        HttpContext httpContext,
        SignApiRequestException exception,
        string fallbackTitle,
        int fallbackStatus,
        string fallbackMessage)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = exception.StatusCode == HttpStatusCode.NotFound
                ? fallbackTitle == "Certificate details unavailable"
                    ? "The requested certificate was not found."
                    : fallbackTitle
                : fallbackTitle,
            ["status"] = (int)exception.StatusCode,
            ["correlationId"] = httpContext.TraceIdentifier,
            ["message"] = exception.SafeMessage ?? fallbackMessage,
        };

        if (httpContext.RequestServices.GetService<IWebHostEnvironment>()?.IsDevelopment() == true)
        {
            payload["providerStatus"] = (int)exception.StatusCode;
            payload["providerReasonPhrase"] = exception.ReasonPhrase;
            payload["providerContentType"] = exception.ContentType;
            payload["providerCode"] = exception.SafeCode;
            payload["detail"] = exception.SafeDetail;
            payload["providerPath"] = exception.ProviderPath;
        }

        return Results.Json(payload, statusCode: (int)exception.StatusCode);
    }
}
