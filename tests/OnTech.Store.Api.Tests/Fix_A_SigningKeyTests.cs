using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using OnTech.Store.Api.Auth;
using OnTech.Store.Api.Data;

namespace OnTech.Store.Api.Tests;

/// <summary>H-4: the JWT signing key is not in git, the leaked sample keys never sign a valid token, and Production refuses to start without a key.</summary>
public class Fix_A_SigningKeyTests
{
    private static string Forge(string key) =>
        new TokenService(Options.Create(new JwtOptions { SigningKey = key }))
            .CreateToken(new User { Id = 1, Username = "admin", FullName = "Administrator" }).Token;

    private static string AllMessages(Exception e)
    {
        var parts = new List<string>();
        for (Exception? x = e; x is not null; x = x.InnerException) parts.Add(x.Message);
        if (e is AggregateException agg) parts.AddRange(agg.InnerExceptions.Select(i => i.Message));
        return string.Join(" | ", parts);
    }

    [Theory]
    [InlineData("development-only-signing-key-0123456789abcdef")]
    [InlineData("REPLACE-THIS-with-a-long-random-secret-of-at-least-32-chars")]
    public async Task Token_signed_with_a_leaked_key_is_refused_in_development(string leakedKey)
    {
        // No key configured: before the fix Development used the key from appsettings.Development.json.
        using var f = new StoreApiFactory { EnvironmentName = "Development", Settings = { ["Jwt:SigningKey"] = null } };
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Forge(leakedKey));

        var res = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        // The server still works with its generated key.
        var admin = await f.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("development-only-signing-key-0123456789abcdef")]
    [InlineData("REPLACE-THIS-with-a-long-random-secret-of-at-least-32-chars")]
    public async Task Token_signed_with_a_leaked_key_is_refused_by_the_test_server(string leakedKey)
    {
        using var f = new StoreApiFactory();
        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Forge(leakedKey));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public void Production_without_a_key_does_not_start()
    {
        using var f = new StoreApiFactory { EnvironmentName = "Production", Settings = { ["Jwt:SigningKey"] = "" } };
        var e = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("Jwt__SigningKey", AllMessages(e));
        Assert.Contains("is not set", AllMessages(e));
    }

    [Fact]
    public void Production_with_a_short_key_does_not_start()
    {
        using var f = new StoreApiFactory { EnvironmentName = "Production", Settings = { ["Jwt:SigningKey"] = "short-key-0123456789" } };
        var e = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("too short", AllMessages(e));
    }

    [Theory]
    [InlineData("development-only-signing-key-0123456789abcdef")]
    [InlineData("REPLACE-THIS-with-a-long-random-secret-of-at-least-32-chars")]
    public void Production_with_a_leaked_key_does_not_start(string leakedKey)
    {
        using var f = new StoreApiFactory { EnvironmentName = "Production", Settings = { ["Jwt:SigningKey"] = leakedKey } };
        var e = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("published in git", AllMessages(e));
    }

    [Fact]
    public void No_tracked_appsettings_file_holds_a_signing_key()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OnTech.Store.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var files = Directory.GetFiles(Path.Combine(dir!.FullName, "src", "OnTech.Store.Api"), "appsettings*.json", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(files);
        foreach (var file in files)
            Assert.DoesNotContain("SigningKey", File.ReadAllText(file));
    }
}
