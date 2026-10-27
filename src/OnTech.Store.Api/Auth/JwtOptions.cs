namespace OnTech.Store.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "OnTech.Store";
    public string Audience { get; set; } = "OnTech.Store.Desktop";
    /// <summary>At least 32 characters. Change it on every installation.</summary>
    public string SigningKey { get; set; } = "";
    /// <summary>A working day by default, so users log in once per shift.</summary>
    public int TokenLifetimeHours { get; set; } = 12;
}
