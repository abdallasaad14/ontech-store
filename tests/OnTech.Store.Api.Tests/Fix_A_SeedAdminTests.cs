using System.Net;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Tests;

/// <summary>L-5: no default admin password anywhere; a new database without Seed:AdminPassword refuses to start.</summary>
public class Fix_A_SeedAdminTests
{
    [Fact]
    public void New_database_without_admin_password_does_not_start()
    {
        using var f = new StoreApiFactory { Settings = { ["Seed:AdminPassword"] = null } };
        var e = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        var messages = string.Join(" | ", Enumerable.Repeat(e, 1).Concat(e is AggregateException a ? a.InnerExceptions : Enumerable.Empty<Exception>()).SelectMany(Chain));
        Assert.Contains("Seed__AdminPassword", messages);

        static IEnumerable<string> Chain(Exception x) { for (Exception? i = x; i is not null; i = i.InnerException) yield return i.Message; }
    }

    [Fact]
    public async Task Seeded_admin_must_change_the_password()
    {
        // A username other than the factory's "admin", whose flag the factory clears for the other tests.
        using var f = new StoreApiFactory { Settings = { ["Seed:AdminUsername"] = "boss" } };
        var res = await f.CreateClient().Post("/api/auth/login", new LoginRequest { Username = "boss", Password = StoreApiFactory.AdminPassword });
        var login = await res.Read<LoginResponse>();
        Assert.True(login.User.MustChangePassword);
    }

    [Fact]
    public void Appsettings_has_no_admin_password()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OnTech.Store.sln"))) dir = dir.Parent;
        foreach (var file in Directory.GetFiles(Path.Combine(dir!.FullName, "src", "OnTech.Store.Api"), "appsettings*.json"))
            Assert.DoesNotContain("AdminPassword", File.ReadAllText(file));
        Assert.Equal("", new OnTech.Store.Api.Data.SeedOptions().AdminPassword);
    }
}
