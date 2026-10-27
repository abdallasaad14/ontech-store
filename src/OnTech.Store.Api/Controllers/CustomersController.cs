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
    /// <summary>Customers for pickers and the customers list. Search matches company name, phone, tax number or contact.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<CustomerDto>> List([FromQuery] string? search, [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var q = db.Customers.AsNoTracking().Where(c => includeInactive || c.IsActive);
        if (search.Clean() is { } s)
            q = q.Where(c => c.CompanyName.Contains(s) || (c.PhoneNumber != null && c.PhoneNumber.Contains(s))
                             || (c.TaxNumber != null && c.TaxNumber.Contains(s)) || (c.ContactPerson != null && c.ContactPerson.Contains(s)));
        return await Project(q).OrderBy(c => c.CompanyName).ToListAsync(ct);
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
        await EnsureUnique(name, req.TaxNumber.Clean(), null, ct);
        var c = new Customer { CreatedAt = DateTime.UtcNow, CreatedByUserId = User.GetUserId() };
        Apply(c, req);
        db.Customers.Add(c);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = c.Id }, await Project(db.Customers.Where(x => x.Id == c.Id)).FirstAsync(ct));
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permission.ManageCustomers)]
    public async Task<ActionResult<CustomerDto>> Update(int id, SaveCustomerRequest req, CancellationToken ct)
    {
        var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw ApiException.NotFound("Customer");
        await EnsureUnique(req.CompanyName.Trim(), req.TaxNumber.Clean(), id, ct);
        Apply(c, req);
        await db.SaveChangesAsync(ct);
        return await Project(db.Customers.Where(x => x.Id == id)).FirstAsync(ct);
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
    private async Task EnsureUnique(string name, string? taxNumber, int? exceptId, CancellationToken ct)
    {
        var lower = name.ToLower();
        if (await db.Customers.AnyAsync(c => c.CompanyName.ToLower() == lower && c.Id != exceptId, ct))
            throw ApiException.Conflict($"A customer named '{name}' already exists.");
        if (taxNumber is not null && await db.Customers.AnyAsync(c => c.TaxNumber == taxNumber && c.Id != exceptId, ct))
            throw ApiException.Conflict($"A customer with tax number '{taxNumber}' already exists.");
    }

    private static IQueryable<CustomerDto> Project(IQueryable<Customer> q) => q.Select(c => new CustomerDto
    {
        Id = c.Id,
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
