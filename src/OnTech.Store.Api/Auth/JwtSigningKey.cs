using System.Security.Cryptography;
using System.Text;

namespace OnTech.Store.Api.Auth;

/// <summary>
/// The key that signs and checks the bearer tokens. It comes only from configuration outside git:
/// the environment variable <c>Jwt__SigningKey</c> or user-secrets (<c>Jwt:SigningKey</c>).
/// </summary>
public sealed class JwtSigningKey
{
    public const int MinimumBytes = 32;

    /// <summary>Sample keys that were once committed to git. Anyone with the repository could sign tokens with them.</summary>
    public static readonly IReadOnlyList<string> LeakedKeys = new[]
    {
        "development-only-signing-key-0123456789abcdef",
        "REPLACE-THIS-with-a-long-random-secret-of-at-least-32-chars",
    };

    public string Value { get; }

    /// <summary>True when no key was configured and a random one was made for this process (Development only).</summary>
    public bool Generated { get; }

    private JwtSigningKey(string value, bool generated) { Value = value; Generated = generated; }

    public static JwtSigningKey Resolve(string? configured, IHostEnvironment env)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (env.IsDevelopment())
                return new JwtSigningKey(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)), generated: true);
            throw new InvalidOperationException(
                $"Jwt:SigningKey is not set ({env.EnvironmentName} mode). Set the environment variable Jwt__SigningKey to a random secret of at least {MinimumBytes} bytes, " +
                "for example: [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48)). See audit/fix/DEPLOY_NOTES.md.");
        }

        if (LeakedKeys.Contains(configured.Trim(), StringComparer.Ordinal))
            throw new InvalidOperationException(
                "Jwt:SigningKey is a sample key that is published in git, so anyone could sign tokens with it. Set Jwt__SigningKey to a new random secret.");

        if (Encoding.UTF8.GetByteCount(configured) < MinimumBytes)
            throw new InvalidOperationException(
                $"Jwt:SigningKey is too short ({Encoding.UTF8.GetByteCount(configured)} bytes). Set Jwt__SigningKey to a random secret of at least {MinimumBytes} bytes.");

        return new JwtSigningKey(configured, generated: false);
    }
}
