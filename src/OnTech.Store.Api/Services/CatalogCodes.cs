using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Data;

namespace OnTech.Store.Api.Services;

/// <summary>
/// Generated codes: categories get C01, C02, ...; items get their category's code plus a number, e.g. C01-007.
/// An item keeps its code if it later moves to another category.
/// </summary>
public static class CatalogCodes
{
    public static async Task<string> NextCategoryCodeAsync(StoreDbContext db, CancellationToken ct = default) =>
        CategoryCode(Max(await db.Categories.Where(c => c.Code != null).Select(c => c.Code!).ToListAsync(ct), "C") + 1);

    public static async Task<string> NextItemCodeAsync(StoreDbContext db, Category category, CancellationToken ct = default)
    {
        category.Code ??= await NextCategoryCodeAsync(db, ct);
        var prefix = category.Code + "-";
        var taken = await db.Items.Where(i => i.Code != null && i.Code.StartsWith(prefix)).Select(i => i.Code!).ToListAsync(ct);
        return ItemCode(category.Code!, Max(taken, prefix) + 1);
    }

    /// <summary>Gives a code to every category and item that has none (older databases, the opening data). Runs on startup.</summary>
    public static async Task BackfillAsync(StoreDbContext db)
    {
        var categories = await db.Categories.OrderBy(c => c.Id).ToListAsync();
        var nextCategory = Max(categories.Where(c => c.Code != null).Select(c => c.Code!), "C") + 1;
        foreach (var c in categories.Where(c => c.Code is null)) c.Code = CategoryCode(nextCategory++);

        var items = await db.Items.Where(i => i.Code == null).OrderBy(i => i.Id).ToListAsync();
        if (items.Count > 0)
        {
            var allCodes = await db.Items.Where(i => i.Code != null).Select(i => i.Code!).ToListAsync();
            var next = new Dictionary<int, int>();
            foreach (var item in items)
            {
                var category = categories.First(c => c.Id == item.CategoryId);
                if (!next.TryGetValue(category.Id, out var n)) n = Max(allCodes, category.Code + "-") + 1;
                item.Code = ItemCode(category.Code!, n);
                next[category.Id] = n + 1;
            }
        }
        await db.SaveChangesAsync();
    }

    internal static string CategoryCode(int n) => $"C{n:D2}";
    internal static string ItemCode(string categoryCode, int n) => $"{categoryCode}-{n:D3}";

    // Highest number after the prefix among codes like "C07" or "C01-012"; 0 if none.
    internal static int Max(IEnumerable<string> codes, string prefix) =>
        codes.Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
             .Select(c => int.TryParse(c.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
             .DefaultIfEmpty(0).Max();
}
