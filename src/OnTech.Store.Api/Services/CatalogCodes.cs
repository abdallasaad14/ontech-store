using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Data;

namespace OnTech.Store.Api.Services;

/// <summary>
/// Codes are digits only. Categories get 01, 02, ...; items get their category's code followed by a three-digit
/// number, e.g. 01007, so every item code is unique across all categories.
/// An item keeps its code if it later moves to another category.
/// </summary>
public static partial class CatalogCodes
{
    public static async Task<string> NextCategoryCodeAsync(StoreDbContext db, CancellationToken ct = default) =>
        CategoryCode(Max(await db.Categories.Where(c => c.Code != null).Select(c => c.Code!).ToListAsync(ct), "") + 1);

    public static async Task<string> NextItemCodeAsync(StoreDbContext db, Category category, CancellationToken ct = default)
    {
        category.Code ??= await NextCategoryCodeAsync(db, ct);
        var prefix = category.Code;
        var taken = await db.Items.Where(i => i.Code != null && i.Code.StartsWith(prefix)).Select(i => i.Code!).ToListAsync(ct);
        var n = Max(taken.Where(c => c.Length == prefix.Length + 3), prefix) + 1;
        while (taken.Contains(ItemCode(prefix, n))) n++;
        return ItemCode(prefix, n);
    }

    public static bool IsValid(string code) => DigitsOnly().IsMatch(code);

    /// <summary>
    /// Turns the old letter codes into digits (C01 to 01, C01-007 to 01007), also on past order lines.
    /// Any other code with letters in it is dropped so <see cref="BackfillAsync"/> gives it a new one. Runs on startup.
    /// </summary>
    public static async Task MakeNumericAsync(StoreDbContext db)
    {
        var categories = await db.Categories.Where(c => c.Code != null).ToListAsync();
        var categoryCodes = categories.Select(c => c.Code!).ToHashSet();
        foreach (var c in categories.Where(c => !IsValid(c.Code!)))
        {
            var m = OldCategoryCode().Match(c.Code!);
            c.Code = m.Success && categoryCodes.Add(m.Groups[1].Value) ? m.Groups[1].Value : null;
        }

        var items = await db.Items.Where(i => i.Code != null).ToListAsync();
        var itemCodes = items.Select(i => i.Code!).ToHashSet();
        var renamed = new Dictionary<int, (string Old, string? New)>();
        foreach (var i in items.Where(i => !IsValid(i.Code!)))
        {
            var m = OldItemCode().Match(i.Code!);
            var code = m.Success ? m.Groups[1].Value + m.Groups[2].Value : null;
            if (code is not null && !itemCodes.Add(code)) code = null;
            renamed[i.Id] = (i.Code!, code);
            i.Code = code;
        }
        await db.SaveChangesAsync();
        if (renamed.Count == 0) return;

        await BackfillAsync(db);
        var newCodes = await db.Items.Where(i => renamed.Keys.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code);
        var lines = await db.OrderLines.Where(l => renamed.Keys.Contains(l.ItemId) && l.ItemCode != null).ToListAsync();
        foreach (var l in lines.Where(l => l.ItemCode == renamed[l.ItemId].Old)) l.ItemCode = newCodes[l.ItemId];
        await db.SaveChangesAsync();
    }

    /// <summary>Gives a code to every category and item that has none (older databases, the opening data). Runs on startup.</summary>
    public static async Task BackfillAsync(StoreDbContext db)
    {
        var categories = await db.Categories.OrderBy(c => c.Id).ToListAsync();
        var nextCategory = Max(categories.Where(c => c.Code != null).Select(c => c.Code!), "") + 1;
        foreach (var c in categories.Where(c => c.Code is null)) c.Code = CategoryCode(nextCategory++);

        var items = await db.Items.Where(i => i.Code == null).OrderBy(i => i.Id).ToListAsync();
        if (items.Count > 0)
        {
            var allCodes = (await db.Items.Where(i => i.Code != null).Select(i => i.Code!).ToListAsync()).ToHashSet();
            foreach (var item in items)
            {
                var category = categories.First(c => c.Id == item.CategoryId);
                var n = Max(allCodes.Where(c => c.Length == category.Code!.Length + 3), category.Code!) + 1;
                while (allCodes.Contains(ItemCode(category.Code!, n))) n++;
                item.Code = ItemCode(category.Code!, n);
                allCodes.Add(item.Code);
            }
        }
        await db.SaveChangesAsync();
    }

    internal static string CategoryCode(int n) => n.ToString("D2", CultureInfo.InvariantCulture);
    internal static string ItemCode(string categoryCode, int n) => categoryCode + n.ToString("D3", CultureInfo.InvariantCulture);

    // Highest number after the prefix among codes like "07" or "01012"; 0 if none.
    internal static int Max(IEnumerable<string> codes, string prefix) =>
        codes.Where(c => c.StartsWith(prefix, StringComparison.Ordinal))
             .Select(c => int.TryParse(c.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
             .DefaultIfEmpty(0).Max();

    [GeneratedRegex(@"^[0-9]+$")] private static partial Regex DigitsOnly();
    [GeneratedRegex(@"^C([0-9]+)$", RegexOptions.IgnoreCase)] private static partial Regex OldCategoryCode();
    [GeneratedRegex(@"^C([0-9]+)-([0-9]+)$", RegexOptions.IgnoreCase)] private static partial Regex OldItemCode();
}
