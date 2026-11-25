namespace TrustPlatform.Api.SignApi;

public sealed class SignApiOptions
{
    public const string SectionName = "SignApi";

    public string IdentityProviderUrl { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public SignApiAuthenticationOptions Authentication { get; set; } = new();

    public SignApiDefaultsOptions Defaults { get; set; } = new();

    public SignApiUploadOptions Upload { get; set; } = new();
}

public sealed class SignApiAuthenticationOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string GrantType { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;
}

public sealed class SignApiDefaultsOptions
{
    public string ApplicationId { get; set; } = string.Empty;

    public AutomaticSigningDefaultsOptions AutomaticSigning { get; set; } = new();
}

public sealed class AutomaticSigningDefaultsOptions
{
    public string CertificateId { get; set; } = string.Empty;

    public string XSignerId { get; set; } = string.Empty;

    public string Pin { get; set; } = string.Empty;

    public string InfocertSat { get; set; } = string.Empty;

    public string Packaging { get; set; } = string.Empty;

    public string SignatureLevel { get; set; } = string.Empty;

    public AutomaticPadesDefaultsOptions Pades { get; set; } = new();
}

public sealed class AutomaticPadesDefaultsOptions
{
    public bool IsVisible { get; set; } = true;

    public bool IsPdfa { get; set; } = true;

    public int Page { get; set; } = 1;

    public int Llx { get; set; } = 100;

    public int Lly { get; set; } = 100;

    public int Urx { get; set; } = 20;

    public int Ury { get; set; } = 20;
}

public sealed class SignApiUploadOptions
{
    public long MaxFileSizeBytes { get; set; } = 10_485_760;
}
