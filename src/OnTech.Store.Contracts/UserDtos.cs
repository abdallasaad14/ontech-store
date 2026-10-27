using System.ComponentModel.DataAnnotations;

namespace OnTech.Store.Contracts;

public sealed class UserDto
{
    public int Id { get; init; }
    public string FullName { get; init; } = "";
    public string Email { get; init; } = "";
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
    [Required, EmailAddress, MaxLength(256)] public string Email { get; set; } = "";
    [Required, MinLength(8), MaxLength(128)] public string Password { get; set; } = "";
    public bool IsAdmin { get; set; }
    public Permission Permissions { get; set; } = Permission.StandardUser;
    /// <summary>Ask the user to pick their own password on first login.</summary>
    public bool MustChangePassword { get; set; } = true;
}

public sealed class UpdateUserRequest
{
    [Required, MaxLength(150)] public string FullName { get; set; } = "";
    [Required, EmailAddress, MaxLength(256)] public string Email { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool IsActive { get; set; } = true;
    public Permission Permissions { get; set; }
}

public sealed class ResetPasswordRequest
{
    [Required, MinLength(8), MaxLength(128)] public string NewPassword { get; set; } = "";
    public bool MustChangePassword { get; set; } = true;
}
