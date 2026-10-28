using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OnTech.Store.Api.Data;

namespace OnTech.Store.Api.Auth;

public static class AuthSetup
{
    public static IServiceCollection AddStoreAuth(this IServiceCollection services, IConfiguration config)
    {
        var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be set to at least 32 characters in appsettings.json.");

        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.AddSingleton<TokenService>();
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = TokenService.CreateKey(jwt.SigningKey),
                    ClockSkew = TimeSpan.FromMinutes(2),
                    NameClaimType = "name",
                };
                o.Events = new JwtBearerEvents
                {
                    // Permissions are read from the database on every request (cheap for a handful of users),
                    // so disabling a user or changing their permissions takes effect immediately.
                    OnTokenValidated = async ctx =>
                    {
                        var sub = ctx.Principal?.FindFirst("sub")?.Value;
                        if (!int.TryParse(sub, out var userId)) { ctx.Fail("Invalid token."); return; }

                        var db = ctx.HttpContext.RequestServices.GetRequiredService<StoreDbContext>();
                        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                        if (user is null || !user.IsActive) { ctx.Fail("User is disabled."); return; }

                        var identity = new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                            new Claim("name", user.FullName),
                            new Claim(StoreClaims.IsAdmin, user.IsAdmin ? "true" : "false"),
                            new Claim(StoreClaims.Permissions, ((int)user.EffectivePermissions).ToString()),
                        }, JwtBearerDefaults.AuthenticationScheme, "name", ClaimTypes.Role);
                        ctx.Principal = new ClaimsPrincipal(identity);
                    }
                };
            });

        services.AddAuthorization(o =>
        {
            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });
        return services;
    }
}
