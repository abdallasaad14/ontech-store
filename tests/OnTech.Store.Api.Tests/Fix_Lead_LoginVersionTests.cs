using System.Net;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Tests;

/// <summary>Signing in is not an edit: an admin saving the same user at the same moment must not turn the login into 409.</summary>
public class Fix_Lead_LoginVersionTests(StoreApiFactory f) : IClassFixture<StoreApiFactory>
{
    [Fact]
    public async Task Logins_while_an_admin_edits_the_same_user_all_succeed()
    {
        var admin = await f.LoginAsync();
        var name = "lv" + Guid.NewGuid().ToString("N")[..8];
        var u = await (await admin.Post("/api/users", new CreateUserRequest { FullName = name, Username = name, Password = "Password1!", Permissions = Permission.ViewStock })).Read<UserDto>();

        var logins = Enumerable.Range(0, 20).Select(_ =>
            f.CreateClient().Post("/api/auth/login", new LoginRequest { Username = name, Password = "Password1!" }));
        // One admin editing one save after another (parallel edits would rightly conflict with each other).
        async Task<HttpStatusCode[]> Edits()
        {
            var codes = new List<HttpStatusCode>();
            for (var i = 0; i < 20; i++)
                codes.Add((await admin.Put($"/api/users/{u.Id}", new UpdateUserRequest { FullName = $"Name {i}", Username = name, IsActive = true, Permissions = Permission.ViewStock })).StatusCode);
            return [.. codes];
        }
        var edits = Edits();
        var loginResults = await Task.WhenAll(logins);

        Assert.All(loginResults, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.All(await edits, c => Assert.Equal(HttpStatusCode.OK, c));
        Assert.Equal(20, (await admin.GetAs<UserDto>($"/api/users/{u.Id}")).Version); // logins did not bump it
    }
}
