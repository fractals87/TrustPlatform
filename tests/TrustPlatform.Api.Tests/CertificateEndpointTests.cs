using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TrustPlatform.Api.SignApi;

namespace TrustPlatform.Api.Tests;

public sealed class CertificateEndpointTests
{
    [Fact]
    public async Task PostCertificates_UsesRequestXSignerIdAndMapsProviderResponse()
    {
        HttpRequestMessage? providerRequest = null;
        var factory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            (request, _) =>
            {
                providerRequest = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        [
                          {
                            "certificate": "base64-certificate-data",
                            "ids": ["urn:alias:alias-1", "urn:key:key-1"],
                            "subject": "CN=Example Subject",
                            "issuer": "CN=Example Issuer",
                            "status": "ACTIVE",
                            "expirationDate": "2035-01-01T00:00:00Z"
                          }
                        ]
                        """),
                });
            });
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/certificates",
            new CertificateListRequest("signer-request"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(providerRequest);
        Assert.Equal("Bearer", providerRequest.Headers.Authorization!.Scheme);
        Assert.Equal("test-access-token", providerRequest.Headers.Authorization.Parameter);
        Assert.Equal("signer-request", providerRequest.Headers.GetValues("X-signer-id").Single());
        Assert.Equal("application/json", providerRequest.Headers.Accept.Single().MediaType);
        Assert.Equal("https://sign.example/api/certificates", providerRequest.RequestUri!.ToString());

        using var json = JsonDocument.Parse(body);
        var certificate = json.RootElement.GetProperty("certificates")[0];
        Assert.Equal("CN=Example Subject", certificate.GetProperty("subject").GetString());
        Assert.Equal("CN=Example Issuer", certificate.GetProperty("issuer").GetString());
        Assert.Equal("ACTIVE", certificate.GetProperty("status").GetString());
        Assert.Equal("alias-1", certificate.GetProperty("aliasId").GetString());
        Assert.Equal("key-1", certificate.GetProperty("keyId").GetString());
        Assert.Equal("2035-01-01T00:00:00Z", certificate.GetProperty("expirationDate").GetString());
        Assert.False(certificate.TryGetProperty("certificate", out _));
        Assert.False(body.Contains("base64-certificate-data", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PostCertificates_HandlesEmptyListAndRejectsMissingXSignerId()
    {
        var factory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]"),
            }));
        using var client = factory.CreateClient();

        using var emptyResponse = await client.PostAsJsonAsync(
            "/api/certificates",
            new CertificateListRequest("signer-default"));
        var emptyBody = await emptyResponse.Content.ReadAsStringAsync();
        using var emptyJson = JsonDocument.Parse(emptyBody);
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        Assert.Equal(0, emptyJson.RootElement.GetProperty("certificates").GetArrayLength());

        var missingXSignerFactory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            (_, _) => throw new InvalidOperationException("Provider must not be called."),
            xSignerId: "");
        using var missingSignerClient = missingXSignerFactory.CreateClient();

        using var missingSignerResponse = await missingSignerClient.PostAsJsonAsync(
            "/api/certificates",
            new CertificateListRequest(""));
        using var missingSignerJson = JsonDocument.Parse(await missingSignerResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, missingSignerResponse.StatusCode);
        Assert.Equal("Certificate query failed", missingSignerJson.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetCertificateQueryDefaults_ReturnsConfiguredIdentifiersWithoutCallingSignApi()
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("The token endpoint must not be called."),
            (_, _) => throw new InvalidOperationException("The provider must not be called."),
            certificateId: "certificate-default",
            xSignerId: "signer-default");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/certificates/defaults");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(
            "certificate-default",
            json.RootElement.GetProperty("certificateId").GetString());
        Assert.Equal("signer-default", json.RootElement.GetProperty("xSignerId").GetString());
        Assert.False(json.RootElement.TryGetProperty("pin", out _));
        Assert.False(json.RootElement.TryGetProperty("infocertSat", out _));
    }

    [Fact]
    public async Task PostConfiguredCertificate_UsesRequestCertificateIdAndSignerAndMapsProviderResponse()
    {
        HttpRequestMessage? providerRequest = null;
        var factory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            async (request, cancellationToken) =>
            {
                providerRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "certificate": "base64-detail-certificate",
                          "ids": ["urn:alias:detail-alias", "urn:key:detail-key", "urn:dnqqualifier:detail-dnq"],
                          "subject": "CN=Configured Subject",
                          "issuer": "CN=Configured Issuer",
                          "status": "VALID",
                          "expirationDate": "2036-01-01T00:00:00Z"
                        }
                        """),
                };
            },
            certificateId: "CERT-123",
            xSignerId: "signer-default");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/certificates/configured",
            new CertificateDetailsRequest("CERT-456", "signer-request"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(providerRequest);
        Assert.Equal("https://sign.example/api/certificates/CERT-456", providerRequest.RequestUri!.ToString());
        Assert.Equal("signer-request", providerRequest.Headers.GetValues("X-signer-id").Single());

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("CERT-456", root.GetProperty("certificateId").GetString());
        Assert.Equal("CN=Configured Subject", root.GetProperty("subject").GetString());
        Assert.Equal("VALID", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("certificateAvailable").GetBoolean());
        Assert.Equal("detail-alias", root.GetProperty("aliasId").GetString());
        Assert.Equal("detail-key", root.GetProperty("keyId").GetString());
        Assert.Equal("detail-dnq", root.GetProperty("dnQualifierId").GetString());
        Assert.False(body.Contains("base64-detail-certificate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PostConfiguredCertificate_ReportsUnsafeCertificateIdAndNotFound()
    {
        var invalidFactory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            (_, _) => throw new InvalidOperationException("Provider must not be called."),
            certificateId: "bad/id");
        using var invalidClient = invalidFactory.CreateClient();

        using var invalidResponse = await invalidClient.PostAsJsonAsync(
            "/api/certificates/configured",
            new CertificateDetailsRequest("bad/id", "signer-default"));
        var invalidBody = await invalidResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Contains("valid certificate ID", invalidBody, StringComparison.OrdinalIgnoreCase);

        var notFoundFactory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"message\":\"not found\"}")
            }),
            certificateId: "CERT-404");
        using var notFoundClient = notFoundFactory.CreateClient();

        using var notFoundResponse = await notFoundClient.PostAsJsonAsync(
            "/api/certificates/configured",
            new CertificateDetailsRequest("CERT-404", "signer-default"));
        using var notFoundJson = JsonDocument.Parse(await notFoundResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
        Assert.Equal("The requested certificate was not found.", notFoundJson.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetCertificateChain_ReturnsArrayUsesConfiguredSignerAndMapsFields()
    {
        HttpRequestMessage? providerRequest = null;
        var factory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            (request, _) =>
            {
                providerRequest = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        [
                          {
                            "certificate": "leaf-certificate",
                            "ids": ["urn:alias:leaf"],
                            "subject": "CN=Leaf",
                            "issuer": "CN=Issuer",
                            "status": "active",
                            "expirationDate": "2035-01-01T00:00:00Z"
                          },
                          {
                            "certificate": "root-certificate",
                            "ids": ["urn:key:root"],
                            "subject": "CN=Root",
                            "issuer": "CN=Root",
                            "status": "future-status",
                            "expirationDate": "2036-01-01T00:00:00Z"
                          }
                        ]
                        """),
                });
            },
            certificateId: "CERT-CHAIN",
            xSignerId: "configured-signer");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/certificates/CERT-CHAIN/chain");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(providerRequest);
        Assert.Equal("https://sign.example/api/certificates/CERT-CHAIN/chain", providerRequest.RequestUri!.ToString());
        Assert.Equal("configured-signer", providerRequest.Headers.GetValues("X-signer-id").Single());
        Assert.Equal("Bearer", providerRequest.Headers.Authorization!.Scheme);
        Assert.Equal("test-access-token", providerRequest.Headers.Authorization.Parameter);

        using var json = JsonDocument.Parse(body);
        Assert.Equal(2, json.RootElement.GetArrayLength());
        var first = json.RootElement[0];
        Assert.Equal("leaf-certificate", first.GetProperty("certificate").GetString());
        Assert.Equal("CN=Leaf", first.GetProperty("subject").GetString());
        Assert.Equal("CN=Issuer", first.GetProperty("issuer").GetString());
        Assert.Equal("active", first.GetProperty("status").GetString());
        Assert.Equal("2035-01-01T00:00:00Z", first.GetProperty("expirationDate").GetString());
        Assert.Equal("unknown", json.RootElement[1].GetProperty("status").GetString());

        using var postResponse = await client.PostAsJsonAsync(
            "/api/certificates/CERT-CHAIN/chain",
            new CertificateChainRequest("request-signer"));
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        Assert.Equal("request-signer", providerRequest!.Headers.GetValues("X-signer-id").Single());
    }

    [Fact]
    public async Task GetCertificateChain_RejectsInvalidIdsAndReturnsSafeProviderErrors()
    {
        var missingSignerFactory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            (_, _) => throw new InvalidOperationException("Provider must not be called."),
            xSignerId: "");
        using var missingSignerClient = missingSignerFactory.CreateClient();
        using var missingSignerResponse = await missingSignerClient.GetAsync("/api/certificates/CERT-1/chain");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, missingSignerResponse.StatusCode);

        using var invalidIdClient = missingSignerFactory.CreateClient();
        using var invalidIdResponse = await invalidIdClient.GetAsync("/api/certificates/bad%2Fid/chain");
        Assert.Equal(HttpStatusCode.BadRequest, invalidIdResponse.StatusCode);

        var notFoundFactory = CreateFactory(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse()),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"access_token\":\"do-not-return\",\"message\":\"not found\"}")
            }),
            xSignerId: "configured-signer");
        using var notFoundClient = notFoundFactory.CreateClient();
        using var notFoundResponse = await notFoundClient.GetAsync("/api/certificates/CERT-404/chain");
        var notFoundBody = await notFoundResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
        Assert.DoesNotContain("do-not-return", notFoundBody, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> tokenSendAsync,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> providerSendAsync,
        string? certificateId = null,
        string? xSignerId = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
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
                        ["SignApi:Defaults:AutomaticSigning:CertificateId"] = certificateId ?? "certificate-default",
                        ["SignApi:Defaults:AutomaticSigning:XSignerId"] = xSignerId ?? "signer-default",
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    services.AddHttpClient(SignApiTokenService.HttpClientName)
                        .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(tokenSendAsync));
                    services.AddHttpClient<SignApiClient>()
                        .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(providerSendAsync));
                });
            });
    }
}
