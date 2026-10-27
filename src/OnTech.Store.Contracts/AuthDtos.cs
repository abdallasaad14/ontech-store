using System.ComponentModel.DataAnnotations;

namespace OnTech.Store.Contracts;

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(256)] public string Email { get; set; } = "";
    [Required, MaxLength(128)] public string Password { get; set; } = "";
}

public sealed class LoginResponse
{
    public required string Token { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public required UserDto User { get; init; }
}

public sealed class ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; set; } = "";
    [Required, MinLength(8), MaxLength(128)] public string NewPassword { get; set; } = "";
}
