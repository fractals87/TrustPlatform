using System.Net;

namespace TrustPlatform.Api.Tests;

internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        sendAsync(request, cancellationToken);
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public List<string> RequestedClientNames { get; } = [];

    public HttpClient CreateClient(string name)
    {
        RequestedClientNames.Add(name);
        return new HttpClient(handler, disposeHandler: false);
    }
}

internal sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan amount) => _now += amount;
}

internal static class SignApiTestConfiguration
{
    public const string ClientId = "test-client-id";
    public const string ClientSecret = "test-client-secret";

    public static TrustPlatform.Api.SignApi.SignApiOptions CreateOptions() =>
        new()
        {
            IdentityProviderUrl = "https://identity.example/auth",
            BaseUrl = "https://sign.example/api",
            Authentication = new TrustPlatform.Api.SignApi.SignApiAuthenticationOptions
            {
                ClientId = ClientId,
                ClientSecret = ClientSecret,
                GrantType = "client_credentials",
                Scope = "signature",
            },
        };

    public static HttpResponseMessage TokenResponse(
        string token = "test-access-token",
        int expiresIn = 300,
        string tokenType = "Bearer") =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"access_token":"{{token}}","token_type":"{{tokenType}}","expires_in":{{expiresIn}}}"""),
        };
}
