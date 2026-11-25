using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TrustPlatform.Api.ValidationApi;

namespace TrustPlatform.Api.Tests;

public sealed class ValidationApiEndpointTests
{
    private const string AccessToken = "test-validation-access-token";
    private const string Username = "test-validation-user";
    private const string Password = "test-validation-password";

    [Fact]
    public async Task PostValidationSignature_SendsConfirmedJsonContractAndReturnsSanitizedBusinessResult()
    {
        CapturedProviderRequest? captured = null;
        var factory = CreateFactory(async (request, cancellationToken) =>
        {
            captured = new CapturedProviderRequest(
                request.RequestUri,
                request.Headers.Authorization,
                request.Headers.Accept.ToString(),
                request.Content?.Headers.ContentType?.MediaType,
                await request.Content!.ReadAsStringAsync(cancellationToken));
            return JsonResponse(HttpStatusCode.OK, """
                {
                  "valid": false,
                  "validationReport": {
                    "status": "INVALID",
                    "certificates": [{ "certificateId": "cert-1", "data": "certificate-base64" }]
                  }
                }
                """);
        });
        using var client = factory.CreateClient();
        using var content = CreateUpload("C:\\private\\sample.p7m", [0, 1, 2, 255]);

        using var response = await client.PostAsync("/api/validation/signature", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(captured);
        Assert.Equal("https://validation.example/validation/signature/v3", captured.Uri!.ToString());
        Assert.Equal("Bearer", captured.Authorization!.Scheme);
        Assert.Equal(AccessToken, captured.Authorization.Parameter);
        Assert.Contains("application/json", captured.Accept, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("application/json", captured.ContentType);
        Assert.Contains(ValidationApiTokenService.HttpClientName, factory.TokenClientFactory.RequestedClientNames);
        Assert.Contains("grant_type=client_credentials", factory.TokenRequestBody);
        Assert.Contains("scope=validation", factory.TokenRequestBody);

        using var providerJson = JsonDocument.Parse(captured.Body);
        var root = providerJson.RootElement;
        Assert.Equal(
            ["signedDocument", "validationOptions"],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        var signedDocument = root.GetProperty("signedDocument");
        Assert.Equal(["data", "name"], signedDocument.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(Convert.ToBase64String([0, 1, 2, 255]), signedDocument.GetProperty("data").GetString());
        Assert.Equal("sample.p7m", signedDocument.GetProperty("name").GetString());
        var validationOptions = root.GetProperty("validationOptions");
        Assert.Equal(
            ["validationLevel", "extractOriginals", "includeEtsiValidationReport"],
            validationOptions.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("ARCHIVAL_DATA", validationOptions.GetProperty("validationLevel").GetString());
        Assert.Equal(JsonValueKind.False, validationOptions.GetProperty("extractOriginals").ValueKind);
        Assert.Equal(JsonValueKind.False, validationOptions.GetProperty("includeEtsiValidationReport").ValueKind);

        using var responseJson = JsonDocument.Parse(body);
        Assert.False(responseJson.RootElement.GetProperty("valid").GetBoolean());
        var safeCertificate = Assert.Single(
            responseJson.RootElement
                .GetProperty("validationReport")
                .GetProperty("certificates")
                .EnumerateArray());
        Assert.False(safeCertificate.TryGetProperty("data", out _));
        Assert.DoesNotContain("certificate-base64", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostValidationSignature_MapsTheProviderTotalFailedSampleAsSuccessfulBusinessResult()
    {
        var factory = CreateFactory((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """
            {
              "valid": false,
              "validationReport": {
                "signatureValidationPolicyName": "QES AdESQC TL based",
                "validationTime": 1794320342761,
                "documentFileName": "sample_cades_enveloping_b-signed.xml.p7m",
                "signaturesCount": 1,
                "validSignaturesCount": 0,
                "status": "TOTAL_FAILED",
                "statusDetails": "The available information is sufficient to ascertain the signature to be TOTAL-FAILED",
                "signatures": [
                  {
                    "signatureId": "S-test",
                    "signatureLevel": "CAdES_BASELINE_B",
                    "signatureFormat": "CAdES",
                    "signatureQualification": "INDETERMINATE_QESIG",
                    "signingTime": 1632317803000,
                    "signerName": "Test Signer",
                    "status": "INDETERMINATE",
                    "messages": [
                      {
                        "level": "ERROR",
                        "section": "ADES",
                        "key": "BBB_XCV_SUB_ANS",
                        "value": "The certificate validation is not conclusive!"
                      }
                    ],
                    "certificateChain": [],
                    "timestampIds": [],
                    "signedAttributes": {
                      "contentType": "1.2.840.113549.1.7.1",
                      "claimedSigningTime": "2021-09-22T13:36:43Z",
                      "messageDigest": "test",
                      "signatureAlgorithm": "RSA with SHA256"
                    }
                  }
                ],
                "timestamps": [],
                "certificates": []
              }
            }
            """)));
        using var client = factory.CreateClient();
        using var content = CreateUpload("sample.p7m", [1, 2, 3]);

        using var response = await client.PostAsync("/api/validation/signature", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.False(root.GetProperty("valid").GetBoolean());
        var report = root.GetProperty("validationReport");
        Assert.Equal("TOTAL_FAILED", report.GetProperty("status").GetString());
        Assert.Equal(1794320342761, report.GetProperty("validationTime").GetInt64());
        Assert.Equal(1, report.GetProperty("signaturesCount").GetInt32());
        Assert.Equal(0, report.GetProperty("validSignaturesCount").GetInt32());
        Assert.Equal(JsonValueKind.Array, report.GetProperty("timestamps").ValueKind);
        Assert.Empty(report.GetProperty("timestamps").EnumerateArray());
        Assert.Empty(report.GetProperty("certificates").EnumerateArray());
        var signature = Assert.Single(report.GetProperty("signatures").EnumerateArray());
        Assert.Equal("S-test", signature.GetProperty("signatureId").GetString());
        Assert.Equal(1632317803000, signature.GetProperty("signingTime").GetInt64());
        var message = Assert.Single(signature.GetProperty("messages").EnumerateArray());
        Assert.Equal("ERROR", message.GetProperty("level").GetString());
        Assert.Equal("ADES", message.GetProperty("section").GetString());
        Assert.Equal("BBB_XCV_SUB_ANS", message.GetProperty("key").GetString());
        Assert.Equal("The certificate validation is not conclusive!", message.GetProperty("value").GetString());
    }

    [Fact]
    public async Task PostValidationSignature_IgnoresUnknownPropertiesAndAcceptsMissingOptionalFields()
    {
        var factory = CreateFactory((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """
            {
              "valid": true,
              "providerExtension": { "newField": "ignored" },
              "validationReport": {
                "signatures": [
                  { "signatureId": "S-optional", "providerExtension": true }
                ]
              }
            }
            """)));
        using var client = factory.CreateClient();
        using var content = CreateUpload("sample.xml", [1, 2, 3]);

        using var response = await client.PostAsync("/api/validation/signature", content);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("valid").GetBoolean());
        var signature = Assert.Single(
            json.RootElement.GetProperty("validationReport").GetProperty("signatures").EnumerateArray());
        Assert.Equal("S-optional", signature.GetProperty("signatureId").GetString());
        Assert.Equal(JsonValueKind.Null, signature.GetProperty("status").ValueKind);
    }

    [Fact]
    public async Task PostValidationSignature_DevelopmentMalformedSuccessfulBodyReturnsSafeParseDiagnostics()
    {
        const string malformedBody = """{"valid": false, "access_token": "do-not-return", "secret": """;
        var factory = CreateFactory((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, malformedBody)));
        using var client = factory.CreateClient();
        using var content = CreateUpload("sample.pdf", [1, 2, 3]);

        using var response = await client.PostAsync("/api/validation/signature", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(200, json.RootElement.GetProperty("providerStatus").GetInt32());
        Assert.Equal("application/json; charset=utf-8", json.RootElement.GetProperty("providerContentType").GetString());
        Assert.Equal("Malformed JSON", json.RootElement.GetProperty("responseShape").GetString());
        Assert.True(json.RootElement.GetProperty("parsingError").GetString()?.Length > 0);
        Assert.False(json.RootElement.TryGetProperty("responseExcerpt", out _));
        Assert.DoesNotContain("do-not-return", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostValidationSignature_ProductionParseFailureDoesNotExposeProviderDiagnostics()
    {
        var factory = CreateFactory(
            (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"valid": "not-a-boolean"}""")),
            environment: "Production");
        using var client = factory.CreateClient();
        using var content = CreateUpload("sample.pdf", [1, 2, 3]);

        using var response = await client.PostAsync("/api/validation/signature", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(
            ["correlationId", "status", "title"],
            json.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.DoesNotContain("providerStatus", body, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-boolean", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostValidationSignature_DevelopmentParseDiagnosticsRedactCertificateDataAndLimitExcerpt()
    {
        var certificateData = new string('A', 1_000);
        var requestData = Convert.ToBase64String(Encoding.UTF8.GetBytes("private-document"));
        var providerBody = $$"""
            {
              "valid": "not-a-boolean",
              "access_token": "{{AccessToken}}",
              "signedDocument": { "data": "{{requestData}}" },
              "validationReport": {
                "certificates": [{ "certificateId": "C-1", "data": "{{certificateData}}" }]
              },
              "largeExtension": "{{new string('x', 6_000)}}"
            }
            """;
        var factory = CreateFactory((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, providerBody)));
        using var client = factory.CreateClient();
        using var content = CreateUpload("sample.pdf", Encoding.UTF8.GetBytes("private-document"));

        using var response = await client.PostAsync("/api/validation/signature", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var excerpt = json.RootElement.GetProperty("responseExcerpt").GetString()!;
        Assert.True(excerpt.Length <= 5_000);
        Assert.DoesNotContain(certificateData, excerpt, StringComparison.Ordinal);
        Assert.DoesNotContain(requestData, excerpt, StringComparison.Ordinal);
        Assert.DoesNotContain(AccessToken, excerpt, StringComparison.Ordinal);
        using var excerptJson = JsonDocument.Parse(excerpt);
        Assert.Equal(
            "[redacted]",
            excerptJson.RootElement.GetProperty("signedDocument").GetProperty("data").GetString());
        var certificate = Assert.Single(
            excerptJson.RootElement.GetProperty("validationReport").GetProperty("certificates").EnumerateArray());
        Assert.False(certificate.TryGetProperty("data", out _));
    }

    [Fact]
    public async Task PostValidationSignature_DevelopmentErrorIncludesSafeProviderMetadataAndCorrelationId()
    {
        var documentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("private-document"));
        var providerError = $$"""
            {
              "errorCode": "INVALID_REQUEST",
              "message": "signedDocument.data is required; {{AccessToken}} {{Password}} {{documentBase64}}",
              "detail": "Basic {{Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"))}} C:\\private\\document.p7m at Example.Method()"
            }
            """;
        var factory = CreateFactory((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.BadRequest, providerError)));
        using var client = factory.CreateClient();
        using var content = CreateUpload("document.p7m", Encoding.UTF8.GetBytes("private-document"));

        using var response = await client.PostAsync("/api/validation/signature", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(400, root.GetProperty("providerStatus").GetInt32());
        Assert.Equal("Bad Request", root.GetProperty("providerReasonPhrase").GetString());
        Assert.Contains("application/json", root.GetProperty("providerContentType").GetString());
        Assert.Equal("INVALID_REQUEST", root.GetProperty("providerCode").GetString());
        Assert.Contains("signedDocument.data is required", root.GetProperty("message").GetString());
        Assert.True(root.GetProperty("correlationId").GetString()?.Length > 0);
        Assert.DoesNotContain(AccessToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Username, body, StringComparison.Ordinal);
        Assert.DoesNotContain(documentBase64, body, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\private", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Example.Method", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostValidationSignature_ProductionErrorDoesNotExposeProviderBody()
    {
        var factory = CreateFactory(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent(
                    "<html><body>gateway internal response</body></html>",
                    Encoding.UTF8,
                    "text/html"),
            }),
            environment: "Production");
        using var client = factory.CreateClient();
        using var content = CreateUpload("document.pdf", [1, 2, 3]);

        using var response = await client.PostAsync("/api/validation/signature", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(
            ["correlationId", "status", "title"],
            json.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.DoesNotContain("gateway internal response", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostValidationSignature_DevelopmentPlainTextErrorIsLengthLimitedAndSanitized()
    {
        var plainText = $"Provider error Bearer {AccessToken}\r\nC:\\private\\document.p7m {new string('x', 1_500)}";
        var factory = CreateFactory((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(plainText, Encoding.UTF8, "text/plain"),
            }));
        using var client = factory.CreateClient();
        using var content = CreateUpload("document.xml", [1, 2, 3]);

        using var response = await client.PostAsync("/api/validation/signature", content);
        var body = await response.Content.ReadAsStringAsync();

        using var json = JsonDocument.Parse(body);
        var message = json.RootElement.GetProperty("message").GetString()!;
        Assert.True(message.Length <= 1_000);
        Assert.Contains("[redacted]", message, StringComparison.Ordinal);
        Assert.DoesNotContain(AccessToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\private", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", message, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostValidationSignature_DevelopmentHtmlErrorUsesGenericGatewayMessage()
    {
        var factory = CreateFactory((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent(
                    "<html><body>gateway internal response</body></html>",
                    Encoding.UTF8,
                    "text/html"),
            }));
        using var client = factory.CreateClient();
        using var content = CreateUpload("document.pdf", [1, 2, 3]);

        using var response = await client.PostAsync("/api/validation/signature", content);
        var body = await response.Content.ReadAsStringAsync();

        using var json = JsonDocument.Parse(body);
        Assert.Equal(
            "The validation provider or an upstream gateway rejected the request.",
            json.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("<html>", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gateway internal response", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostValidationSignature_RequiresExactlyOneAllowedNonEmptyFile()
    {
        var providerCalled = false;
        var factory = CreateFactory((_, _) =>
        {
            providerCalled = true;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"valid":true}"""));
        });
        using var client = factory.CreateClient();

        using var missingFileResponse = await client.PostAsync(
            "/api/validation/signature",
            new MultipartFormDataContent());
        using var multipleFiles = new MultipartFormDataContent();
        multipleFiles.Add(new ByteArrayContent([1]), "file", "first.pdf");
        multipleFiles.Add(new ByteArrayContent([2]), "file", "second.xml");
        using var multipleFileResponse = await client.PostAsync("/api/validation/signature", multipleFiles);
        using var unsupportedFile = CreateUpload("document.txt", [1]);
        using var unsupportedFileResponse = await client.PostAsync("/api/validation/signature", unsupportedFile);
        using var emptyFile = CreateUpload("document.xml", []);
        using var emptyFileResponse = await client.PostAsync("/api/validation/signature", emptyFile);

        Assert.Equal(HttpStatusCode.BadRequest, missingFileResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, multipleFileResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedFileResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyFileResponse.StatusCode);
        Assert.False(providerCalled);
    }

