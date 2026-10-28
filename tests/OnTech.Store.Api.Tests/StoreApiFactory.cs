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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        if (_sqlServer is not null)
        {
            builder.UseSetting("Database:Provider", "SqlServer");
            builder.UseSetting("ConnectionStrings:Store", $"{_sqlServer};Database={_sqlDbName}");
        }
        else
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            // A busy timeout lets concurrent requests queue for SQLite's write lock.
            builder.UseSetting("ConnectionStrings:Store", $"Data Source={_dbPath};Default Timeout=30;Pooling=False");
        }
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-0123456789abcdefghijklmnop");
        builder.UseSetting("Seed:AdminUsername", AdminUsername);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
    }

    public async Task<HttpClient> LoginAsync(string username = AdminUsername, string password = AdminPassword)
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Username = username, Password = password }, Json);
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
        if (_sqlServer is not null)
        {
            try { WithDb(db => db.Database.EnsureDeleted()); } catch { /* best effort */ }
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
            SupplierId = supplier.Id, VoucherNumber = Voucher(), Lines = { new OrderLineRequest { ItemId = itemId, Quantity = quantity } },
        })).Read<StockInDto>();
    }
}
