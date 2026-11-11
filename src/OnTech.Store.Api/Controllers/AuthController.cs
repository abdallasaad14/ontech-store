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
public class AuthController(StoreDbContext db, PasswordService passwords, TokenService tokens, LoginThrottle throttle) : ControllerBase
{
    /// <summary>
    /// Signs in with username and password and returns a bearer token for the other endpoints.
    /// After 5 wrong passwords in a row the username is refused with 429 for 30 s (doubling up to 5 minutes).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest req, CancellationToken ct)
    {
        var normalized = req.Username.Trim().ToUpperInvariant();
        // While locked the password is not checked, so the answer is the same whether it was right or wrong.
        if (throttle.LockedFor(normalized) is { } wait) throw TooManyAttempts(wait);

        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedUsername == normalized, ct);
        if (user is null || !passwords.Verify(user, req.Password, out var rehash))
        {
            // Unknown usernames are counted too, so the answer does not tell which usernames exist.
            throttle.Failed(normalized);
            throw new ApiException(StatusCodes.Status401Unauthorized, "Username or password is incorrect.");
        }
        throttle.Succeeded(normalized);
        if (!user.IsActive)
            throw new ApiException(StatusCodes.Status403Forbidden, "This account is disabled. Ask an admin to enable it.");

        // Signing in is not an edit: write without the Version check, so an admin saving this user at the same moment can't make the login fail with 409.
        if (rehash) user.PasswordHash = passwords.Hash(user, req.Password);
        user.LastLoginAt = DateTime.UtcNow;
        var hash = user.PasswordHash; var at = user.LastLoginAt;
        await db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, hash).SetProperty(u => u.LastLoginAt, at), ct);

        var (token, expires) = tokens.CreateToken(user);
        return new LoginResponse { Token = token, ExpiresAt = expires, User = user.ToDto() };
    }

    private ApiException TooManyAttempts(TimeSpan wait)
    {
        var seconds = LoginThrottle.Seconds(wait);
        Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new ApiException(StatusCodes.Status429TooManyRequests,
            string.Format(System.Globalization.CultureInfo.InvariantCulture, LoginThrottle.TitleFormat, seconds),
            new Dictionary<string, object?> { ["code"] = LoginThrottle.Code });
    }

    /// <summary>The signed-in user, with their current permissions.</summary>
    [AllowWhileMustChangePassword]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == User.GetUserId(), ct);
        return user.ToDto();
    }

    /// <summary>Changes the signed-in user's password and clears the must-change flag.</summary>
    [AllowWhileMustChangePassword]
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
