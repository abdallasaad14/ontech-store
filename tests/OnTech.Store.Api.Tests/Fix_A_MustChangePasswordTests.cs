using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Tests;

/// <summary>M-2: while MustChangePassword is set, the server refuses everything except GET api/auth/me and POST api/auth/change-password.</summary>
public class Fix_A_MustChangePasswordTests : IClassFixture<StoreApiFactory>
{
    private const string Title = "You must change your password before you continue.";
    private readonly StoreApiFactory _f;
    public Fix_A_MustChangePasswordTests(StoreApiFactory f) => _f = f;

    private async Task<(HttpClient client, UserDto user)> NewFlaggedUserAsync(string password)
    {
        var admin = await _f.LoginAsync();
        var username = $"mcp{Guid.NewGuid():N}"[..14];
        var user = await (await admin.Post("/api/users", new CreateUserRequest
        {
            FullName = "Flagged", Username = username, Password = password,
            Permissions = Permission.ViewStock | Permission.ManageCustomers, MustChangePassword = true,
        })).Read<UserDto>();

        // Sign in by hand: the factory's LoginAsync clears the flag on purpose.
        var client = _f.CreateClient();
        var login = await (await client.Post("/api/auth/login", new LoginRequest { Username = username, Password = password })).Read<LoginResponse>();
        Assert.True(login.User.MustChangePassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        return (client, user);
    }

    private static async Task AssertMustChange(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        using var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(Title, body.RootElement.GetProperty("title").GetString());
        Assert.Equal(ErrorCodes.MustChangePassword, body.RootElement.GetProperty("code").GetString());
        Assert.Equal(403, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Flagged_user_is_refused_until_the_password_is_changed()
    {
        var (client, _) = await NewFlaggedUserAsync("First!1234");

        await AssertMustChange(await client.GetAsync("/api/items"));
        await AssertMustChange(await client.GetAsync("/api/customers"));
        await AssertMustChange(await client.Post("/api/customers", new SaveCustomerRequest { CompanyName = "Should not be saved " + Guid.NewGuid().ToString("N")[..6] }));

        // Allowed while flagged.
        var me = await client.GetAs<UserDto>("/api/auth/me");
        Assert.True(me.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.Post("/api/auth/change-password",
            new ChangePasswordRequest { CurrentPassword = "First!1234", NewPassword = "Second!1234" })).StatusCode);

        // The same token works now: the flag is read from the database on each request.
        Assert.False((await client.GetAs<UserDto>("/api/auth/me")).MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/items")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/customers")).StatusCode);
    }

    [Fact]
    public async Task Admin_reset_with_must_change_flags_the_user_again()
    {
        var (client, user) = await NewFlaggedUserAsync("First!1234");
        await client.Post("/api/auth/change-password", new ChangePasswordRequest { CurrentPassword = "First!1234", NewPassword = "Second!1234" });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/items")).StatusCode);

        var admin = await _f.LoginAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Post($"/api/users/{user.Id}/reset-password",
            new ResetPasswordRequest { NewPassword = "Third!1234", MustChangePassword = true })).StatusCode);

        // The old token is refused with the must-change answer, not served.
        await AssertMustChange(await client.GetAsync("/api/items"));
    }

    [Fact]
    public async Task Wrong_current_password_keeps_the_flag()
    {
        var (client, _) = await NewFlaggedUserAsync("First!1234");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.Post("/api/auth/change-password",
            new ChangePasswordRequest { CurrentPassword = "Wrong!1234", NewPassword = "Second!1234" })).StatusCode);
        await AssertMustChange(await client.GetAsync("/api/items"));
    }

    [Fact]
    public async Task Test_admin_is_not_flagged()
    {
        var admin = await _f.LoginAsync();
        Assert.False((await admin.GetAs<UserDto>("/api/auth/me")).MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/users")).StatusCode);
    }
}
