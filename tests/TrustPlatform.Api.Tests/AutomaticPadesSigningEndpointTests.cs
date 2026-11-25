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

public sealed class AutomaticPadesSigningEndpointTests
{
    private static readonly byte[] PdfBytes = "%PDF-1.7\nmock document"u8.ToArray();

    [Fact]
    public async Task PostAutomaticPades_MapsVisibleProviderRequestAndReturnsPdfDownload()
    {
        HttpRequestMessage? providerRequest = null;
        string? providerBody = null;
        var factory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            async (request, cancellationToken) =>
            {
                providerRequest = request;
                providerBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var body = JsonDocument.Parse(providerBody);
                var requestId = body.RootElement
                    .GetProperty("padesSignatures")[0]
                    .GetProperty("requestId")
                    .GetString();
                return PadesProviderResponse(requestId!, Convert.ToBase64String("signed pades pdf"u8.ToArray()));
            });
        using var client = factory.CreateClient();

        using var form = CreateForm(isVisible: "true");
        using var response = await client.PostAsync("/api/signing/automatic/pades", form);
        var signedContent = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("signed pades pdf", Encoding.UTF8.GetString(signedContent));
        Assert.NotNull(providerRequest);
        Assert.Equal("https://sign.example/api/certificates/INFOCERT%2BCL_EXT_ID_ZORRO_000000001/sign", providerRequest.RequestUri!.ToString());
        Assert.Equal("signer-123", providerRequest.Headers.GetValues("X-signer-id").Single());
        Assert.Equal("sat-sensitive-value", providerRequest.Headers.GetValues("Infocert-SAT").Single());

        using var providerJson = JsonDocument.Parse(providerBody!);
        var root = providerJson.RootElement;
        Assert.Equal("desktop-signer", root.GetProperty("applicationId").GetString());
        Assert.Equal("pin-sensitive-value", root.GetProperty("pin").GetString());
        Assert.False(root.TryGetProperty("cadesSignatures", out _));

        var signature = root.GetProperty("padesSignatures")[0];
        Assert.Equal("true", signature.GetProperty("isVisible").GetString());
        Assert.Equal("ENVELOPED", signature.GetProperty("packaging").GetString());
        Assert.Equal("BASELINE-B", signature.GetProperty("signatureLevel").GetString());
        Assert.Equal(
            Convert.ToBase64String(PdfBytes),
            signature.GetProperty("document").GetProperty("content").GetString());
        Assert.Equal("application/pdf", signature.GetProperty("document").GetProperty("contentType").GetString());

        var field = signature.GetProperty("signatureFields")[0];
        Assert.Equal("true", field.GetProperty("isPdfa").GetString());
        var position = field.GetProperty("position");
        Assert.Equal(JsonValueKind.Number, position.GetProperty("page").ValueKind);
        Assert.Equal(1, position.GetProperty("page").GetInt32());
        Assert.Equal(100, position.GetProperty("llx").GetInt32());
        Assert.Equal(100, position.GetProperty("lly").GetInt32());
        Assert.Equal(20, position.GetProperty("urx").GetInt32());
        Assert.Equal(20, position.GetProperty("ury").GetInt32());
    }

    [Fact]
    public async Task PostAutomaticPades_InvisibleSignatureOmitsSignatureFields()
    {
        string? providerBody = null;
        var factory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            async (request, cancellationToken) =>
            {
                providerBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var body = JsonDocument.Parse(providerBody);
                var requestId = body.RootElement
                    .GetProperty("padesSignatures")[0]
                    .GetProperty("requestId")
                    .GetString();
                return PadesProviderResponse(requestId!, Convert.ToBase64String("signed pades pdf"u8.ToArray()));
            });
        using var client = factory.CreateClient();

        using var form = CreateForm(isVisible: "false");
        using var response = await client.PostAsync("/api/signing/automatic/pades", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var providerJson = JsonDocument.Parse(providerBody!);
        var signature = providerJson.RootElement
            .GetProperty("padesSignatures")[0];
        Assert.Equal("false", signature.GetProperty("isVisible").GetString());
        Assert.False(signature.TryGetProperty("signatureFields", out _));
    }

    [Fact]
    public async Task PostAutomaticPades_RejectsUnsupportedSignatureLevel()
    {
        var factory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            (_, _) => throw new InvalidOperationException("Provider must not be called."));
        using var client = factory.CreateClient();
        using var form = CreateForm(signatureLevel: "BASELINE-T");

        using var response = await client.PostAsync("/api/signing/automatic/pades", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static MultipartFormDataContent CreateForm(
        string isVisible = "true",
        string signatureLevel = "BASELINE-B")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(PdfBytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "document.pdf");
        form.Add(new StringContent("INFOCERT+CL_EXT_ID_ZORRO_000000001"), "certificateId");
        form.Add(new StringContent("signer-123"), "xSignerId");
        form.Add(new StringContent("pin-sensitive-value"), "pin");
        form.Add(new StringContent("sat-sensitive-value"), "sat");
        form.Add(new StringContent("ENVELOPED"), "packaging");
        form.Add(new StringContent(signatureLevel), "signatureLevel");
        form.Add(new StringContent(isVisible), "isVisible");
        if (isVisible == "true")
        {
            form.Add(new StringContent("true"), "isPdfa");
            form.Add(new StringContent("1"), "page");
            form.Add(new StringContent("100"), "llx");
            form.Add(new StringContent("100"), "lly");
            form.Add(new StringContent("20"), "urx");
            form.Add(new StringContent("20"), "ury");
        }
        return form;
    }

    private static HttpResponseMessage PadesProviderResponse(string requestId, string content) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent(requestId, content),
        };

    private static StringContent JsonContent(string requestId, string content) =>
        new(
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
                            contentType = "application/pdf",
                            attachName = "signed.pdf",
                        },
                    },
                },
            }),
            Encoding.UTF8,
            "application/json");

    private static PadesApiFactory CreateFactory(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> tokenSendAsync,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> providerSendAsync) =>
        new(tokenSendAsync, providerSendAsync);

    private sealed class PadesApiFactory : WebApplicationFactory<Program>
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _tokenSendAsync;
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _providerSendAsync;

        public PadesApiFactory(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> tokenSendAsync,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> providerSendAsync)
        {
            _tokenSendAsync = tokenSendAsync;
            _providerSendAsync = providerSendAsync;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
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
                    ["SignApi:Defaults:AutomaticSigning:SignatureLevel"] = "BASELINE-B",
                    ["SignApi:Defaults:AutomaticSigning:Pades:IsVisible"] = "true",
                    ["SignApi:Defaults:AutomaticSigning:Pades:IsPdfa"] = "true",
                    ["SignApi:Defaults:AutomaticSigning:Pades:Page"] = "1",
                    ["SignApi:Defaults:AutomaticSigning:Pades:Llx"] = "100",
                    ["SignApi:Defaults:AutomaticSigning:Pades:Lly"] = "100",
                    ["SignApi:Defaults:AutomaticSigning:Pades:Urx"] = "20",
                    ["SignApi:Defaults:AutomaticSigning:Pades:Ury"] = "20",
                    ["SignApi:Upload:MaxFileSizeBytes"] = "10485760",
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
