using TrustPlatform.Api.SignApi;
using TrustPlatform.Api.Signing;
using TrustPlatform.Api.ValidationApi;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SignApiOptions>(
    builder.Configuration.GetSection(SignApiOptions.SectionName));
builder.Services.Configure<ValidationApiOptions>(
    builder.Configuration.GetSection(ValidationApiOptions.SectionName));
var configuredUploadLimit = Math.Max(
    builder.Configuration.GetValue<long?>("SignApi:Upload:MaxFileSizeBytes") ?? 10_485_760,
    builder.Configuration.GetValue<long?>("ValidationApi:Upload:MaxFileSizeBytes") ?? 10_485_760);
var multipartBodyLimit = configuredUploadLimit is > 0 and <= int.MaxValue - 65_536
    ? configuredUploadLimit + 65_536
    : 10_551_296;
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = multipartBodyLimit);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient(SignApiTokenService.HttpClientName)
    .RedactLoggedHeaders(["Authorization", "Infocert-SAT", "X-signer-id"]);
builder.Services.AddHttpClient<SignApiClient>()
    .RedactLoggedHeaders(["Authorization", "Infocert-SAT", "X-signer-id"]);
builder.Services.AddHttpClient(ValidationApiTokenService.HttpClientName)
    .RedactLoggedHeaders(["Authorization", "Infocert-SAT", "X-signer-id"]);
builder.Services.AddHttpClient<ValidationApiClient>()
    .RedactLoggedHeaders(["Authorization", "Infocert-SAT", "X-signer-id"]);
builder.Services.AddSingleton<SignApiTokenService>();
builder.Services.AddSingleton<ValidationApiTokenService>();
builder.Services.AddTransient<IAutomaticCadesSigningService, AutomaticCadesSigningService>();
builder.Services.AddTransient<IAutomaticPadesSigningService, AutomaticPadesSigningService>();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "An unexpected error occurred."
        });
    });
});

app.MapGet("/api/health", () => TypedResults.Ok(new HealthResponse("Healthy")))
    .WithName("GetHealth");

app.MapSigningEndpoints();
app.MapAutomaticPadesEndpoint();
app.MapCertificateEndpoints();
app.MapValidationApiEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/diagnostics/sign-auth", async (
        SignApiTokenService tokenService,
        CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await tokenService.TestAuthenticationAsync(cancellationToken));
        }
        catch (SignApiConfigurationException)
        {
            return Results.Json(
                new SignApiAuthenticationDiagnostic(
                    Authenticated: false,
                    TokenAcquired: false,
                    TokenType: null,
                    ExpiresIn: null,
                    Cached: false,
                    Error: "SIGN API authentication is not configured."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (SignApiAuthenticationException)
        {
            return Results.Json(
                new SignApiAuthenticationDiagnostic(
                    Authenticated: false,
                    TokenAcquired: false,
                    TokenType: null,
                    ExpiresIn: null,
                    Cached: false,
                    Error: "SIGN API authentication failed."),
                statusCode: StatusCodes.Status502BadGateway);
        }
    })
    .WithName("TestSignApiAuthentication");
}

app.Run();

public sealed record HealthResponse(string Status);

public partial class Program
{
}
