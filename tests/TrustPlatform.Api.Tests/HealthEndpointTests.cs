using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrustPlatform.Api.Tests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ReturnsHealthyStatus()
    {
        using var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var health = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
        Assert.Equal("Healthy", health.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetHealth_DoesNotExposeConfiguration()
    {
        using var response = await _client.GetAsync("/api/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("appsettings", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connectionString", body, StringComparison.OrdinalIgnoreCase);
    }
}
