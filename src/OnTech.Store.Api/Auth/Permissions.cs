using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Auth;

public static class StoreClaims
{
    public const string Permissions = "perms";
    public const string IsAdmin = "admin";
}

/// <summary>Requires the caller to hold a permission. Usage: [HasPermission(Permission.CreateOrders)].</summary>
public sealed class HasPermissionAttribute(Permission permission) : AuthorizeAttribute(PolicyName(permission))
{
    public const string Prefix = "perm:";
    public static string PolicyName(Permission p) => Prefix + p;
}

public sealed class PermissionRequirement(Permission permission) : IAuthorizationRequirement
{
    public Permission Permission { get; } = permission;
}

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.GetPermissions().HasFlag(requirement.Permission))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Builds "perm:X" policies on demand so each permission does not need registering by hand.</summary>
public sealed class PermissionPolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(HasPermissionAttribute.Prefix, StringComparison.Ordinal)
            && Enum.TryParse<Permission>(policyName[HasPermissionAttribute.Prefix.Length..], out var permission))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
        }
        return await base.GetPolicyAsync(policyName);
    }
}

public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("No user id claim."));

    public static Permission GetPermissions(this ClaimsPrincipal user) =>
        Enum.TryParse<Permission>(user.FindFirstValue(StoreClaims.Permissions), out var p) ? p : Permission.None;
}
