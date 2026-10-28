using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using OnTech.Store.Client;
using OnTech.Store.Contracts;

// Simulates the store's PCs using the server at the same time, through the same API client as the desktop app,
// then prints how fast each screen's call was and checks that no stock went missing.
//
//   dotnet run --project tools/OnTech.Store.LoadTest -- --server 192.168.1.10 --user admin --password ... [--pcs 3] [--seconds 60] [--think-ms 300]
//
// Orders and restocks only touch a "Load test" category, items and customer that the tool makes for itself
// (and disables at the end), but they stay in the orders log. Run it on a test copy, not the live store, if you can.

var opt = Options.Parse(args);
if (opt is null) return 2;

var setup = StoreApiClient.Create(opt.Server, TimeSpan.FromSeconds(60));
await setup.LoginAsync(opt.User, opt.Password);
var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
Console.WriteLine($"Server {setup.ServerUrl}, {opt.Pcs} PCs, {opt.Seconds}s, think time {opt.ThinkMs} ms");

var category = await setup.CreateCategoryAsync(new SaveCategoryRequest { Name = $"Load test {stamp}" });
var items = new List<ItemDto>();
for (var i = 1; i <= 5; i++)
    items.Add(await setup.CreateItemAsync(new CreateItemRequest { CategoryId = category.Id, Name = $"Load item {i}", InitialQuantity = 200 }));
var customer = await setup.CreateCustomerAsync(new SaveCustomerRequest { CompanyName = $"Load test customer {stamp}" });
var catalog = await setup.GetCategoriesAsync();

var stats = new ConcurrentDictionary<string, ConcurrentBag<double>>();
var errors = new ConcurrentDictionary<string, int>();
var expected = new ConcurrentDictionary<int, int>(items.ToDictionary(i => i.Id, i => i.Quantity));
var placed = new ConcurrentQueue<OrderDto>();
var shortages = 0;

async Task<T> Time<T>(string name, Func<Task<T>> call)
{
    var sw = Stopwatch.StartNew();
    try { return await call(); }
    finally { stats.GetOrAdd(name, _ => new()).Add(sw.Elapsed.TotalMilliseconds); }
}

// What a store PC does, weighted roughly like a working day: mostly looking things up, then orders and restocks.
async Task Step(StoreApiClient pc, Random rnd)
{
    var roll = rnd.Next(100);
    if (roll < 15) await Time("Home (dashboard)", () => pc.GetDashboardAsync());
    else if (roll < 25) await Time("Stock list (all items)", () => pc.GetItemsAsync());
    else if (roll < 35)
    {
        var cat = catalog[rnd.Next(catalog.Count)];
        await Time("New order: items of a category", () => pc.GetItemsAsync(cat.Id, inStockOnly: true));
    }
    else if (roll < 45) await Time("Customer search (name)", () => pc.GetCustomersAsync(((char)('a' + rnd.Next(26))).ToString()));
    else if (roll < 50) await Time("Customer search (number)", () => pc.GetCustomersAsync(rnd.Next(1, 500).ToString(CultureInfo.InvariantCulture)));
    else if (roll < 55) await Time("Customers list (all)", () => pc.GetCustomersAsync());
    else if (roll < 63) await Time("Orders log page", () => pc.SearchOrdersAsync(new OrderQuery { PageSize = 50 }));
    else if (roll < 67) await Time("Orders log search", () => pc.SearchOrdersAsync(new OrderQuery { Search = "load", WithReceipt = rnd.Next(2) == 0 }));
    else if (roll < 87)
    {
        var item = items[rnd.Next(items.Count)];
        var qty = rnd.Next(1, 6);
        var receipt = rnd.Next(4) == 0 ? $"LT-{stamp}-{Guid.NewGuid():N}"[..40] : null;
        try
        {
            var order = await Time("Save order", () => pc.CreateOrderAsync(new CreateOrderRequest
            {
                CustomerId = customer.Id, WithReceipt = receipt is not null, ReceiptNumber = receipt, Notes = "load test",
                Lines = { new OrderLineRequest { ItemId = item.Id, Quantity = qty } },
            }));
            expected.AddOrUpdate(item.Id, 0, (_, q) => q - qty);
            placed.Enqueue(order);
        }
        catch (ApiError e) when (e.Shortages.Count > 0) { Interlocked.Increment(ref shortages); }
    }
    else if (roll < 95)
    {
        var item = items[rnd.Next(items.Count)];
        var qty = rnd.Next(1, 10);
        await Time("Add quantity", () => pc.AddQuantityAsync(item.Id, qty, "load test"));
        expected.AddOrUpdate(item.Id, 0, (_, q) => q + qty);
    }
    else if (placed.TryDequeue(out var toCancel))
    {
        var cancelled = await Time("Cancel order", () => pc.CancelOrderAsync(toCancel.Id, "load test"));
        foreach (var l in cancelled.Lines) expected.AddOrUpdate(l.ItemId, 0, (_, q) => q + l.Quantity);
    }
}

