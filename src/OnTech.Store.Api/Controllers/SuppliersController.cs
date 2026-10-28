using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Auth;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Api.Services;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Controllers;

/// <summary>Companies that stock comes from. Managed the same way as customers.</summary>
[ApiController]
[Route("api/suppliers")]
public class SuppliersController(StoreDbContext db) : ControllerBase
{
    /// <summary>Suppliers for pickers and the suppliers list. Search matches supplier number, company name, phone, tax number or contact.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<SupplierDto>> List([FromQuery] string? search, [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var q = db.Suppliers.AsNoTracking().Where(s => includeInactive || s.IsActive);
        if (search.Clean() is { } text)
        {
            // A number matches that supplier exactly; the name still matches as text.
            var number = int.TryParse(text, out var n) ? n : -1;
            var lower = text.ToLower(); // SQLite compares case-sensitively; SQL Server doesn't.
            q = q.Where(s => s.SupplierNumber == number || s.CompanyName.ToLower().Contains(lower) || (s.PhoneNumber != null && s.PhoneNumber.Contains(text))
                             || (s.TaxNumber != null && s.TaxNumber.Contains(text)) || (s.ContactPerson != null && s.ContactPerson.ToLower().Contains(lower)));
        }
        return await Project(q).OrderBy(s => s.SupplierNumber == null).ThenBy(s => s.SupplierNumber).ThenBy(s => s.CompanyName).ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SupplierDto>> Get(int id, CancellationToken ct) =>
        await Project(db.Suppliers.Where(s => s.Id == id)).FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound("Supplier");

    [HttpPost]
    [HasPermission(Permission.ManageSuppliers)]
    public async Task<ActionResult<SupplierDto>> Create(SaveSupplierRequest req, CancellationToken ct)
    {
        var name = req.CompanyName.Trim();
        if (name.Length == 0) throw ApiException.BadRequest("Company name is required.");
        var s = new Supplier { CreatedAt = DateTime.UtcNow, CreatedByUserId = User.GetUserId() };
        await NumberingGate.RunAsync(async () =>
        {
            await EnsureUnique(name, req.TaxNumber.Clean(), req.SupplierNumber, null, ct);
            Apply(s, req);
            s.SupplierNumber ??= (await db.Suppliers.MaxAsync(x => x.SupplierNumber, ct) ?? 0) + 1;
            db.Suppliers.Add(s);
            return await db.SaveChangesAsync(ct);
        }, ct);
        return CreatedAtAction(nameof(Get), new { id = s.Id }, await Project(db.Suppliers.Where(x => x.Id == s.Id)).FirstAsync(ct));
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permission.ManageSuppliers)]
    public async Task<ActionResult<SupplierDto>> Update(int id, SaveSupplierRequest req, CancellationToken ct)
    {
        var s = await db.Suppliers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiException.NotFound("Supplier");
        await EnsureUnique(req.CompanyName.Trim(), req.TaxNumber.Clean(), req.SupplierNumber, id, ct);
        var oldNumber = s.SupplierNumber;
        Apply(s, req);
        s.SupplierNumber ??= oldNumber;
        await db.SaveChangesAsync(ct);
        return await Project(db.Suppliers.Where(x => x.Id == id)).FirstAsync(ct);
    }

    /// <summary>Disables the supplier; their stock-in vouchers stay in the log.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permission.ManageSuppliers)]
    public async Task<IActionResult> Disable(int id, CancellationToken ct)
    {
        var s = await db.Suppliers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiException.NotFound("Supplier");
        s.IsActive = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(Supplier s, SaveSupplierRequest r)
    {
        s.SupplierNumber = r.SupplierNumber;
        s.CompanyName = r.CompanyName.Trim();
        s.PhoneNumber = r.PhoneNumber.Clean();
        s.TaxNumber = r.TaxNumber.Clean();
        s.ContactPerson = r.ContactPerson.Clean();
        s.Email = r.Email.Clean();
        s.Address = r.Address.Clean();
        s.Notes = r.Notes.Clean();
        s.IsActive = r.IsActive;
    }

    // Stops the same company being added twice from two PCs.
    private async Task EnsureUnique(string name, string? taxNumber, int? number, int? exceptId, CancellationToken ct)
    {
        if (number is not null && await db.Suppliers.AnyAsync(s => s.SupplierNumber == number && s.Id != exceptId, ct))
            throw ApiException.Conflict($"Supplier number {number} is already used.");
        var lower = name.ToLower();
        if (await db.Suppliers.AnyAsync(s => s.CompanyName.ToLower() == lower && s.Id != exceptId, ct))
            throw ApiException.Conflict($"A supplier named '{name}' already exists.");
        if (taxNumber is not null && await db.Suppliers.AnyAsync(s => s.TaxNumber == taxNumber && s.Id != exceptId, ct))
            throw ApiException.Conflict($"A supplier with tax number '{taxNumber}' already exists.");
    }

    private static IQueryable<SupplierDto> Project(IQueryable<Supplier> q) => q.Select(s => new SupplierDto
    {
        Id = s.Id,
        SupplierNumber = s.SupplierNumber,
        CompanyName = s.CompanyName,
        PhoneNumber = s.PhoneNumber,
        TaxNumber = s.TaxNumber,
        ContactPerson = s.ContactPerson,
        Email = s.Email,
        Address = s.Address,
        Notes = s.Notes,
        IsActive = s.IsActive,
        StockInCount = s.StockIns.Count(x => x.Status == OrderStatus.Completed),
        CreatedAt = new DateTimeOffset(s.CreatedAt, TimeSpan.Zero),
    });
}