    [Fact]
    public async Task PostValidationSignature_EnforcesConfiguredFileSizeLimit()
    {
        var providerCalled = false;
        var factory = CreateFactory(
            (_, _) =>
            {
                providerCalled = true;
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"valid":true}"""));
            },
            maxFileSizeBytes: 3);
        using var client = factory.CreateClient();
        using var content = CreateUpload("document.pdf", [1, 2, 3, 4]);

        using var response = await client.PostAsync("/api/validation/signature", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(providerCalled);
    }

    private static ValidationApiTestFactory CreateFactory(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> providerSend,
        string environment = "Development",
        long maxFileSizeBytes = 10_485_760) =>
        new(providerSend, environment, maxFileSizeBytes);

    private static MultipartFormDataContent CreateUpload(string fileName, byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", fileName);
        return content;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed record CapturedProviderRequest(
        Uri? Uri,
        AuthenticationHeaderValue? Authorization,
        string Accept,
        string? ContentType,
        string Body);

    private sealed class ValidationApiTestFactory : WebApplicationFactory<Program>
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _providerSend;
        private readonly string _environment;
        private readonly long _maxFileSizeBytes;
        private readonly StubHttpClientFactory _tokenClientFactory;
        public string? TokenRequestBody { get; private set; }
        public StubHttpClientFactory TokenClientFactory => _tokenClientFactory;

        public ValidationApiTestFactory(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> providerSend,
            string environment,
            long maxFileSizeBytes)
        {
            _providerSend = providerSend;
            _environment = environment;
            _maxFileSizeBytes = maxFileSizeBytes;
            _tokenClientFactory = new StubHttpClientFactory(new StubHttpMessageHandler(async (request, cancellationToken) =>
            {
                TokenRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return JsonResponse(
                    HttpStatusCode.OK,
                    $$"""{"access_token":"{{AccessToken}}","token_type":"Bearer","expires_in":300,"scope":"validation"}""");
            }));
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ValidationApi:IdentityProviderUrl"] = "https://identity.example/auth",
                    ["ValidationApi:BaseUrl"] = "https://validation.example/validation/signature/v3",
                    ["ValidationApi:Authentication:Username"] = Username,
                    ["ValidationApi:Authentication:Password"] = Password,
                    ["ValidationApi:Authentication:GrantType"] = "client_credentials",
                    ["ValidationApi:Authentication:Scope"] = "validation",
                    ["ValidationApi:Upload:MaxFileSizeBytes"] = _maxFileSizeBytes.ToString(),
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(_tokenClientFactory);
                services.RemoveAll<ValidationApiClient>();
                services.AddSingleton<ValidationApiClient>(serviceProvider =>
                    new ValidationApiClient(
                        new HttpClient(new StubHttpMessageHandler(_providerSend)),
                        serviceProvider.GetRequiredService<IOptions<ValidationApiOptions>>()));
            });
        }
    }
}