using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(opt.Seconds));
var wall = Stopwatch.StartNew();
await Task.WhenAll(Enumerable.Range(0, opt.Pcs).Select(async n =>
{
    var pc = StoreApiClient.Create(opt.Server, TimeSpan.FromSeconds(60));
    await Time("Sign in", () => pc.LoginAsync(opt.User, opt.Password));
    var rnd = new Random(n * 7919 + 1);
    while (!stop.IsCancellationRequested)
    {
        try { await Step(pc, rnd); }
        catch (ApiError e) { errors.AddOrUpdate($"{(int?)e.Status} {e.Message.Split('\n')[0]}", 1, (_, c) => c + 1); }
        if (opt.ThinkMs > 0) await Task.Delay(rnd.Next(opt.ThinkMs / 2, opt.ThinkMs * 3 / 2 + 1));
    }
}));
wall.Stop();

// Results
var total = stats.Values.Sum(b => b.Count);
Console.WriteLine();
Console.WriteLine($"{"Call",-32} {"count",7} {"avg ms",8} {"p50",8} {"p95",8} {"p99",8} {"max",8}");
Console.WriteLine(new string('-', 85));
foreach (var (name, bag) in stats.OrderBy(s => s.Key))
{
    var v = bag.OrderBy(x => x).ToArray();
    double P(double p) => v[Math.Min(v.Length - 1, (int)Math.Ceiling(p * v.Length) - 1)];
    Console.WriteLine($"{name,-32} {v.Length,7} {v.Average(),8:F1} {P(.50),8:F1} {P(.95),8:F1} {P(.99),8:F1} {v[^1],8:F1}");
}
Console.WriteLine(new string('-', 85));
Console.WriteLine($"{total} calls in {wall.Elapsed.TotalSeconds:F0}s = {total / wall.Elapsed.TotalSeconds:F1} calls/second");
Console.WriteLine($"Orders refused for not enough stock (expected): {shortages}");
Console.WriteLine(errors.IsEmpty ? "Errors: none" : "Errors:");
foreach (var (msg, count) in errors) Console.WriteLine($"  {count} x {msg}");

// Stock check: every unit taken, added or returned must be accounted for.
var ok = errors.IsEmpty;
foreach (var item in items)
{
    var now = await setup.GetItemAsync(item.Id);
    var match = now.Quantity == expected[item.Id];
    ok &= match;
    Console.WriteLine($"Stock check {item.Code} {item.Name}: server {now.Quantity}, expected {expected[item.Id]} {(match ? "OK" : "MISMATCH")}");
}

// Hide what the run made from the pickers.
await setup.DisableCustomerAsync(customer.Id);
await setup.DeleteCategoryAsync(category.Id);
Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
return ok ? 0 : 1;

sealed record Options(string Server, string User, string Password, int Pcs, int Seconds, int ThinkMs)
{
    public static Options? Parse(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 1 < args.Length; i += 2) map[args[i].TrimStart('-')] = args[i + 1];
        string Get(string k, string d) => map.TryGetValue(k, out var v) ? v : d;
        if (!map.ContainsKey("password"))
        {
            Console.Error.WriteLine("Usage: OnTechStoreLoadTest --server <address> --user <admin user> --password <password> [--pcs 3] [--seconds 60] [--think-ms 300]");
            return null;
        }
        return new Options(Get("server", "localhost"), Get("user", "admin"), map["password"],
            int.Parse(Get("pcs", "3"), CultureInfo.InvariantCulture), int.Parse(Get("seconds", "60"), CultureInfo.InvariantCulture),
            int.Parse(Get("think-ms", "300"), CultureInfo.InvariantCulture));
    }
}
