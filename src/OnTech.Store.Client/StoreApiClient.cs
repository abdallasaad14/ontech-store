using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnTech.Store.Contracts;

namespace OnTech.Store.Client;

/// <summary>
/// Typed access to the OnTech Store server. One instance per signed-in session; every call throws
/// <see cref="ApiError"/> with a message that can be shown as-is.
/// </summary>
public sealed class StoreApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _http;

    /// <param name="http">Its <see cref="HttpClient.BaseAddress"/> must point at the server, e.g. http://192.168.1.10:5080/.</param>
    public StoreApiClient(HttpClient http) => _http = http;

    public static StoreApiClient Create(string serverUrl, TimeSpan? timeout = null) =>
        new(new HttpClient { BaseAddress = NormalizeServerUrl(serverUrl), Timeout = timeout ?? TimeSpan.FromSeconds(20) });

    /// <summary>Accepts "192.168.1.10", "pc-name:5080" or a full URL, and fills in http:// and the default port 5080.</summary>
    public static Uri NormalizeServerUrl(string serverUrl)
    {
        var text = serverUrl.Trim();
        if (text.Length == 0) throw new ArgumentException("Enter the server address.");
        var hasScheme = text.Contains("://", StringComparison.Ordinal);
        if (!hasScheme) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ArgumentException($"\"{serverUrl}\" is not a valid server address.");
        var builder = new UriBuilder(uri) { Path = "/", Query = "", Fragment = "" };
        if (!hasScheme && !HasExplicitPort(serverUrl)) builder.Port = 5080;
        return builder.Uri;
    }

    private static bool HasExplicitPort(string text)
    {
        var host = text.Trim().Split('/')[0];
        return host.Contains(':') && int.TryParse(host[(host.LastIndexOf(':') + 1)..], out _);
    }

    public Uri? ServerUrl => _http.BaseAddress;
    public UserDto? CurrentUser { get; private set; }
    public DateTimeOffset? TokenExpiresAt { get; private set; }

    // ---- Auth ----

    public async Task CheckHealthAsync(CancellationToken ct = default) => await SendAsync(HttpMethod.Get, "api/health", null, ct);

    public async Task<UserDto> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var res = await SendAsync<LoginResponse>(HttpMethod.Post, "api/auth/login", new LoginRequest { Username = username.Trim(), Password = password }, ct);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", res.Token);
        TokenExpiresAt = res.ExpiresAt;
        return CurrentUser = res.User;
    }

    public void Logout()
    {
        _http.DefaultRequestHeaders.Authorization = null;
        CurrentUser = null;
        TokenExpiresAt = null;
    }

    public async Task<UserDto> RefreshMeAsync(CancellationToken ct = default) =>
        CurrentUser = await SendAsync<UserDto>(HttpMethod.Get, "api/auth/me", null, ct);

    public Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/auth/change-password", new ChangePasswordRequest { CurrentPassword = currentPassword, NewPassword = newPassword }, ct);

    // ---- Dashboard ----

    public Task<DashboardSummaryDto> GetDashboardAsync(CancellationToken ct = default) =>
        SendAsync<DashboardSummaryDto>(HttpMethod.Get,
            "api/dashboard/summary" + Query(("utcOffsetMinutes", (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes)), null, ct);

    // ---- Categories & items ----

    public Task<List<CategoryDto>> GetCategoriesAsync(bool includeInactive = false, CancellationToken ct = default) =>
        SendAsync<List<CategoryDto>>(HttpMethod.Get, "api/categories" + Query(("includeInactive", includeInactive)), null, ct);

    public Task<CategoryDto> CreateCategoryAsync(SaveCategoryRequest req, CancellationToken ct = default) =>
        SendAsync<CategoryDto>(HttpMethod.Post, "api/categories", req, ct);

    public Task<CategoryDto> UpdateCategoryAsync(int id, SaveCategoryRequest req, CancellationToken ct = default) =>
        SendAsync<CategoryDto>(HttpMethod.Put, $"api/categories/{id}", req, ct);

    public Task DeleteCategoryAsync(int id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/categories/{id}", null, ct);

    public Task<List<ItemDto>> GetItemsAsync(int? categoryId = null, string? search = null, bool includeInactive = false,
        bool inStockOnly = false, bool lowStockOnly = false, CancellationToken ct = default) =>
        SendAsync<List<ItemDto>>(HttpMethod.Get, "api/items" + Query(
            ("categoryId", categoryId), ("search", search), ("includeInactive", includeInactive),
            ("inStockOnly", inStockOnly), ("lowStockOnly", lowStockOnly)), null, ct);

    public Task<ItemDto> GetItemAsync(int id, CancellationToken ct = default) =>
        SendAsync<ItemDto>(HttpMethod.Get, $"api/items/{id}", null, ct);

    public Task<ItemCardDto> GetItemCardAsync(int id, ItemCardQuery? q = null, CancellationToken ct = default) =>
        SendAsync<ItemCardDto>(HttpMethod.Get, $"api/items/{id}/card" + Query(("from", q?.From), ("to", q?.To)), null, ct);

    public Task<ItemDto> CreateItemAsync(CreateItemRequest req, CancellationToken ct = default) =>
        SendAsync<ItemDto>(HttpMethod.Post, "api/items", req, ct);

    public Task<ItemDto> UpdateItemAsync(int id, UpdateItemRequest req, CancellationToken ct = default) =>
        SendAsync<ItemDto>(HttpMethod.Put, $"api/items/{id}", req, ct);

    public Task DisableItemAsync(int id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/items/{id}", null, ct);

    // ---- Customers ----

    public Task<List<CustomerDto>> GetCustomersAsync(string? search = null, bool includeInactive = false, CancellationToken ct = default) =>
        SendAsync<List<CustomerDto>>(HttpMethod.Get, "api/customers" + Query(("search", search), ("includeInactive", includeInactive)), null, ct);

    public Task<CustomerDto> CreateCustomerAsync(SaveCustomerRequest req, CancellationToken ct = default) =>
        SendAsync<CustomerDto>(HttpMethod.Post, "api/customers", req, ct);

    public Task<CustomerDto> UpdateCustomerAsync(int id, SaveCustomerRequest req, CancellationToken ct = default) =>
        SendAsync<CustomerDto>(HttpMethod.Put, $"api/customers/{id}", req, ct);

    public Task<ImportCustomersResult> ImportCustomersAsync(ImportCustomersRequest req, CancellationToken ct = default) =>
        SendAsync<ImportCustomersResult>(HttpMethod.Post, "api/customers/import", req, ct);

    public Task DisableCustomerAsync(int id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/customers/{id}", null, ct);

    // ---- Suppliers ----

    public Task<List<SupplierDto>> GetSuppliersAsync(string? search = null, bool includeInactive = false, CancellationToken ct = default) =>
        SendAsync<List<SupplierDto>>(HttpMethod.Get, "api/suppliers" + Query(("search", search), ("includeInactive", includeInactive)), null, ct);

    public Task<SupplierDto> CreateSupplierAsync(SaveSupplierRequest req, CancellationToken ct = default) =>
        SendAsync<SupplierDto>(HttpMethod.Post, "api/suppliers", req, ct);

    public Task<SupplierDto> UpdateSupplierAsync(int id, SaveSupplierRequest req, CancellationToken ct = default) =>
        SendAsync<SupplierDto>(HttpMethod.Put, $"api/suppliers/{id}", req, ct);

    public Task DisableSupplierAsync(int id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/suppliers/{id}", null, ct);

    // ---- Stock in (from suppliers) ----

    public Task<StockInDto> CreateStockInAsync(CreateStockInRequest req, CancellationToken ct = default) =>
        SendAsync<StockInDto>(HttpMethod.Post, "api/stock-ins", req, ct);

    public Task<PagedResult<StockInDto>> SearchStockInsAsync(StockInQuery q, CancellationToken ct = default) =>
        SendAsync<PagedResult<StockInDto>>(HttpMethod.Get, "api/stock-ins" + Query(
            ("search", q.Search), ("from", q.From), ("to", q.To), ("supplierId", q.SupplierId), ("itemId", q.ItemId),
            ("status", q.Status), ("page", q.Page), ("pageSize", q.PageSize)), null, ct);

    public Task<StockInDto> GetStockInAsync(int id, CancellationToken ct = default) =>
        SendAsync<StockInDto>(HttpMethod.Get, $"api/stock-ins/{id}", null, ct);

    public Task<StockInDto> CancelStockInAsync(int id, string reason, CancellationToken ct = default) =>
        SendAsync<StockInDto>(HttpMethod.Post, $"api/stock-ins/{id}/cancel", new CancelStockInRequest { Reason = reason }, ct);

    // ---- Orders ----

    public Task<OrderDto> CreateOrderAsync(CreateOrderRequest req, CancellationToken ct = default) =>
        SendAsync<OrderDto>(HttpMethod.Post, "api/orders", req, ct);

    public Task<PagedResult<OrderDto>> SearchOrdersAsync(OrderQuery q, CancellationToken ct = default) =>
        SendAsync<PagedResult<OrderDto>>(HttpMethod.Get, "api/orders" + Query(
            ("search", q.Search), ("from", q.From), ("to", q.To), ("customerId", q.CustomerId), ("categoryId", q.CategoryId),
            ("itemId", q.ItemId), ("createdByUserId", q.CreatedByUserId), ("withReceipt", q.WithReceipt),
            ("receiptNumber", q.ReceiptNumber), ("orderNumber", q.OrderNumber), ("issueVoucherNumber", q.IssueVoucherNumber), ("status", q.Status),
            ("sort", q.Sort), ("page", q.Page), ("pageSize", q.PageSize)), null, ct);

    public Task<OrderDto> GetOrderAsync(int id, CancellationToken ct = default) =>
        SendAsync<OrderDto>(HttpMethod.Get, $"api/orders/{id}", null, ct);

    public Task<OrderDto> CancelOrderAsync(int id, string reason, CancellationToken ct = default) =>
        SendAsync<OrderDto>(HttpMethod.Post, $"api/orders/{id}/cancel", new CancelOrderRequest { Reason = reason }, ct);

    // ---- Users ----

    public Task<List<UserDto>> GetUsersAsync(bool includeInactive = true, CancellationToken ct = default) =>
        SendAsync<List<UserDto>>(HttpMethod.Get, "api/users" + Query(("includeInactive", includeInactive)), null, ct);

    public Task<UserDto> CreateUserAsync(CreateUserRequest req, CancellationToken ct = default) =>
        SendAsync<UserDto>(HttpMethod.Post, "api/users", req, ct);

    public Task<UserDto> UpdateUserAsync(int id, UpdateUserRequest req, CancellationToken ct = default) =>
        SendAsync<UserDto>(HttpMethod.Put, $"api/users/{id}", req, ct);

    public Task ResetPasswordAsync(int id, string newPassword, bool mustChangePassword, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/users/{id}/reset-password", new ResetPasswordRequest { NewPassword = newPassword, MustChangePassword = mustChangePassword }, ct);

    public Task DisableUserAsync(int id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/users/{id}", null, ct);

    // ---- Plumbing ----

    private static string Query(params (string Key, object? Value)[] pairs)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in pairs)
        {
            var text = value switch
            {
                null => null,
                bool b => b ? "true" : "false",
                string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
                DateTimeOffset d => d.ToString("o", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
            if (text is null) continue;
            sb.Append(sb.Length == 0 ? '?' : '&').Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(text));
        }
        return sb.ToString();
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var res = await SendCoreAsync(method, url, body, ct);
        return (await res.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private async Task SendAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var res = await SendCoreAsync(method, url, body, ct);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body, body.GetType(), options: Json);
        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiError(null, $"Cannot reach the server at {_http.BaseAddress}. Check that the server PC is on and connected to the network.", inner: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ApiError(null, $"The server at {_http.BaseAddress} did not answer in time.", inner: ex);
        }
        if (res.IsSuccessStatusCode) return res;
        using (res) throw await ToErrorAsync(res, ct);
    }

    private static async Task<ApiError> ToErrorAsync(HttpResponseMessage res, CancellationToken ct)
    {
        string? title = null;
        List<StockShortage>? shortages = null;
        try
        {
            var problem = await res.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
            if (problem.ValueKind == JsonValueKind.Object)
            {
                if (problem.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    // Validation errors: show the individual messages, not "One or more validation errors occurred."
                    var messages = errors.EnumerateObject()
                        .SelectMany(p => p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().Select(m => m.GetString()) : [])
                        .Where(m => !string.IsNullOrWhiteSpace(m));
                    title = string.Join(Environment.NewLine, messages);
                }
                if (string.IsNullOrWhiteSpace(title) && problem.TryGetProperty("title", out var t)) title = t.GetString();
                if (problem.TryGetProperty("shortages", out var s) && s.ValueKind == JsonValueKind.Array)
                    shortages = s.Deserialize<List<StockShortage>>(Json);
            }
        }
        catch (JsonException) { /* not a ProblemDetails body */ }
        catch (NotSupportedException) { /* no content type */ }

        // Framework-generated bodies (401/403/404 from routing or auth) only carry the bare status name.
        if (string.IsNullOrWhiteSpace(title) || string.Equals(title, res.ReasonPhrase, StringComparison.OrdinalIgnoreCase)
            || string.Equals(title.Replace(" ", ""), res.StatusCode.ToString(), StringComparison.OrdinalIgnoreCase))
            title = DefaultMessage(res.StatusCode);
        if (shortages is { Count: > 0 })
            title += Environment.NewLine + string.Join(Environment.NewLine,
                shortages.Select(x => $"• {x.ItemName}: asked for {x.Requested}, only {x.Available} available"));
        return new ApiError(res.StatusCode, title, shortages);
    }

    private static string DefaultMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Your session has ended. Please sign in again.",
        HttpStatusCode.Forbidden => "You don't have permission to do this. Ask an admin.",
        HttpStatusCode.NotFound => "That record was not found. It may have been removed on another PC.",
        _ => $"The server returned an error ({(int)status}). Please try again.",
    };
}
