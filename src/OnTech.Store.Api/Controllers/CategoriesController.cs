using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Auth;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Api.Services;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController(StoreDbContext db) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permission.ViewStock)]
    public async Task<IReadOnlyList<CategoryDto>> List([FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        await Project(db.Categories.Where(c => includeInactive || c.IsActive)).OrderBy(c => c.Code).ThenBy(c => c.Name).ToListAsync(ct);

    [HttpGet("{id:int}")]
    [HasPermission(Permission.ViewStock)]
    public async Task<ActionResult<CategoryDto>> Get(int id, CancellationToken ct) =>
        await Project(db.Categories.Where(c => c.Id == id)).FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound("Category");

    [HttpPost]
    [HasPermission(Permission.ManageCatalog)]
    public async Task<ActionResult<CategoryDto>> Create(SaveCategoryRequest req, CancellationToken ct)
    {
        var name = req.Name.Trim();
        var cat = await NumberingGate.RunAsync(async () =>
        {
            await EnsureNameFree(name, null, ct);
            var code = req.Code.Clean() ?? await CatalogCodes.NextCategoryCodeAsync(db, ct);
            await EnsureCodeFree(code, null, ct);
            var created = new Category { Code = code, Name = name, Description = req.Description?.Trim(), IsActive = req.IsActive, CreatedAt = DateTime.UtcNow };
            db.Categories.Add(created);
            await db.SaveChangesAsync(ct);
            return created;
        }, ct);
        return CreatedAtAction(nameof(Get), new { id = cat.Id }, await Project(db.Categories.Where(c => c.Id == cat.Id)).FirstAsync(ct));
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permission.ManageCatalog)]
    public async Task<ActionResult<CategoryDto>> Update(int id, SaveCategoryRequest req, CancellationToken ct)
    {
        var cat = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw ApiException.NotFound("Category");
        var name = req.Name.Trim();
        await EnsureNameFree(name, id, ct);
        if (req.Code.Clean() is { } code && code != cat.Code)
        {
            await EnsureCodeFree(code, id, ct);
            cat.Code = code; // Items keep the codes they already have.
        }
        cat.Name = name;
        cat.Description = req.Description?.Trim();
        cat.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);
        return await Project(db.Categories.Where(c => c.Id == id)).FirstAsync(ct);
    }

    /// <summary>Deletes an empty category, or disables it if it has items (so order history stays intact).</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permission.ManageCatalog)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var cat = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw ApiException.NotFound("Category");
        if (await db.Items.AnyAsync(i => i.CategoryId == id, ct)) cat.IsActive = false;
        else db.Categories.Remove(cat);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static IQueryable<CategoryDto> Project(IQueryable<Category> q) => q.Select(c => new CategoryDto
    {
        Id = c.Id,
        Code = c.Code,
        Name = c.Name,
        Description = c.Description,
        IsActive = c.IsActive,
        ItemCount = c.Items.Count(i => i.IsActive),
        TotalQuantity = c.Items.Where(i => i.IsActive).Sum(i => (int?)i.Quantity) ?? 0,
    });

    private async Task EnsureCodeFree(string code, int? exceptId, CancellationToken ct)
    {
        if (!CatalogCodes.IsValid(code)) throw ApiException.BadRequest("Codes can only contain digits.");
        if (await db.Categories.AnyAsync(c => c.Code == code && c.Id != exceptId, ct))
            throw ApiException.Conflict($"Category code '{code}' is already used.");
    }

    private async Task EnsureNameFree(string name, int? exceptId, CancellationToken ct)
    {
        var lower = name.ToLower();
        if (await db.Categories.AnyAsync(c => c.Name.ToLower() == lower && c.Id != exceptId, ct))
            throw ApiException.Conflict($"A category named '{name}' already exists.");
    }
}
