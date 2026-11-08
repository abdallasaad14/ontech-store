using System.Net.Http.Json;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Tests;

/// <summary>Shared helpers for the Fix_B_* regression tests.</summary>
internal static class FixB
{
    public static string Tag() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>A signed-in client for a new user holding exactly <paramref name="permissions"/>.</summary>
    public static async Task<HttpClient> UserWithAsync(this StoreApiFactory f, HttpClient admin, Permission permissions)
    {
        var username = $"b{Tag()}";
        await (await admin.Post("/api/users", new CreateUserRequest
        {
            FullName = username, Username = username, Password = "Password1!", Permissions = permissions, MustChangePassword = false,
        })).Read<UserDto>();
        return await f.LoginAsync(username, "Password1!");
    }

    public static async Task<(CategoryDto cat, ItemDto item, CustomerDto customer, SupplierDto supplier)> SeedAsync(HttpClient admin)
    {
        var tag = Tag();
        var cat = await (await admin.Post("/api/categories", new SaveCategoryRequest { Name = $"B cat {tag}" })).Read<CategoryDto>();
        var item = await admin.AddItem(new CreateItemRequest { CategoryId = cat.Id, Name = $"B item {tag}" }, 20);
        var customer = await (await admin.Post("/api/customers", new SaveCustomerRequest { CompanyName = $"B customer {tag}" })).Read<CustomerDto>();
        var supplier = await (await admin.Post("/api/suppliers", new SaveSupplierRequest { CompanyName = $"B supplier {tag}" })).Read<SupplierDto>();
        return (cat, item, customer, supplier);
    }

    public sealed class Problem
    {
        public int Status { get; set; }
        public string? Title { get; set; }
        public string? Code { get; set; }
    }

    public static async Task<Problem> ProblemAsync(this HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<Problem>(StoreApiFactory.Json))!;

    public static bool OnSqlServer => Environment.GetEnvironmentVariable("ONTECH_TEST_SQLSERVER") is not null;
}
