using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnTech.Store.Api.Data;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Tests;

/// <summary>Runs the real API in memory against a fresh SQLite database file per test class.</summary>
public sealed class StoreApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin!12345";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ontech-test-{Guid.NewGuid():N}.db");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        // Shared cache + a busy timeout lets concurrent requests queue for the write lock like SQL Server row locks would.
        builder.UseSetting("ConnectionStrings:Store", $"Data Source={_dbPath};Default Timeout=30;Pooling=False");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-0123456789abcdefghijklmnop");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
    }

    public async Task<HttpClient> LoginAsync(string email = AdminEmail, string password = AdminPassword)
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password }, Json);
        res.EnsureSuccessStatusCode();
        var login = (await res.Content.ReadFromJsonAsync<LoginResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        return client;
    }

    public T WithDb<T>(Func<StoreDbContext, T> f)
    {
        using var scope = Services.CreateScope();
        return f(scope.ServiceProvider.GetRequiredService<StoreDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}

public static class HttpExtensions
{
    public static async Task<T> Read<T>(this HttpResponseMessage res)
    {
        if (!res.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)res.StatusCode} {res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>(StoreApiFactory.Json))!;
    }

    public static Task<HttpResponseMessage> Post<T>(this HttpClient c, string url, T body) => c.PostAsJsonAsync(url, body, StoreApiFactory.Json);
    public static Task<HttpResponseMessage> Put<T>(this HttpClient c, string url, T body) => c.PutAsJsonAsync(url, body, StoreApiFactory.Json);
    public static async Task<T> GetAs<T>(this HttpClient c, string url) => await (await c.GetAsync(url)).Read<T>();
}
