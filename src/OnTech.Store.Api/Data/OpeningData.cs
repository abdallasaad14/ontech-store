using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Services;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Data;

/// <summary>
/// OnTech's stock and customers as they were when the app went live (Seed/opening-data.json, built from the
/// old Excel sheets). Loaded once on startup: categories, items and customers that already exist by name
/// (or, for customers, by number or name) are left alone, and the load is recorded in AppliedSeeds so it never runs again.
/// </summary>
public static class OpeningData
{
    private sealed record File(string Name, List<SeedCategory> Categories, List<SeedCustomer> Customers);
    private sealed record SeedCategory(string Name, List<SeedItem> Items);
    private sealed record SeedItem(string Name, int Quantity);
    private sealed record SeedCustomer(int Number, string CompanyName);

    public static async Task ApplyAsync(StoreDbContext db, ILogger log)
    {
        await using var stream = typeof(OpeningData).Assembly.GetManifestResourceStream("OnTech.Store.Api.Data.Seed.opening-data.json")
            ?? throw new InvalidOperationException("Seed/opening-data.json is not embedded.");
        var data = await JsonSerializer.DeserializeAsync<File>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Seed/opening-data.json is empty.");
        if (await db.AppliedSeeds.AnyAsync(s => s.Name == data.Name)) return;

        var now = DateTime.UtcNow;
        var adminId = await db.Users.Where(u => u.IsAdmin).OrderBy(u => u.Id).Select(u => u.Id).FirstAsync();
        var categories = await db.Categories.Include(c => c.Items).ToListAsync();
        int newItems = 0, newCustomers = 0;
        // Codes follow the sheet's order (Data card = 01, its first item 01001), so they are set here, not by insert order.
        var nextCategory = CatalogCodes.Max(categories.Where(c => c.Code != null).Select(c => c.Code!), "") + 1;

        foreach (var sc in data.Categories)
        {
            var category = categories.FirstOrDefault(c => c.Name.Equals(sc.Name, StringComparison.OrdinalIgnoreCase));
            if (category is null)
            {
                category = new Category { Code = CatalogCodes.CategoryCode(nextCategory++), Name = sc.Name, CreatedAt = now };
                db.Categories.Add(category);
                categories.Add(category);
            }
            category.Code ??= CatalogCodes.CategoryCode(nextCategory++);
            var nextItem = CatalogCodes.Max((await db.Items.Where(i => i.Code != null).Select(i => i.Code!).ToListAsync())
                .Where(c => c.Length == category.Code.Length + 3), category.Code) + 1;
            foreach (var si in sc.Items)
            {
                if (category.Items.Any(i => i.Name.Equals(si.Name, StringComparison.OrdinalIgnoreCase))) continue;
                var item = new Item { Category = category, Code = CatalogCodes.ItemCode(category.Code, nextItem++), Name = si.Name, Quantity = si.Quantity, CreatedAt = now, UpdatedAt = now };
                category.Items.Add(item);
                if (item.Quantity > 0)
                    db.StockMovements.Add(new StockMovement
                    {
                        Item = item, Type = StockMovementType.Initial, Change = item.Quantity, QuantityAfter = item.Quantity,
                        UserId = adminId, Note = "Opening stock from the old Excel sheet", CreatedAt = now,
                    });
                newItems++;
            }
        }

        // Customers may already be in (e.g. from Import from Excel): skip any whose number or name is already there.
        var existing = await db.Customers.Select(c => new { c.CustomerNumber, c.CompanyName }).ToListAsync();
        var usedNumbers = existing.Where(c => c.CustomerNumber != null).Select(c => c.CustomerNumber!.Value).ToHashSet();
        var usedNames = existing.Select(c => c.CompanyName.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var sc in data.Customers.Where(c => !usedNames.Contains(c.CompanyName) && usedNumbers.Add(c.Number)))
        {
            db.Customers.Add(new Customer { CustomerNumber = sc.Number, CompanyName = sc.CompanyName, CreatedAt = now, CreatedByUserId = adminId });
            newCustomers++;
        }

        db.AppliedSeeds.Add(new AppliedSeed { Name = data.Name, AppliedAt = now });
        await db.SaveChangesAsync();
        log.LogInformation("Loaded opening data '{Name}': {Items} items, {Customers} customers.", data.Name, newItems, newCustomers);
    }
}
