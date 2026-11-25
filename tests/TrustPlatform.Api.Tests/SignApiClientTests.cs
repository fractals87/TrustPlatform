using System.Net;
using Microsoft.Extensions.Options;
using TrustPlatform.Api.SignApi;

namespace TrustPlatform.Api.Tests;

public sealed class SignApiClientTests
{
    [Fact]
    public async Task SendAsync_UsesConfiguredBaseUrlAndBearerToken()
    {
        HttpRequestMessage? capturedRequest = null;
        var tokenService = new SignApiTokenService(
            new StubHttpClientFactory(new StubHttpMessageHandler((_, _) =>
                Task.FromResult(SignApiTestConfiguration.TokenResponse()))),
            Options.Create(SignApiTestConfiguration.CreateOptions()),
            new TestTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        var providerHandler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var client = new SignApiClient(
            new HttpClient(providerHandler),
            tokenService,
            Options.Create(SignApiTestConfiguration.CreateOptions()));

        using var response = await client.SendAsync(HttpMethod.Get, "certificates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(capturedRequest);
        Assert.Equal("https://sign.example/api/certificates", capturedRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal("test-access-token", capturedRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task GetCertificateAsync_UsesConfiguredBaseUrlAndSeparatesCertificateIdFromXSignerId()
    {
        HttpRequestMessage? capturedRequest = null;
        var options = SignApiTestConfiguration.CreateOptions();
        options.BaseUrl = "https://apistage.infocert.digital/signature/v1";
        options.Defaults.AutomaticSigning.CertificateId = "CD6972A8922C0230B45C3F97B26891E3";
        options.Defaults.AutomaticSigning.XSignerId = "MA471086";

        var tokenService = new SignApiTokenService(
            new StubHttpClientFactory(new StubHttpMessageHandler((_, _) =>
                Task.FromResult(SignApiTestConfiguration.TokenResponse()))),
            Options.Create(options),
            new TestTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")));

        var providerHandler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"certificate\":\"cert-data\",\"ids\":[\"id-1\"]}")
            });
        });

        var client = new SignApiClient(
            new HttpClient(providerHandler),
            tokenService,
            Options.Create(options));

        var certificate = await client.GetCertificateAsync(options.Defaults.AutomaticSigning.CertificateId, options.Defaults.AutomaticSigning.XSignerId);

        Assert.Equal("cert-data", certificate.Certificate);
        Assert.NotNull(capturedRequest);
        Assert.Equal("https://apistage.infocert.digital/signature/v1/certificates/CD6972A8922C0230B45C3F97B26891E3", capturedRequest.RequestUri!.ToString());
        Assert.Equal("MA471086", capturedRequest.Headers.GetValues("X-signer-id").Single());
        Assert.Equal("application/json", capturedRequest.Headers.Accept.Single().MediaType);
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("/absolute")]
    [InlineData("https://other.example/path")]
    public async Task SendAsync_RejectsPathsOutsideConfiguredBase(string path)
    {
        var client = new SignApiClient(
            new HttpClient(new StubHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("Provider must not be called."))),
            new SignApiTokenService(
                new StubHttpClientFactory(new StubHttpMessageHandler((_, _) =>
                    Task.FromResult(SignApiTestConfiguration.TokenResponse()))),
                Options.Create(SignApiTestConfiguration.CreateOptions()),
                new TestTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"))),
            Options.Create(SignApiTestConfiguration.CreateOptions()));

        await Assert.ThrowsAsync<ArgumentException>(() => client.SendAsync(HttpMethod.Get, path));
    }
}
