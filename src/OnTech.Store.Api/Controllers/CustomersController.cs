using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Auth;
using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Api.Services;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController(StoreDbContext db) : ControllerBase
{
    /// <summary>Customers for pickers and the customers list. Search matches customer number, company name, phone, tax number or contact.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<CustomerDto>> List([FromQuery] string? search, [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var q = db.Customers.AsNoTracking().Where(c => includeInactive || c.IsActive);
        if (search.Clean() is { } s)
        {
            // A number matches that customer exactly (not 1 -> 10, 11, 100...); the name still matches as text.
            var number = int.TryParse(s, out var n) ? n : -1;
            var lower = s.ToLower(); // SQLite compares case-sensitively; SQL Server doesn't.
            q = q.Where(c => c.CustomerNumber == number || c.CompanyName.ToLower().Contains(lower) || (c.PhoneNumber != null && c.PhoneNumber.Contains(s))
                             || (c.TaxNumber != null && c.TaxNumber.Contains(s)) || (c.ContactPerson != null && c.ContactPerson.ToLower().Contains(lower)));
        }
        // Customers without a number (none after the import) go last.
        return await Project(q).OrderBy(c => c.CustomerNumber == null).ThenBy(c => c.CustomerNumber).ThenBy(c => c.CompanyName).ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerDto>> Get(int id, CancellationToken ct) =>
        await Project(db.Customers.Where(c => c.Id == id)).FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound("Customer");

    [HttpPost]
    [HasPermission(Permission.ManageCustomers)]
    public async Task<ActionResult<CustomerDto>> Create(SaveCustomerRequest req, CancellationToken ct)
    {
        var name = req.CompanyName.Trim();
        if (name.Length == 0) throw ApiException.BadRequest("Company name is required.");
        var c = new Customer { CreatedAt = DateTime.UtcNow, CreatedByUserId = User.GetUserId() };
        await NumberingGate.RunAsync(async () =>
        {
            await EnsureUnique(name, req.TaxNumber.Clean(), req.CustomerNumber, null, ct);
            Apply(c, req);
            c.CustomerNumber ??= await NextNumberAsync(ct);
            db.Customers.Add(c);
            return await db.SaveChangesAsync(ct);
        }, ct);
        return CreatedAtAction(nameof(Get), new { id = c.Id }, await Project(db.Customers.Where(x => x.Id == c.Id)).FirstAsync(ct));
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permission.ManageCustomers)]
    public async Task<ActionResult<CustomerDto>> Update(int id, SaveCustomerRequest req, CancellationToken ct)
    {
        var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiException.NotFound("Customer");
        await EnsureUnique(req.CompanyName.Trim(), req.TaxNumber.Clean(), req.CustomerNumber, id, ct);
        var oldNumber = c.CustomerNumber;
        Apply(c, req);
        c.CustomerNumber ??= oldNumber;
        await db.SaveChangesAsync(ct);
        return await Project(db.Customers.Where(x => x.Id == id)).FirstAsync(ct);
    }

    /// <summary>
    /// Adds many customers at once (the Import button on the Customers page). Rows with no company name,
    /// or whose number is already taken, are skipped and reported; everything else is saved in one go.
    /// </summary>
    [HttpPost("import")]
    [HasPermission(Permission.ManageCustomers)]
    public Task<ImportCustomersResult> Import(ImportCustomersRequest req, CancellationToken ct) =>
        NumberingGate.RunAsync(() => ImportRows(req, ct), ct);

    private async Task<ImportCustomersResult> ImportRows(ImportCustomersRequest req, CancellationToken ct)
    {
        var usedNumbers = (await db.Customers.Where(c => c.CustomerNumber != null).Select(c => c.CustomerNumber!.Value).ToListAsync(ct)).ToHashSet();
        var next = usedNumbers.Count == 0 ? 1 : usedNumbers.Max() + 1;
        // A row without a number is skipped when its name is already in (or earlier in the file), so a sheet
        // without numbers can be imported twice safely. Numbered rows are matched by number only.
        var usedNames = (await db.Customers.Select(c => c.CompanyName).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var userId = User.GetUserId();
        var now = DateTime.UtcNow;
        var messages = new List<string>();
        var added = new List<Customer>();

        // Numbered rows first, so a row without a number can't grab a number used later in the file.
        foreach (var number in req.Rows.Select(r => r.CustomerNumber).OfType<int>().Where(n => n > 0 && !usedNumbers.Contains(n)))
            next = Math.Max(next, number + 1);

        for (var i = 0; i < req.Rows.Count; i++)
        {
            var r = req.Rows[i];
            var label = $"Row {i + 1}";
            var name = r.CompanyName.Clean() is { } raw ? Regex.Replace(raw, @"\s{2,}", " ") : null;
            if (name is null) { messages.Add($"{label}: no company name"); continue; }
            if (name.Length > 200) { messages.Add($"{label}: company name is longer than 200 characters"); continue; }
            if (r.CustomerNumber is <= 0) { messages.Add($"{label} ({name}): number must be 1 or more"); continue; }
            if (r.CustomerNumber is { } n && usedNumbers.Contains(n)) { messages.Add($"{label} ({name}): number {n} already exists"); continue; }
            if (!usedNames.Add(name) && r.CustomerNumber is null) { messages.Add($"{label}: '{name}' already exists"); continue; }

            var number = r.CustomerNumber ?? next++;
            usedNumbers.Add(number);
            added.Add(new Customer
            {
                CustomerNumber = number,
                CompanyName = name,
                PhoneNumber = Truncate(r.PhoneNumber.Clean(), 50),
                TaxNumber = Truncate(r.TaxNumber.Clean(), 50),
                CreatedAt = now,
                CreatedByUserId = userId,
            });
        }

        db.Customers.AddRange(added);
        await db.SaveChangesAsync(ct);
        return new ImportCustomersResult { Added = added.Count, Skipped = req.Rows.Count - added.Count, Messages = messages };
    }

    /// <summary>Disables the customer; their orders stay in the log.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permission.ManageCustomers)]
    public async Task<IActionResult> Disable(int id, CancellationToken ct)
    {
        var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiException.NotFound("Customer");
        c.IsActive = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(Customer c, SaveCustomerRequest r)
    {
        c.CustomerNumber = r.CustomerNumber;
        c.CompanyName = r.CompanyName.Trim();
        c.PhoneNumber = r.PhoneNumber.Clean();
        c.TaxNumber = r.TaxNumber.Clean();
        c.ContactPerson = r.ContactPerson.Clean();
        c.Email = r.Email.Clean();
        c.Address = r.Address.Clean();
        c.Notes = r.Notes.Clean();
        c.IsActive = r.IsActive;
    }

    // Stops the same company being added twice from two PCs.
    private async Task EnsureUnique(string name, string? taxNumber, int? number, int? exceptId, CancellationToken ct)
    {
        if (number is not null && await db.Customers.AnyAsync(c => c.CustomerNumber == number && c.Id != exceptId, ct))
            throw ApiException.Conflict($"Customer number {number} is already used.");
        var lower = name.ToLower();
        if (await db.Customers.AnyAsync(c => c.CompanyName.ToLower() == lower && c.Id != exceptId, ct))
            throw ApiException.Conflict($"A customer named '{name}' already exists.");
        if (taxNumber is not null && await db.Customers.AnyAsync(c => c.TaxNumber == taxNumber && c.Id != exceptId, ct))
            throw ApiException.Conflict($"A customer with tax number '{taxNumber}' already exists.");
    }

    private async Task<int> NextNumberAsync(CancellationToken ct) => (await db.Customers.MaxAsync(c => c.CustomerNumber, ct) ?? 0) + 1;

    private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..max];

    private static IQueryable<CustomerDto> Project(IQueryable<Customer> q) => q.Select(c => new CustomerDto
    {
        Id = c.Id,
        CustomerNumber = c.CustomerNumber,
        CompanyName = c.CompanyName,
        PhoneNumber = c.PhoneNumber,
        TaxNumber = c.TaxNumber,
        ContactPerson = c.ContactPerson,
        Email = c.Email,
        Address = c.Address,
        Notes = c.Notes,
        IsActive = c.IsActive,
        OrderCount = c.Orders.Count(o => o.Status == OrderStatus.Completed),
        CreatedAt = new DateTimeOffset(c.CreatedAt, TimeSpan.Zero),
    });
}
