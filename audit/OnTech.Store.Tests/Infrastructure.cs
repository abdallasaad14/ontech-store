using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;
using OnTech.Store.Client;
using OnTech.Store.Contracts;

namespace OnTech.Store.Tests;

/// <summary>One of the two running audit servers.</summary>
public sealed record Server(string Name, string Url, string? SqlServerDb, string? SqliteFile, string JwtKey)
{
    public const string AdminPassword = "ChangeMe!123";

    public static readonly Server SqlServer = new("sqlserver", "http://127.0.0.1:5081",
        "Server=.\\SQLEXPRESS;Database=OnTechStore;Trusted_Connection=True;TrustServerCertificate=True", null,
        "REPLACE-THIS-with-a-long-random-secret-of-at-least-32-chars");

    public static readonly Server Sqlite = new("sqlite", "http://127.0.0.1:5082", null,
        Path.Combine(Paths.Audit, "build", "api-sqlite", "ontech-store-dev.db"),
        "development-only-signing-key-0123456789abcdef");

    public static Server ByName(string name) => name == "sqlserver" ? SqlServer : Sqlite;
    public static IEnumerable<object[]> Both => [["sqlserver"], ["sqlite"]];

    public override string ToString() => Name;

    // ---- direct DB access (to check what the API really wrote) ----

    public object? Scalar(string sql, params (string, object?)[] args)
    {
        if (SqlServerDb is not null)
        {
            using var c = new SqlConnection(SqlServerDb); c.Open();
            using var cmd = new SqlCommand(sql, c);
            foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
            var r = cmd.ExecuteScalar(); return r is DBNull ? null : r;
        }
        using var s = new SqliteConnection($"Data Source={SqliteFile};Default Timeout=30"); s.Open();
        using var sc = s.CreateCommand(); sc.CommandText = sql;
        foreach (var (k, v) in args) sc.Parameters.AddWithValue(k, v ?? DBNull.Value);
        var sr = sc.ExecuteScalar(); return sr is DBNull ? null : sr;
    }

    public int Exec(string sql, params (string, object?)[] args)
    {
        if (SqlServerDb is not null)
        {
            using var c = new SqlConnection(SqlServerDb); c.Open();
            using var cmd = new SqlCommand(sql, c) { CommandTimeout = 600 };
            foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }
        using var s = new SqliteConnection($"Data Source={SqliteFile};Default Timeout=60"); s.Open();
        using var sc = s.CreateCommand(); sc.CommandText = sql; sc.CommandTimeout = 600;
        foreach (var (k, v) in args) sc.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return sc.ExecuteNonQuery();
    }

    public long Count(string table, string where = "1=1") => Convert.ToInt64(Scalar($"SELECT COUNT(*) FROM {table} WHERE {where}"));
    public int ItemQty(int itemId) => Convert.ToInt32(Scalar("SELECT Quantity FROM Items WHERE Id=@id", ("@id", itemId)));
}

public static class Paths
{
    public static readonly string Audit = FindAudit();
    private static string FindAudit()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "PROGRESS.md"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("audit folder not found");
    }
}

