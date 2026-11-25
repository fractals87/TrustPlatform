using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using TrustPlatform.Api.SignApi;

namespace TrustPlatform.Api.Tests;

public sealed class SignApiTokenServiceTests
{
    [Fact]
    public async Task GetAccessTokenAsync_SendsConfiguredClientCredentialsAndParsesResponse()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedForm = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return CaptureRequestAndRespondAsync(request);
        });
        var service = CreateService(handler);

        var accessToken = await service.GetAccessTokenAsync();

        Assert.Equal("test-access-token", accessToken);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://identity.example/auth/token", capturedRequest.RequestUri!.ToString());
        Assert.Equal("Basic", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal(
            "application/x-www-form-urlencoded",
            capturedRequest.Content!.Headers.ContentType!.MediaType);
        var credentials = Encoding.UTF8.GetString(
            Convert.FromBase64String(capturedRequest.Headers.Authorization.Parameter!));
        Assert.Equal(
            $"{SignApiTestConfiguration.ClientId}:{SignApiTestConfiguration.ClientSecret}",
            credentials);

        Assert.Contains("grant_type=client_credentials", capturedForm);
        Assert.Contains("scope=signature", capturedForm);

        async Task<HttpResponseMessage> CaptureRequestAndRespondAsync(HttpRequestMessage request)
        {
            capturedForm = await request.Content!.ReadAsStringAsync();
            return SignApiTestConfiguration.TokenResponse();
        }
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReusesTokenUntilRefreshWindow()
    {
        var requestCount = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(SignApiTestConfiguration.TokenResponse(expiresIn: 100));
        });
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var service = CreateService(handler, timeProvider);

        var firstToken = await service.GetAccessTokenAsync();
        var cachedToken = await service.GetAccessTokenAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(91));
        var renewedToken = await service.GetAccessTokenAsync();

        Assert.Equal(firstToken, cachedToken);
        Assert.Equal(firstToken, renewedToken);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task GetAccessTokenAsync_RenewsExpiredToken()
    {
        var requestCount = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(SignApiTestConfiguration.TokenResponse(
                token: $"test-access-token-{requestCount}",
                expiresIn: 2));
        });
        var timeProvider = new TestTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var service = CreateService(handler, timeProvider);

        var firstToken = await service.GetAccessTokenAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        var renewedToken = await service.GetAccessTokenAsync();

        Assert.Equal("test-access-token-1", firstToken);
        Assert.Equal("test-access-token-2", renewedToken);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task TestAuthenticationAsync_ReturnsOnlySafeTokenMetadata()
    {
        var service = CreateService(new StubHttpMessageHandler(
            (_, _) => Task.FromResult(SignApiTestConfiguration.TokenResponse(expiresIn: 300))));

        var result = await service.TestAuthenticationAsync();

        Assert.True(result.Authenticated);
        Assert.True(result.TokenAcquired);
        Assert.Equal("Bearer", result.TokenType);
        Assert.Equal(300, result.ExpiresIn);
        Assert.True(result.Cached);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task GetAccessTokenAsync_MapsProviderFailureWithoutResponseBody()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("provider error includes sensitive details"),
            }));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<SignApiAuthenticationException>(
            () => service.GetAccessTokenAsync());

        Assert.Equal("The SIGN API identity provider rejected authentication.", exception.Message);
        Assert.DoesNotContain("sensitive details", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAccessTokenAsync_RejectsMalformedTokenResponse()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"test-access-token","token_type":"Bearer"}"""),
            }));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsAsync<SignApiAuthenticationException>(
            () => service.GetAccessTokenAsync());

        Assert.Equal(
            "The SIGN API identity provider returned an invalid token response.",
            exception.Message);
    }

    [Fact]
    public async Task GetAccessTokenAsync_RejectsMissingConfigurationBeforeCallingProvider()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("Provider must not be called."));
        var options = SignApiTestConfiguration.CreateOptions();
        options.Authentication.ClientSecret = string.Empty;
        var service = CreateService(handler, options: options);

        var exception = await Assert.ThrowsAsync<SignApiConfigurationException>(
            () => service.GetAccessTokenAsync());

        Assert.Equal("SIGN API client credentials are not configured.", exception.Message);
    }

    [Fact]
    public async Task GetAccessTokenAsync_RejectsUnsupportedTokenType()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(SignApiTestConfiguration.TokenResponse(tokenType: "Other")));
        var service = CreateService(handler);

        await Assert.ThrowsAsync<SignApiAuthenticationException>(
            () => service.GetAccessTokenAsync());
    }

    private static SignApiTokenService CreateService(
        HttpMessageHandler handler,
        TestTimeProvider? timeProvider = null,
        SignApiOptions? options = null)
    {
        var clientFactory = new StubHttpClientFactory(handler);
        return new SignApiTokenService(
            clientFactory,
            Options.Create(options ?? SignApiTestConfiguration.CreateOptions()),
            timeProvider ?? new TestTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
    }
}
