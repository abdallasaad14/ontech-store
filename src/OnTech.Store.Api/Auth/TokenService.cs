using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OnTech.Store.Api.Data;

namespace OnTech.Store.Api.Auth;

public sealed class TokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _opt = options.Value;

    public (string Token, DateTimeOffset ExpiresAt) CreateToken(User user)
    {
        var expires = DateTimeOffset.UtcNow.AddHours(_opt.TokenLifetimeHours);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.FullName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        var creds = new SigningCredentials(CreateKey(_opt.SigningKey), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_opt.Issuer, _opt.Audience, claims,
            notBefore: DateTime.UtcNow, expires: expires.UtcDateTime, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public static SymmetricSecurityKey CreateKey(string signingKey) => new(Encoding.UTF8.GetBytes(signingKey));
}