/// <summary>Signs in as admin (fixing up the copied SQLite admin password once) and creates throw-away test data.</summary>
public static class Api
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<string, StoreApiClient> Admins = new();
    public static readonly string Run = DateTime.Now.ToString("MMddHHmmss");
    private static int _seq;
    public static string U(string prefix) => $"{prefix}-{Run}-{Interlocked.Increment(ref _seq)}";

    public static async Task<StoreApiClient> AdminAsync(Server s)
    {
        await Gate.WaitAsync();
        try
        {
            if (Admins.TryGetValue(s.Name, out var c)) return c;
            var api = StoreApiClient.Create(s.Url, TimeSpan.FromSeconds(120));
            try { await api.LoginAsync("admin", Server.AdminPassword); }
            catch (ApiError e) when (e.IsUnauthorized && s.SqliteFile is not null)
            {
                // The SQLite file is a copy of the live DB, whose admin has its own password. Give the COPY's admin the seed password.
                var hash = new OnTech.Store.Api.Services.PasswordService().Hash(new OnTech.Store.Api.Data.User(), Server.AdminPassword);
                s.Exec("UPDATE Users SET PasswordHash=@h WHERE Username='admin'", ("@h", hash));
                await api.LoginAsync("admin", Server.AdminPassword);
            }
            return Admins[s.Name] = api;
        }
        finally { Gate.Release(); }
    }

    public static async Task<(StoreApiClient Client, UserDto User)> UserAsync(Server s, Permission perms, bool mustChange = false, string password = "Passw0rd!x")
    {
        var admin = await AdminAsync(s);
        var name = U("u").Replace("-", "_");
        var user = await admin.CreateUserAsync(new CreateUserRequest { FullName = "Audit " + name, Username = name, Password = password, Permissions = perms, MustChangePassword = mustChange });
        var c = StoreApiClient.Create(s.Url, TimeSpan.FromSeconds(120));
        await c.LoginAsync(name, password);
        return (c, user);
    }

    public static async Task<CategoryDto> CategoryAsync(Server s) =>
        await (await AdminAsync(s)).CreateCategoryAsync(new SaveCategoryRequest { Name = U("AuditCat") });

    public static async Task<ItemDto> ItemAsync(Server s, int stock, int? categoryId = null)
    {
        var admin = await AdminAsync(s);
        categoryId ??= (await CategoryAsync(s)).Id;
        var item = await admin.CreateItemAsync(new CreateItemRequest { CategoryId = categoryId.Value, Name = U("AuditItem") });
        if (stock > 0)
            await admin.CreateStockInAsync(new CreateStockInRequest
            {
                SupplierId = (await SupplierAsync(s)).Id, VoucherNumber = U("IN"),
                Lines = [new OrderLineRequest { ItemId = item.Id, Quantity = stock }],
            });
        return item;
    }

    public static async Task<CustomerDto> CustomerAsync(Server s, string? name = null) =>
        await (await AdminAsync(s)).CreateCustomerAsync(new SaveCustomerRequest { CompanyName = name ?? U("AuditCust") });

    public static async Task<SupplierDto> SupplierAsync(Server s) =>
        await (await AdminAsync(s)).CreateSupplierAsync(new SaveSupplierRequest { CompanyName = U("AuditSupp") });

    public static CreateOrderRequest Order(int customerId, params (int ItemId, int Qty)[] lines) => new()
    {
        CustomerId = customerId, IssueVoucherNumber = U("V"),
        Lines = lines.Select(l => new OrderLineRequest { ItemId = l.ItemId, Quantity = l.Qty }).ToList(),
    };

    // ---- raw HTTP, for requests the typed client can't make ----

    public static HttpClient Raw(Server s, string? token = null)
    {
        var h = new HttpClient { BaseAddress = new Uri(s.Url + "/"), Timeout = TimeSpan.FromSeconds(120) };
        if (token is not null) h.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return h;
    }

    public static async Task<string> TokenAsync(Server s, string user, string password)
    {
        using var h = Raw(s);
        var r = await h.PostAsJsonAsync("api/auth/login", new { username = user, password });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    /// <summary>A token made WITHOUT signing in, only from the signing key that is in the repository.</summary>
    public static string Forge(Server s, int userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(s.JwtKey));
        var token = new JwtSecurityToken("OnTech.Store", "OnTech.Store.Desktop",
            [new Claim("sub", userId.ToString())], DateTime.UtcNow, DateTime.UtcNow.AddHours(1),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static async Task<(HttpStatusCode Status, string Body, double Ms)> SendAsync(HttpClient h, HttpMethod m, string url, object? body = null)
    {
        var sw = Stopwatch.StartNew();
        using var req = new HttpRequestMessage(m, url);
        if (body is not null) req.Content = JsonContent.Create(body, body.GetType(), options: StoreApiClient.Json);
        using var res = await h.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        return (res.StatusCode, text, sw.Elapsed.TotalMilliseconds);
    }
}

/// <summary>Writes evidence lines next to the test results (audit/evidence/tests/*.log).</summary>
public static class Evidence
{
    private static readonly object Lock = new();
    public static void Log(string file, string line)
    {
        var dir = Path.Combine(Paths.Audit, "evidence", "tests");
        Directory.CreateDirectory(dir);
        lock (Lock) File.AppendAllText(Path.Combine(dir, file + ".log"), $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
    }
}
