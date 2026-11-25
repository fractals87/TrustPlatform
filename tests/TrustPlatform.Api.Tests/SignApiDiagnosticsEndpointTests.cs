using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TrustPlatform.Api.Tests;

public sealed class SignApiDiagnosticsEndpointTests
{
    [Fact]
    public async Task PostSignAuthentication_ReturnsSafeMetadataWithoutTokenOrSecret()
    {
        var factory = CreateFactory((_, _) =>
            Task.FromResult(SignApiTestConfiguration.TokenResponse()));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/diagnostics/sign-auth", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.True(json.RootElement.GetProperty("tokenAcquired").GetBoolean());
        Assert.Equal("Bearer", json.RootElement.GetProperty("tokenType").GetString());
        Assert.Equal(300, json.RootElement.GetProperty("expiresIn").GetInt32());
        Assert.True(json.RootElement.GetProperty("cached").GetBoolean());
        Assert.DoesNotContain("test-access-token", body, StringComparison.Ordinal);
        Assert.DoesNotContain(SignApiTestConfiguration.ClientId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(SignApiTestConfiguration.ClientSecret, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostSignAuthentication_SanitizesProviderFailure()
    {
        var factory = CreateFactory((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    $"provider response {SignApiTestConfiguration.ClientSecret} test-access-token"),
            }));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/diagnostics/sign-auth", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("SIGN API authentication failed.", body, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(body);
        Assert.False(json.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.False(json.RootElement.GetProperty("tokenAcquired").GetBoolean());
        Assert.False(json.RootElement.GetProperty("cached").GetBoolean());
        Assert.DoesNotContain("provider response", body, StringComparison.Ordinal);
        Assert.DoesNotContain("test-access-token", body, StringComparison.Ordinal);
        Assert.DoesNotContain(SignApiTestConfiguration.ClientId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(SignApiTestConfiguration.ClientSecret, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostSignAuthentication_ReturnsSafeFailureWhenConfigurationIsMissing()
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("Provider must not be called."),
            configured: false);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/diagnostics/sign-auth", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("SIGN API authentication is not configured.", body, StringComparison.Ordinal);
        Assert.DoesNotContain(SignApiTestConfiguration.ClientId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(SignApiTestConfiguration.ClientSecret, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostSignAuthentication_IsNotMappedOutsideDevelopment()
    {
        var factory = CreateFactory(
            (_, _) => throw new InvalidOperationException("Provider must not be called."),
            environment: "Production");
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/diagnostics/sign-auth", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static DiagnosticApiFactory CreateFactory(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
        bool configured = true,
        string environment = "Development") =>
        new(new StubHttpMessageHandler(sendAsync), configured, environment);

    private sealed class DiagnosticApiFactory : WebApplicationFactory<Program>
    {
        private readonly StubHttpClientFactory _httpClientFactory;
        private readonly bool _configured;
        private readonly string _environment;

        public DiagnosticApiFactory(
            HttpMessageHandler handler,
            bool configured,
            string environment)
        {
            _httpClientFactory = new StubHttpClientFactory(handler);
            _configured = configured;
            _environment = environment;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["SignApi:IdentityProviderUrl"] = _configured
                        ? "https://identity.example/auth"
                        : string.Empty,
                    ["SignApi:BaseUrl"] = "https://sign.example/api",
                    ["SignApi:Authentication:ClientId"] = SignApiTestConfiguration.ClientId,
                    ["SignApi:Authentication:ClientSecret"] = _configured
                        ? SignApiTestConfiguration.ClientSecret
                        : string.Empty,
                    ["SignApi:Authentication:GrantType"] = "client_credentials",
                    ["SignApi:Authentication:Scope"] = "signature",
                };
                configuration.AddInMemoryCollection(values);
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(_httpClientFactory);
            });
        }
    }
}
