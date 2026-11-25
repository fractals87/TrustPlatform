using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TrustPlatform.Api.SignApi;

namespace TrustPlatform.Api.Tests;

public sealed class AutomaticCadesSigningEndpointTests
{
    private static readonly byte[] PdfBytes = "%PDF-1.7\nmock document"u8.ToArray();

    [Fact]
    public async Task PostAutomaticCades_MapsProviderRequestAndReturnsSignedDocumentDownload()
    {
        string? providerBody = null;
        HttpRequestMessage? providerRequest = null;
        var factory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            async (request, cancellationToken) =>
            {
                providerRequest = request;
                providerBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var body = JsonDocument.Parse(providerBody);
                var requestId = body.RootElement
                    .GetProperty("cadesSignatures")[0]
                    .GetProperty("requestId")
                    .GetString();
                return SignedProviderResponse(requestId!, Convert.ToBase64String("signed PDF payload"u8.ToArray()));
            });
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/signing/automatic/cades",
            CreateForm());
        var signedContent = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pkcs7-mime", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("signed PDF payload", Encoding.UTF8.GetString(signedContent));
        Assert.NotNull(providerRequest);
        Assert.Equal("Bearer", providerRequest.Headers.Authorization!.Scheme);
        Assert.Equal("test-access-token", providerRequest.Headers.Authorization.Parameter);
        Assert.Equal("signer-123", providerRequest.Headers.GetValues("X-signer-id").Single());
        Assert.Equal("sat-sensitive-value", providerRequest.Headers.GetValues("Infocert-SAT").Single());
        Assert.Equal(
            "https://sign.example/api/certificates/INFOCERT%2BCL_EXT_ID_ZORRO_000000001/sign",
            providerRequest.RequestUri!.ToString());
        Assert.Equal("application/json", providerRequest.Content!.Headers.ContentType!.MediaType);
        Assert.NotNull(providerBody);

        using var providerJson = JsonDocument.Parse(providerBody);
        var root = providerJson.RootElement;
        Assert.Equal("desktop-signer", root.GetProperty("applicationId").GetString());
        Assert.Equal("pin-sensitive-value", root.GetProperty("pin").GetString());
        Assert.Equal(3, root.EnumerateObject().Count());
        Assert.False(root.TryGetProperty("certificateId", out _));
        Assert.False(root.TryGetProperty("xSignerId", out _));
        var signature = root.GetProperty("cadesSignatures")[0];
        Assert.Equal(4, signature.EnumerateObject().Count());
        Assert.Equal("ENVELOPED", signature.GetProperty("packaging").GetString());
        Assert.Equal("BASELINE-B", signature.GetProperty("signatureLevel").GetString());
        Assert.Equal(
            Convert.ToBase64String(PdfBytes),
            signature.GetProperty("document").GetProperty("content").GetString());
        Assert.Equal(
            "application/pdf",
            signature.GetProperty("document").GetProperty("contentType").GetString());
        Assert.Equal(2, signature.GetProperty("document").EnumerateObject().Count());
        Assert.False(signature.TryGetProperty("isVisible", out _));
        Assert.False(signature.TryGetProperty("signatureFields", out _));

        var operationId = response.Headers.GetValues("X-TrustPlatform-Request-Id").Single();
        Assert.Equal(signature.GetProperty("requestId").GetString(), operationId);
        Assert.Matches("^signed-document-[0-9a-f]{32}\\.p7m$", response.Content.Headers.ContentDisposition!.FileNameStar!);
    }

    [Fact]
    public async Task PostAutomaticCades_AcceptsEnvelopingPackaging()
    {
        string? providerBody = null;
        var factory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            async (request, cancellationToken) =>
            {
                providerBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var body = JsonDocument.Parse(providerBody);
                var requestId = body.RootElement
                    .GetProperty("cadesSignatures")[0]
                    .GetProperty("requestId")
                    .GetString();
                return SignedProviderResponse(
                    requestId!,
                    Convert.ToBase64String("signed PDF payload"u8.ToArray()));
            });
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/signing/automatic/cades",
            CreateForm(packaging: "ENVELOPING"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(providerBody!);
        Assert.Equal(
            "ENVELOPING",
            json.RootElement.GetProperty("cadesSignatures")[0].GetProperty("packaging").GetString());
    }

    [Theory]
    [InlineData(null, "sat-value", "ENVELOPED", "BASELINE-B", HttpStatusCode.BadRequest)]
    [InlineData("pin-value", null, "ENVELOPED", "BASELINE-B", HttpStatusCode.BadRequest)]
    [InlineData("pin-value", "sat-value", "BAD", "BASELINE-B", HttpStatusCode.BadRequest)]
    [InlineData("pin-value", "sat-value", "ENVELOPED", "BASELINE-T", HttpStatusCode.BadRequest)]
    [InlineData("pin-value", "sat-value", "ENVELOPED", "BASELINE-LT", HttpStatusCode.BadRequest)]
    [InlineData("pin-value", "sat-value", "ENVELOPED", "BASELINE-LTV", HttpStatusCode.BadRequest)]
    [InlineData("pin-value", "sat-value", "ENVELOPED", "UNKNOWN", HttpStatusCode.BadRequest)]
    public async Task PostAutomaticCades_RejectsMissingSecretsAndUnsupportedValues(
        string? pin,
        string? sat,
        string packaging,
        string signatureLevel,
        HttpStatusCode expectedStatus)
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("Token endpoint must not be called."),
            (_, _) => throw new InvalidOperationException("SIGN endpoint must not be called."));
        using var client = factory.CreateClient();
        using var form = CreateForm(pin, sat, packaging, signatureLevel);

        using var response = await client.PostAsync("/api/signing/automatic/cades", form);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.DoesNotContain("pin-value", body, StringComparison.Ordinal);
        Assert.DoesNotContain("sat-value", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostAutomaticCades_RejectsMissingFile()
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("Token endpoint must not be called."),
            (_, _) => throw new InvalidOperationException("SIGN endpoint must not be called."));
        using var client = factory.CreateClient();
        using var form = CreateForm(includeFile: false);

        using var response = await client.PostAsync("/api/signing/automatic/cades", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Upload exactly one PDF document", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostAutomaticCades_RejectsEmptyFile()
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("Token endpoint must not be called."),
            (_, _) => throw new InvalidOperationException("SIGN endpoint must not be called."));
        using var client = factory.CreateClient();
        using var form = CreateForm(fileBytes: []);

        using var response = await client.PostAsync("/api/signing/automatic/cades", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAutomaticCades_RejectsMultipleFiles()
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("Token endpoint must not be called."),
            (_, _) => throw new InvalidOperationException("SIGN endpoint must not be called."));
        using var client = factory.CreateClient();
        using var form = CreateForm();
        form.Add(new ByteArrayContent(PdfBytes), "file", "second.pdf");

        using var response = await client.PostAsync("/api/signing/automatic/cades", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAutomaticCades_RejectsMissingXSignerId()
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("Token endpoint must not be called."),
            (_, _) => throw new InvalidOperationException("SIGN endpoint must not be called."));
        using var client = factory.CreateClient();
        using var form = CreateForm(xSignerId: null);

        using var response = await client.PostAsync("/api/signing/automatic/cades", form);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("X-signer-id", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostAutomaticCades_RejectsMissingCertificateId()
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("Token endpoint must not be called."),
            (_, _) => throw new InvalidOperationException("SIGN endpoint must not be called."));
        using var client = factory.CreateClient();
        using var form = CreateForm(certificateId: null);

        using var response = await client.PostAsync("/api/signing/automatic/cades", form);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Certificate ID", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("../other")]
    [InlineData("https://evil.example")]
    [InlineData("certificate/other")]
    [InlineData("certificate\\other")]
    [InlineData("certificate?query")]
    [InlineData("certificate#fragment")]
    [InlineData("https:evil")]
    [InlineData("..")]
    public async Task PostAutomaticCades_RejectsUnsafeCertificateId(string certificateId)
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("Token endpoint must not be called."),
            (_, _) => throw new InvalidOperationException("SIGN endpoint must not be called."));
        using var client = factory.CreateClient();
        using var form = CreateForm(certificateId: certificateId);

        using var response = await client.PostAsync("/api/signing/automatic/cades", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAutomaticCades_RejectsNonPdfAndForgedContentType()
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("Token endpoint must not be called."),
            (_, _) => throw new InvalidOperationException("SIGN endpoint must not be called."));
        using var client = factory.CreateClient();
        using var form = CreateForm(fileBytes: "not a pdf"u8.ToArray());

        using var response = await client.PostAsync("/api/signing/automatic/cades", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAutomaticCades_RejectsOversizedFile()
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("Token endpoint must not be called."),
            (_, _) => throw new InvalidOperationException("SIGN endpoint must not be called."),
            maxFileSizeBytes: 16);
        using var client = factory.CreateClient();
        using var form = CreateForm(fileBytes: PdfBytes);

        using var response = await client.PostAsync("/api/signing/automatic/cades", form);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("TrustPlatform upload limit", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostAutomaticCades_SanitizesProviderAuthenticationFailure()
    {
        var factory = CreateFactory(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("provider error pin-sensitive-value sat-sensitive-value test-access-token"),
            }),
            (_, _) => throw new InvalidOperationException("SIGN endpoint must not be called."));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/signing/automatic/cades",
            CreateForm());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("SIGN API authentication failed.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("pin-sensitive-value", body, StringComparison.Ordinal);
        Assert.DoesNotContain("sat-sensitive-value", body, StringComparison.Ordinal);
        Assert.DoesNotContain("test-access-token", body, StringComparison.Ordinal);
        Assert.DoesNotContain(SignApiTestConfiguration.ClientSecret, body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task PostAutomaticCades_SanitizesProviderSigningFailure(HttpStatusCode providerStatus)
    {
        var factory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            (_, _) => Task.FromResult(new HttpResponseMessage(providerStatus)
            {
                Content = new StringContent(
                    "provider stack trace pin-sensitive-value sat-sensitive-value test-access-token"),
            }));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/signing/automatic/cades",
            CreateForm());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("provider stack trace", body, StringComparison.Ordinal);
        Assert.DoesNotContain("pin-sensitive-value", body, StringComparison.Ordinal);
        Assert.DoesNotContain("sat-sensitive-value", body, StringComparison.Ordinal);
        Assert.DoesNotContain("test-access-token", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostAutomaticCades_SanitizesMalformedProviderResponse()
    {
        var factory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"applicationId":"desktop-signer","signatureResult":[{"requestId":"other","isOk":true}]}""",
                    Encoding.UTF8,
                    "application/json"),
            }));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/signing/automatic/cades",
            CreateForm());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("invalid response", body, StringComparison.Ordinal);
        Assert.DoesNotContain("test-access-token", body, StringComparison.Ordinal);
        Assert.DoesNotContain("pin-sensitive-value", body, StringComparison.Ordinal);
        Assert.DoesNotContain("sat-sensitive-value", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSigningOptions_ReturnsOnlySafeSupportedValues()
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("Provider must not be called."),
            (_, _) => throw new InvalidOperationException("Provider must not be called."));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/signing/options");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = json.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1024, body.GetProperty("maxFileSizeBytes").GetInt64());
        Assert.Equal(".pdf", body.GetProperty("allowedExtensions")[0].GetString());
        Assert.Equal("application/pdf", body.GetProperty("allowedContentTypes")[0].GetString());
        Assert.Equal("ENVELOPED", body.GetProperty("packagingValues")[0].GetString());
        Assert.Equal("ENVELOPING", body.GetProperty("packagingValues")[1].GetString());
        Assert.Equal("BASELINE-B", body.GetProperty("signatureLevelValues")[0].GetString());
        Assert.DoesNotContain(SignApiTestConfiguration.ClientSecret, json.RootElement.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAutomaticCadesDevelopmentDefaults_ReturnsOnlyDevelopmentFormDefaults()
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("Token endpoint must not be called."),
            (_, _) => throw new InvalidOperationException("SIGN endpoint must not be called."));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/signing/automatic/cades/defaults");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = json.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("certificate-default", body.GetProperty("certificateId").GetString());
        Assert.Equal("signer-default", body.GetProperty("xSignerId").GetString());
        Assert.Equal("pin-default", body.GetProperty("pin").GetString());
        Assert.Equal("sat-default", body.GetProperty("infocertSat").GetString());
        Assert.Equal("ENVELOPING", body.GetProperty("packaging").GetString());
        Assert.Equal("BASELINE-B", body.GetProperty("signatureLevel").GetString());
        Assert.Equal(
            new[] { "certificateId", "xSignerId", "pin", "infocertSat", "packaging", "signatureLevel" },
            body.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.DoesNotContain(SignApiTestConfiguration.ClientSecret, body.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("test-access-token", body.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", body.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAutomaticCadesDevelopmentDefaults_FallsBackToBaselineBForUnsupportedConfiguredLevel()
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("Token endpoint must not be called."),
            (_, _) => throw new InvalidOperationException("SIGN endpoint must not be called."),
            defaultSignatureLevel: "BASELINE-T");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/signing/automatic/cades/defaults");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(
            "BASELINE-B",
            json.RootElement.GetProperty("signatureLevel").GetString());
    }

    [Fact]
    public async Task GetAutomaticCadesDevelopmentDefaults_IsNotMappedOutsideDevelopment()
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("Token endpoint must not be called."),
            (_, _) => throw new InvalidOperationException("SIGN endpoint must not be called."),
            environment: "Production");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/signing/automatic/cades/defaults");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static MultipartFormDataContent CreateForm(
        string? pin = "pin-sensitive-value",
        string? sat = "sat-sensitive-value",
        string packaging = "ENVELOPED",
        string signatureLevel = "BASELINE-B",
        byte[]? fileBytes = null,
        bool includeFile = true,
        string? certificateId = "INFOCERT+CL_EXT_ID_ZORRO_000000001",
        string? xSignerId = "signer-123")
    {
        var form = new MultipartFormDataContent();
        if (includeFile)
        {
            form.Add(new ByteArrayContent(fileBytes ?? PdfBytes), "file", "source.pdf");
        }

        if (certificateId is not null)
        {
            form.Add(new StringContent(certificateId), "certificateId");
        }

        if (xSignerId is not null)
        {
            form.Add(new StringContent(xSignerId), "xSignerId");
        }
        if (pin is not null)
        {
            form.Add(new StringContent(pin), "pin");
        }

        if (sat is not null)
        {
            form.Add(new StringContent(sat), "sat");
        }

        form.Add(new StringContent(packaging), "packaging");
        form.Add(new StringContent(signatureLevel), "signatureLevel");
        return form;
    }

    private static HttpResponseMessage SignedProviderResponse(string requestId, string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    applicationId = "desktop-signer",
                    signatureResult = new[]
                    {
                        new
                        {
                            requestId,
                            isOk = true,
                            signedDocument = new
                            {
                                content,
                                contentType = "application/pkcs7-mime",
                                attachName = "untrusted-provider-name.p7m",
                            },
                        },
                    },
                }),
                Encoding.UTF8,
                "application/json"),
        };

    private static AutomaticCadesApiFactory CreateFactory(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> tokenSendAsync,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> providerSendAsync,
        long maxFileSizeBytes = 1024,
        string environment = "Development",
        string defaultSignatureLevel = "BASELINE-B") =>
        new(tokenSendAsync, providerSendAsync, maxFileSizeBytes, environment, defaultSignatureLevel);

    private sealed class AutomaticCadesApiFactory : WebApplicationFactory<Program>
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _tokenSendAsync;
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _providerSendAsync;
        private readonly long _maxFileSizeBytes;
        private readonly string _environment;
        private readonly string _defaultSignatureLevel;

        public AutomaticCadesApiFactory(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> tokenSendAsync,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> providerSendAsync,
            long maxFileSizeBytes,
            string environment,
            string defaultSignatureLevel)
        {
            _tokenSendAsync = tokenSendAsync;
            _providerSendAsync = providerSendAsync;
            _maxFileSizeBytes = maxFileSizeBytes;
            _environment = environment;
            _defaultSignatureLevel = defaultSignatureLevel;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SignApi:IdentityProviderUrl"] = "https://identity.example/auth",
                    ["SignApi:BaseUrl"] = "https://sign.example/api",
                    ["SignApi:Authentication:ClientId"] = SignApiTestConfiguration.ClientId,
                    ["SignApi:Authentication:ClientSecret"] = SignApiTestConfiguration.ClientSecret,
                    ["SignApi:Authentication:GrantType"] = "client_credentials",
                    ["SignApi:Authentication:Scope"] = "signature",
                    ["SignApi:Defaults:ApplicationId"] = "desktop-signer",
                    ["SignApi:Defaults:AutomaticSigning:CertificateId"] = "certificate-default",
                    ["SignApi:Defaults:AutomaticSigning:XSignerId"] = "signer-default",
                    ["SignApi:Defaults:AutomaticSigning:Pin"] = "pin-default",
                    ["SignApi:Defaults:AutomaticSigning:InfocertSat"] = "sat-default",
                    ["SignApi:Defaults:AutomaticSigning:Packaging"] = "ENVELOPING",
                    ["SignApi:Defaults:AutomaticSigning:SignatureLevel"] = _defaultSignatureLevel,
                    ["SignApi:Upload:MaxFileSizeBytes"] = _maxFileSizeBytes.ToString(),
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient(SignApiTokenService.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() =>
                        new StubHttpMessageHandler(_tokenSendAsync));
                services.AddHttpClient<SignApiClient>()
                    .ConfigurePrimaryHttpMessageHandler(() =>
                        new StubHttpMessageHandler(_providerSendAsync));
            });
        }
    }
}
