using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OnTech.Store.Api.Auth;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Tests;

/// <summary>L-2: 5 wrong passwords in a row lock the username for 30 s (429), doubling up to 5 minutes; a right password resets it.</summary>
public class Fix_A_LoginLockoutTests
{
    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static StoreApiFactory NewFactory(FakeClock clock) =>
        new() { TestServices = s => s.AddSingleton<TimeProvider>(clock) };

    private static Task<HttpResponseMessage> Login(HttpClient c, string password, string username = StoreApiFactory.AdminUsername) =>
        c.Post("/api/auth/login", new LoginRequest { Username = username, Password = password });

    private static async Task WrongTimes(HttpClient c, int times, string username = StoreApiFactory.AdminUsername)
    {
        for (var i = 0; i < times; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, "Wrong!" + i, username)).StatusCode);
    }

    private static async Task<string> AssertLocked(HttpResponseMessage res, int seconds)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
        var text = await res.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        Assert.Equal($"Too many wrong passwords. Wait {seconds} seconds and try again.", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("TooManyAttempts", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(seconds.ToString(), res.Headers.GetValues("Retry-After").Single());
        return text;
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_username_for_30_seconds()
    {
        var clock = new FakeClock();
        using var f = NewFactory(clock);
        var c = f.CreateClient();

        await WrongTimes(c, 5);
        // The right password is refused too, with the same answer as a wrong one: nothing tells which it was.
        var right = await AssertLocked(await Login(c, StoreApiFactory.AdminPassword), 30);
        var wrong = await AssertLocked(await Login(c, "Wrong!x"), 30);
        Assert.Equal(right, wrong);

        clock.Advance(TimeSpan.FromSeconds(12));
        await AssertLocked(await Login(c, StoreApiFactory.AdminPassword), 18);

        clock.Advance(TimeSpan.FromSeconds(18));
        Assert.Equal(HttpStatusCode.OK, (await Login(c, StoreApiFactory.AdminPassword)).StatusCode);
    }

    [Fact]
    public async Task Each_further_lockout_doubles_up_to_5_minutes()
    {
        var clock = new FakeClock();
        using var f = NewFactory(clock);
        var c = f.CreateClient();

        foreach (var seconds in new[] { 30, 60, 120, 240, 300, 300 })
        {
            await WrongTimes(c, 5);
            await AssertLocked(await Login(c, "Wrong!x"), seconds);
            clock.Advance(TimeSpan.FromSeconds(seconds));
        }

        // A right password resets the doubling.
        Assert.Equal(HttpStatusCode.OK, (await Login(c, StoreApiFactory.AdminPassword)).StatusCode);
        await WrongTimes(c, 5);
        await AssertLocked(await Login(c, "Wrong!x"), 30);
    }

    [Fact]
    public async Task A_right_password_resets_the_count()
    {
        var clock = new FakeClock();
        using var f = NewFactory(clock);
        var c = f.CreateClient();

        await WrongTimes(c, 4);
        Assert.Equal(HttpStatusCode.OK, (await Login(c, StoreApiFactory.AdminPassword)).StatusCode);
        await WrongTimes(c, 4);
        Assert.Equal(HttpStatusCode.OK, (await Login(c, StoreApiFactory.AdminPassword)).StatusCode);
    }

    [Fact]
    public async Task Lockout_is_per_username()
    {
        var clock = new FakeClock();
        using var f = NewFactory(clock);
        var admin = await f.LoginAsync();
        var username = $"lock{Guid.NewGuid():N}"[..14];
        await (await admin.Post("/api/users", new CreateUserRequest
        {
            FullName = "Locked", Username = username, Password = "Right!1234", Permissions = Permission.ViewStock, MustChangePassword = false,
        })).Read<UserDto>();

        var c = f.CreateClient();
        await WrongTimes(c, 5, username);
        await AssertLocked(await Login(c, "Right!1234", username), 30);
        // Upper/lower case and spaces are the same username.
        await AssertLocked(await Login(c, "Right!1234", " " + username.ToUpperInvariant()), 30);
        Assert.Equal(HttpStatusCode.OK, (await Login(c, StoreApiFactory.AdminPassword)).StatusCode);
    }

    [Fact]
    public async Task Unknown_usernames_lock_the_same_way()
    {
        var clock = new FakeClock();
        using var f = NewFactory(clock);
        var c = f.CreateClient();
        await WrongTimes(c, 5, "nobody-here");
        await AssertLocked(await Login(c, "x", "nobody-here"), 30);
    }

    [Fact]
    public async Task Parallel_wrong_passwords_are_counted_safely()
    {
        var clock = new FakeClock();
        var throttle = new LoginThrottle(clock);
        var locks = await Task.WhenAll(Enumerable.Range(0, 1000).Select(_ => Task.Run(() => throttle.Failed("PARALLEL"))));
        // The first four failures leave it unlocked; every other call sees the single 30 s lockout.
        Assert.Equal(LoginThrottle.MaxFailures - 1, locks.Count(l => l is null));
        Assert.All(locks.Where(l => l is not null), l => Assert.Equal(TimeSpan.FromSeconds(30), l));
        Assert.Equal(TimeSpan.FromSeconds(30), throttle.LockedFor("PARALLEL"));
    }
}
