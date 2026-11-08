using System.Net;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Contracts;
using Xunit;

namespace OnTech.Store.Api.Tests;

/// <summary>M-8: a number or code that is already used is a 409 Duplicate with a readable message, never a 500, also in a race.</summary>
public class Fix_B_DuplicateTests : IClassFixture<StoreApiFactory>
{
    private readonly StoreApiFactory _f;
    public Fix_B_DuplicateTests(StoreApiFactory f) => _f = f;

    private static async Task AssertDuplicate(HttpResponseMessage res, string? title = null)
    {
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var p = await res.ProblemAsync();
        Assert.Equal(ErrorCodes.Duplicate, p.Code);
        if (title is not null) Assert.Equal(title, p.Title);
    }

    private static int FreeNumber() => Random.Shared.Next(100_000, 900_000);

    [Fact]
    public async Task Parallel_customer_number_changes_end_in_one_ok_and_409s_not_500()
    {
        var admin = await _f.LoginAsync();
        var customers = new List<CustomerDto>();
        for (var i = 0; i < 6; i++)
            customers.Add(await (await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = $"Num race {FixB.Tag()}" })).Read<CustomerDto>());
        var number = FreeNumber();
        var clients = await Task.WhenAll(customers.Select(_ => _f.LoginAsync()));

        var results = await Task.WhenAll(customers.Select((c, i) =>
            clients[i].Put($"/api/customers/{c.Id}", new SaveCustomerRequest { CompanyName = c.CompanyName, CustomerNumber = number })));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        foreach (var r in results.Where(r => r.StatusCode != HttpStatusCode.OK))
            await AssertDuplicate(r, $"Customer number {number} is already used.");
    }

    [Fact]
    public async Task Parallel_supplier_number_changes_end_in_one_ok_and_409s_not_500()
    {
        var admin = await _f.LoginAsync();
        var suppliers = new List<SupplierDto>();
        for (var i = 0; i < 6; i++)
            suppliers.Add(await (await admin.Post("/api/suppliers", new SaveSupplierRequest { CompanyName = $"Sup race {FixB.Tag()}" })).Read<SupplierDto>());
        var number = FreeNumber();
        var clients = await Task.WhenAll(suppliers.Select(_ => _f.LoginAsync()));

        var results = await Task.WhenAll(suppliers.Select((s, i) =>
            clients[i].Put($"/api/suppliers/{s.Id}", new SaveSupplierRequest { CompanyName = s.CompanyName, SupplierNumber = number })));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        foreach (var r in results.Where(r => r.StatusCode != HttpStatusCode.OK))
            await AssertDuplicate(r, $"Supplier number {number} is already used.");
    }

    [Fact]
    public async Task Parallel_category_and_item_codes_never_give_500()
    {
        var admin = await _f.LoginAsync();
        var clients = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _f.LoginAsync()));
        var code = (FreeNumber() % 90 + 500).ToString();
        var cats = await Task.WhenAll(clients.Select(cl => cl.Post("/api/categories", new SaveCategoryRequest { Name = $"Code race {FixB.Tag()}", Code = code })));
        Assert.Single(cats, r => r.StatusCode == HttpStatusCode.Created);
        foreach (var r in cats.Where(r => r.StatusCode != HttpStatusCode.Created)) await AssertDuplicate(r, $"Category code '{code}' is already used.");

        var cat = await cats.Single(r => r.StatusCode == HttpStatusCode.Created).Read<CategoryDto>();
        var itemCode = code + "777";
        var items = await Task.WhenAll(clients.Select(cl => cl.Post("/api/items", new CreateItemRequest { CategoryId = cat.Id, Name = $"Item {FixB.Tag()}", Code = itemCode })));
        Assert.Single(items, r => r.StatusCode == HttpStatusCode.Created);
        foreach (var r in items.Where(r => r.StatusCode != HttpStatusCode.Created)) await AssertDuplicate(r, $"Item code '{itemCode}' is already used.");
    }

    [Fact]
    public async Task Plain_duplicates_carry_the_duplicate_code()
    {
        var admin = await _f.LoginAsync();
        var (cat, item, customer, supplier) = await FixB.SeedAsync(admin);
        await AssertDuplicate(await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = "x" + FixB.Tag(), CustomerNumber = customer.CustomerNumber }),
            $"Customer number {customer.CustomerNumber} is already used.");
        await AssertDuplicate(await admin.Post("/api/suppliers", new SaveSupplierRequest { CompanyName = "x" + FixB.Tag(), SupplierNumber = supplier.SupplierNumber }),
            $"Supplier number {supplier.SupplierNumber} is already used.");
        await AssertDuplicate(await admin.Post("/api/categories", new SaveCategoryRequest { Name = cat.Name }));
        await AssertDuplicate(await admin.Post("/api/items", new CreateItemRequest { CategoryId = cat.Id, Name = "y" + FixB.Tag(), Code = item.Code }),
            $"Item code '{item.Code}' is already used.");
        await AssertDuplicate(await admin.Post("/api/users", new CreateUserRequest { FullName = "a", Username = StoreApiFactory.AdminUsername.ToUpperInvariant(), Password = "Password1!" }));
    }

    [Fact]
    public void Unique_index_violations_are_recognised_on_this_provider()
    {
        var number = FreeNumber();
        var ex = Assert.ThrowsAny<DbUpdateException>(() => _f.WithDb(db =>
        {
            var userId = db.Users.Select(u => u.Id).First();
            db.Customers.Add(new Customer { CompanyName = "u1" + FixB.Tag(), CustomerNumber = number, CreatedByUserId = userId, CreatedAt = DateTime.UtcNow });
            db.Customers.Add(new Customer { CompanyName = "u2" + FixB.Tag(), CustomerNumber = number, CreatedByUserId = userId, CreatedAt = DateTime.UtcNow });
            return db.SaveChanges();
        }));
        Assert.True(ex.IsUniqueViolation());
    }
}
