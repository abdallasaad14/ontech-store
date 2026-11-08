using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using OnTech.Store.Client;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;
using Xunit;

// L.Set changes the process culture and some tests count log files: run everything one test at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace OnTech.Store.Desktop.Tests;

/// <summary>
/// A fake server: answers requests by "METHOD path" (path without the query string). Each answer is a function, so a test can
/// delay one answer (to make responses arrive out of order) or count calls. Unknown routes answer 404.
/// </summary>
public sealed class FakeServer : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _routes = new();
    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();
    public ConcurrentQueue<string> Bodies { get; } = new();

    public FakeServer On(string method, string path, Func<HttpRequestMessage, Task<HttpResponseMessage>> answer)
    {
        _routes[method.ToUpperInvariant() + " " + path.TrimStart('/')] = answer;
        return this;
    }

    public FakeServer OnJson(string method, string path, Func<HttpRequestMessage, object?> body) =>
        On(method, path, req => Task.FromResult(Json(HttpStatusCode.OK, body(req))));

    public FakeServer OnJson(string method, string path, object? body) => OnJson(method, path, _ => body);

    public static HttpResponseMessage Json(HttpStatusCode status, object? body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body, StoreApiClient.Json), Encoding.UTF8,
            (int)status >= 400 ? "application/problem+json" : "application/json"),
    };

    public static HttpResponseMessage Problem(HttpStatusCode status, string title, string? code = null) =>
        Json(status, code is null ? new { status = (int)status, title } : new { status = (int)status, title, code });

    public int Count(string method, string path) =>
        Requests.Count(r => r.Method.Method == method && r.RequestUri!.AbsolutePath.TrimStart('/') == path.TrimStart('/'));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Enqueue(request);
        if (request.Content is not null) Bodies.Enqueue(await request.Content.ReadAsStringAsync(ct));
        var key = request.Method.Method + " " + request.RequestUri!.AbsolutePath.TrimStart('/');
        if (!_routes.TryGetValue(key, out var answer))
            return Problem(HttpStatusCode.NotFound, "Not Found");
        var response = await answer(request);
        ct.ThrowIfCancellationRequested();
        return response;
    }
}

public static class TestSession
{
    public static UserDto User(Permission permissions, bool isAdmin = false, bool mustChange = false) => new()
    {
        Id = 7, Username = "tester", FullName = "Test User", Permissions = permissions, IsAdmin = isAdmin, IsActive = true,
        MustChangePassword = mustChange,
    };

    public static StoreApiClient Client(FakeServer server) =>
        new(new HttpClient(server) { BaseAddress = new Uri("http://fake.local/") });

    public static Session Create(FakeServer server, Permission permissions, bool isAdmin = false) =>
        new(Client(server), User(permissions, isAdmin));

    /// <summary>Answers every list the pages load with an empty list, so a page can be opened without a real server.</summary>
    public static FakeServer WithEmptyLists(this FakeServer s) => s
        .OnJson("GET", "api/categories", Array.Empty<CategoryDto>())
        .OnJson("GET", "api/items", Array.Empty<ItemDto>())
        .OnJson("GET", "api/customers", Array.Empty<CustomerDto>())
        .OnJson("GET", "api/suppliers", Array.Empty<SupplierDto>())
        .OnJson("GET", "api/users", Array.Empty<UserDto>())
        .OnJson("GET", "api/dashboard/summary", new DashboardSummaryDto())
        .OnJson("GET", "api/orders", new PagedResult<OrderDto> { Items = [] })
        .OnJson("GET", "api/stock-ins", new PagedResult<StockInDto> { Items = [] });
}

/// <summary>Sets the UI language for one test and puts English back afterwards.</summary>
public sealed class Language : IDisposable
{
    public Language(string code) => L.Set(code);
    public void Dispose() => L.Set(L.English);
}

public static class Wait
{
    public static async Task For(Func<bool> condition, int ms = 3000)
    {
        for (var waited = 0; waited < ms && !condition(); waited += 10) await Task.Delay(10);
        Assert.True(condition(), "condition not met in time");
    }
}
