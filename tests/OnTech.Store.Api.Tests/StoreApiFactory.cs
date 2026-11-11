using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OnTech.Store.Api.Data;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Tests;

/// <summary>
/// Runs the real API in memory against a fresh database per test class: SQLite by default, or SQL Server
/// (through the real migrations) when ONTECH_TEST_SQLSERVER holds a connection string without a database name.
/// </summary>
public sealed class StoreApiFactory : WebApplicationFactory<Program>
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "Admin!12345";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ontech-test-{Guid.NewGuid():N}.db");
    private readonly string? _sqlServer = Environment.GetEnvironmentVariable("ONTECH_TEST_SQLSERVER");
    private readonly string _sqlDbName = $"OnTechStoreTest_{Guid.NewGuid():N}";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>The host environment. "Testing" unless a test needs to see Development or Production behaviour.</summary>
    public string EnvironmentName { get; init; } = "Testing";

    /// <summary>Settings that replace the defaults below. A null value leaves that setting out entirely.</summary>
    public Dictionary<string, string?> Settings { get; init; } = new();

    /// <summary>Replaces services after the app registered its own (for example a fake TimeProvider).</summary>
    public Action<IServiceCollection>? TestServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "test-signing-key-0123456789abcdefghijklmnop",
            ["Seed:AdminUsername"] = AdminUsername,
            ["Seed:AdminPassword"] = AdminPassword,
            // Swagger is off outside Development; the existing Dashboard_and_swagger_respond test reads it, so tests turn it on.
            ["Swagger:Enabled"] = "true",
        };
        if (_sqlServer is not null)
        {
            settings["Database:Provider"] = "SqlServer";
            settings["ConnectionStrings:Store"] = $"{_sqlServer};Database={_sqlDbName}";
        }
        else
        {
            settings["Database:Provider"] = "Sqlite";
            // A busy timeout lets concurrent requests queue for SQLite's write lock.
            settings["ConnectionStrings:Store"] = $"Data Source={_dbPath};Default Timeout=30;Pooling=False";
        }
        foreach (var (k, v) in Settings) settings[k] = v;
        foreach (var (k, v) in settings)
            if (v is not null) builder.UseSetting(k, v);
        if (TestServices is not null) builder.ConfigureTestServices(TestServices);
    }

    /// <summary>
    /// The seeded admin always has MustChangePassword (L-5), and the server refuses a flagged user (M-2).
    /// The tests' admin counts as one who already changed the password, so clear the flag once the database exists.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
        var normalized = AdminUsername.ToUpperInvariant();
        db.Users.Where(u => u.NormalizedUsername == normalized && u.MustChangePassword)
            .ExecuteUpdate(u => u.SetProperty(x => x.MustChangePassword, false));
        return host;
    }

    /// <summary>
    /// A client signed in and ready to use the API. A user created with MustChangePassword counts as one who already changed it
    /// (the flag is cleared in the database), like the desktop app does before it opens the main window.
    /// To test the flag itself, sign in by hand with CreateClient() and POST api/auth/login.
    /// </summary>
    public async Task<HttpClient> LoginAsync(string username = AdminUsername, string password = AdminPassword)
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Username = username, Password = password }, Json);
        res.EnsureSuccessStatusCode();
        var login = (await res.Content.ReadFromJsonAsync<LoginResponse>(Json))!;
        if (login.User.MustChangePassword)
            WithDb(db => db.Users.Where(u => u.Id == login.User.Id)
                .ExecuteUpdate(u => u.SetProperty(x => x.MustChangePassword, false)));
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
        if (_sqlServer is not null)
        {
            // The host may have failed to start (startup tests), so drop the database without it.
            try
            {
                using var db = new StoreDbContext(new DbContextOptionsBuilder<StoreDbContext>().UseSqlServer($"{_sqlServer};Database={_sqlDbName}").Options);
                db.Database.EnsureDeleted();
            }
            catch { /* best effort */ }
        }
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

/// <summary>Stock only changes through stock-in vouchers and orders, so tests fill items the same way.</summary>
public static class TestStock
{
    private static int _next;

    /// <summary>A fresh, unique voucher number.</summary>
    public static string Voucher() => $"V{Interlocked.Increment(ref _next)}-{Guid.NewGuid().ToString("N")[..6]}";

    public static async Task<ItemDto> AddItem(this HttpClient admin, CreateItemRequest req, int quantity)
    {
        var item = await (await admin.Post("/api/items", req)).Read<ItemDto>();
        if (quantity > 0) await admin.StockIn(item.Id, quantity);
        return await admin.GetAs<ItemDto>($"/api/items/{item.Id}");
    }

    public static async Task<StockInDto> StockIn(this HttpClient admin, int itemId, int quantity, SupplierDto? supplier = null)
    {
        supplier ??= await (await admin.Post("/api/suppliers", new SaveSupplierRequest { CompanyName = $"Test supplier {Guid.NewGuid():N}" })).Read<SupplierDto>();
        return await (await admin.Post("/api/stock-ins", new CreateStockInRequest
        {
            SupplierId = supplier.Id, VoucherNumber = Voucher(), Lines = { new StockInLineRequest { ItemId = itemId, Quantity = quantity } },
        })).Read<StockInDto>();
    }
}
