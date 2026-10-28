using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Auth;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Api.Services;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Controllers;

[ApiController]
[Route("api/users")]
[HasPermission(Permission.ManageUsers)]
public class UsersController(StoreDbContext db, PasswordService passwords) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<UserDto>> List([FromQuery] bool includeInactive = true, CancellationToken ct = default)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => includeInactive || u.IsActive)
            .OrderBy(u => u.FullName).ToListAsync(ct);
        return users.Select(u => u.ToDto()).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserDto>> Get(int id, CancellationToken ct) =>
        (await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw ApiException.NotFound("User")).ToDto();

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest req, CancellationToken ct)
    {
        var username = req.Username.Trim();
        await EnsureUsernameFree(username, null, ct);
        var user = new User
        {
            FullName = req.FullName.Trim(),
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            IsAdmin = req.IsAdmin,
            Permissions = req.Permissions & Permission.All,
            MustChangePassword = req.MustChangePassword,
            CreatedAt = DateTime.UtcNow,
        };
        user.PasswordHash = passwords.Hash(user, req.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(int id, UpdateUserRequest req, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw ApiException.NotFound("User");
        var username = req.Username.Trim();
        await EnsureUsernameFree(username, id, ct);

        var losesAdmin = user.IsAdmin && user.IsActive && (!req.IsAdmin || !req.IsActive);
        if (losesAdmin && !await db.Users.AnyAsync(u => u.Id != id && u.IsAdmin && u.IsActive, ct))
            throw ApiException.Conflict("You can't remove or disable the last active admin.");

        if (id == User.GetUserId() && (!req.IsActive || (user.IsAdmin && !req.IsAdmin)))
            throw ApiException.BadRequest("You can't disable your own account or remove your own admin rights.");

        user.FullName = req.FullName.Trim();
        user.Username = username;
        user.NormalizedUsername = username.ToUpperInvariant();
        user.IsAdmin = req.IsAdmin;
        user.IsActive = req.IsActive;
        user.Permissions = req.Permissions & Permission.All;
        await db.SaveChangesAsync(ct);
        return user.ToDto();
    }

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest req, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw ApiException.NotFound("User");
        user.PasswordHash = passwords.Hash(user, req.NewPassword);
        user.MustChangePassword = req.MustChangePassword;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Disables the user. Users are never hard-deleted because orders reference them.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Disable(int id, CancellationToken ct)
    {
        if (id == User.GetUserId()) throw ApiException.BadRequest("You can't disable your own account.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw ApiException.NotFound("User");
        if (user.IsAdmin && !await db.Users.AnyAsync(u => u.Id != id && u.IsAdmin && u.IsActive, ct))
            throw ApiException.Conflict("You can't disable the last active admin.");
        user.IsActive = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task EnsureUsernameFree(string username, int? exceptId, CancellationToken ct)
    {
        var normalized = username.ToUpperInvariant();
        if (await db.Users.AnyAsync(u => u.NormalizedUsername == normalized && u.Id != exceptId, ct))
            throw ApiException.Conflict($"The username '{username}' is already taken.");
    }
}
