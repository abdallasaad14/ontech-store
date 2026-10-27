using Microsoft.AspNetCore.Identity;
using OnTech.Store.Api.Data;

namespace OnTech.Store.Api.Services;

/// <summary>PBKDF2 hashing from ASP.NET Core Identity; plain passwords are never stored.</summary>
public sealed class PasswordService
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(User user, string password, out bool needsRehash)
    {
        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
        return result != PasswordVerificationResult.Failed;
    }
}
