using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Auth;

/// <summary>
/// Lets the caller in when they hold ANY of the given permissions (admins hold them all).
/// Usage: [HasAnyPermission(ReadAccess.Catalog)]. The table of who may read what is <see cref="ReadAccess"/>.
/// </summary>
/// <remarks>
/// The attribute hands its requirement straight to ASP.NET Core (<see cref="IAuthorizationRequirementData"/>), and the requirement
/// checks itself, so it works without registering anything. <see cref="AnyPermissionPolicies.AddAnyPermissionPolicies"/> also makes
/// the named policy <c>any:&lt;int&gt;</c> available, for [Authorize(Policy = "any:7")].
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HasAnyPermissionAttribute(Permission anyOf) : AuthorizeAttribute, IAuthorizationRequirementData
{
    public const string Prefix = "any:";
    public static string PolicyName(Permission anyOf) => Prefix + ((int)anyOf).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public Permission AnyOf { get; } = anyOf;

    public IEnumerable<IAuthorizationRequirement> GetRequirements() => [new AnyPermissionRequirement(AnyOf)];
}

/// <summary>Met when the signed-in user holds at least one of <see cref="AnyOf"/>. It is its own handler.</summary>
public sealed class AnyPermissionRequirement(Permission anyOf) : AuthorizationHandler<AnyPermissionRequirement>, IAuthorizationRequirement
{
    public Permission AnyOf { get; } = anyOf;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AnyPermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && context.User.GetPermissions().HasAny(requirement.AnyOf))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }

    public override string ToString() => $"Any of: {AnyOf}";
}

/// <summary>Builds "any:&lt;int&gt;" policies on demand and hands every other name to the provider registered before it.</summary>
public sealed class AnyPermissionPolicyProvider(IAuthorizationPolicyProvider inner) : IAuthorizationPolicyProvider
{
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(HasAnyPermissionAttribute.Prefix, StringComparison.Ordinal)
            && int.TryParse(policyName.AsSpan(HasAnyPermissionAttribute.Prefix.Length), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var bits) && bits > 0)
        {
            return Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new AnyPermissionRequirement((Permission)bits))
                .Build());
        }
        return inner.GetPolicyAsync(policyName);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => inner.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => inner.GetFallbackPolicyAsync();
}

public static class AnyPermissionPolicies
{
    /// <summary>
    /// Optional: makes named "any:&lt;int&gt;" policies resolvable by wrapping the policy provider already registered
    /// (the PermissionPolicyProvider from AddStoreAuth). [HasAnyPermission] works without this. Call after AddStoreAuth.
    /// </summary>
    public static IServiceCollection AddAnyPermissionPolicies(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(Marker))) return services; // already wrapped
        services.AddSingleton<Marker>();
        var previous = services.LastOrDefault(d => d.ServiceType == typeof(IAuthorizationPolicyProvider));
        services.RemoveAll<IAuthorizationPolicyProvider>();
        services.AddSingleton<IAuthorizationPolicyProvider>(sp => new AnyPermissionPolicyProvider(previous switch
        {
            { ImplementationInstance: IAuthorizationPolicyProvider p } => p,
            { ImplementationFactory: { } f } => (IAuthorizationPolicyProvider)f(sp),
            { ImplementationType: { } t } => (IAuthorizationPolicyProvider)ActivatorUtilities.CreateInstance(sp, t),
            _ => new PermissionPolicyProvider(sp.GetRequiredService<IOptions<AuthorizationOptions>>()),
        }));
        return services;
    }

    private sealed class Marker;
}
