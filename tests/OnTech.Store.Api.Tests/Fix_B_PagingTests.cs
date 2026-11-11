using System.Net;
using OnTech.Store.Contracts;
using Xunit;

namespace OnTech.Store.Api.Tests;

/// <summary>L-1: out-of-range paging and dashboard offsets are a 400, never a 500 or wrong rows (CONTRACTS.md §8).</summary>
public class Fix_B_PagingTests : IClassFixture<StoreApiFactory>
{
    private readonly StoreApiFactory _f;
    public Fix_B_PagingTests(StoreApiFactory f) => _f = f;

    public static TheoryData<string> PagedLists => new() { "/api/orders", "/api/stock-ins", "/api/stock-movements" };

    [Theory]
    [MemberData(nameof(PagedLists))]
    public async Task Out_of_range_paging_is_400(string url)
    {
        var admin = await _f.LoginAsync();
        foreach (var q in new[] { "page=2147483647", "page=1000001", "page=0", "page=-1", "pageSize=0", "pageSize=201", "pageSize=2147483647", "page=2147483647&pageSize=200" })
        {
            var res = await admin.GetAsync($"{url}?{q}");
            Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{url}?{q}: {res.StatusCode}");
        }
    }

    [Theory]
    [MemberData(nameof(PagedLists))]
    public async Task Highest_allowed_page_is_an_empty_page_not_an_error(string url)
    {
        var admin = await _f.LoginAsync();
        await FixB.SeedAsync(admin); // at least one stock-in and movement
        var res = await admin.GetAsync($"{url}?page={Paging.MaxPage}&pageSize={Paging.MaxPageSize}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"items\":[]", body);
    }

    [Fact]
    public void Skip_never_overflows()
    {
        Assert.Equal(0, Paging.Skip(1, 50));
        Assert.Equal(50, Paging.Skip(2, 50));
        Assert.Equal(int.MaxValue, Paging.Skip(int.MaxValue, int.MaxValue));
        Assert.Equal((Paging.MaxPage - 1) * Paging.MaxPageSize, Paging.Skip(Paging.MaxPage, Paging.MaxPageSize));
    }

    [Fact]
    public async Task Dashboard_offset_is_limited_to_14_hours()
    {
        var admin = await _f.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/dashboard/summary?utcOffsetMinutes=840")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/dashboard/summary?utcOffsetMinutes=-840")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/dashboard/summary?utcOffsetMinutes=841")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/dashboard/summary?utcOffsetMinutes=-841")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/dashboard/summary?utcOffsetMinutes=2147483647")).StatusCode);
    }
}
