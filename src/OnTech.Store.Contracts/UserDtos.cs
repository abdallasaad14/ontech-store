using System.ComponentModel.DataAnnotations;

namespace OnTech.Store.Contracts;

public sealed class UserDto
{
    public int Id { get; init; }
    public string FullName { get; init; } = "";
    public string Username { get; init; } = "";
    public bool IsAdmin { get; init; }
    public bool IsActive { get; init; }
    public bool MustChangePassword { get; init; }
    /// <summary>Effective permissions (all of them for admins).</summary>
    public Permission Permissions { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
}

public sealed class CreateUserRequest
{
    [Required, MaxLength(150)] public string FullName { get; set; } = "";
    /// <summary>What the user types to sign in: letters, digits, dot, dash or underscore. Not case-sensitive.</summary>
    [Required, MinLength(3), MaxLength(64), RegularExpression(UserNames.Pattern, ErrorMessage = UserNames.Rule)]
    public string Username { get; set; } = "";
    [Required, MinLength(8), MaxLength(128)] public string Password { get; set; } = "";
    public bool IsAdmin { get; set; }
    public Permission Permissions { get; set; } = Permission.StandardUser;
    /// <summary>Ask the user to pick their own password on first login.</summary>
    public bool MustChangePassword { get; set; } = true;
}

public sealed class UpdateUserRequest
{
    [Required, MaxLength(150)] public string FullName { get; set; } = "";
    /// <summary>What the user types to sign in: letters, digits, dot, dash or underscore. Not case-sensitive.</summary>
    [Required, MinLength(3), MaxLength(64), RegularExpression(UserNames.Pattern, ErrorMessage = UserNames.Rule)]
    public string Username { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool IsActive { get; set; } = true;
    public Permission Permissions { get; set; }
}

public sealed class ResetPasswordRequest
{
    [Required, MinLength(8), MaxLength(128)] public string NewPassword { get; set; } = "";
    public bool MustChangePassword { get; set; } = true;
}

public static class UserNames
{
    public const string Pattern = @"^[A-Za-z0-9._-]+$";
    public const string Rule = "Username can only use letters, numbers, dot (.), dash (-) and underscore (_), with no spaces.";
}
