using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Auth;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Api.Services;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(StoreDbContext db, PasswordService passwords, TokenService tokens) : ControllerBase
{
    /// <summary>Signs in with email and password and returns a bearer token for the other endpoints.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest req, CancellationToken ct)
    {
        var normalized = req.Email.Trim().ToUpperInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is null || !passwords.Verify(user, req.Password, out var rehash))
            throw new ApiException(StatusCodes.Status401Unauthorized, "Email or password is incorrect.");
        if (!user.IsActive)
            throw new ApiException(StatusCodes.Status403Forbidden, "This account is disabled. Ask an admin to enable it.");

        if (rehash) user.PasswordHash = passwords.Hash(user, req.Password);
        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var (token, expires) = tokens.CreateToken(user);
        return new LoginResponse { Token = token, ExpiresAt = expires, User = user.ToDto() };
    }

    /// <summary>The signed-in user, with their current permissions.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == User.GetUserId(), ct);
        return user.ToDto();
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == User.GetUserId(), ct);
        if (!passwords.Verify(user, req.CurrentPassword, out _))
            throw ApiException.BadRequest("Current password is incorrect.");
        user.PasswordHash = passwords.Hash(user, req.NewPassword);
        user.MustChangePassword = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
