using Microsoft.AspNetCore.Authorization;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Auth;

/// <summary>Marks an endpoint a signed-in user may call while they still have to change their password.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowWhileMustChangePasswordAttribute : Attribute;

/// <summary>
/// M-2: while the user's MustChangePassword flag is set (read from the database on every request in <see cref="AuthSetup"/>),
/// every authenticated endpoint answers 403 with code <see cref="ErrorCodes.MustChangePassword"/>,
/// except the ones marked <see cref="AllowWhileMustChangePasswordAttribute"/> (GET api/auth/me, POST api/auth/change-password).
/// </summary>
public sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    public const string Title = "You must change your password before you continue.";

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is not null
            && context.User.Identity?.IsAuthenticated == true
            && context.User.HasClaim(StoreClaims.MustChangePassword, "true")
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
            && endpoint.Metadata.GetMetadata<AllowWhileMustChangePasswordAttribute>() is null)
        {
            await Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: Title,
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.MustChangePassword })
                .ExecuteAsync(context);
            return;
        }
        await next(context);
    }
}
