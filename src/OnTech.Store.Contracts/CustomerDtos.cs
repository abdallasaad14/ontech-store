using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OnTech.Store.Contracts;

public sealed class CustomerDto
{
    public int Id { get; init; }
    public int? CustomerNumber { get; init; }
    public string CompanyName { get; init; } = "";
    public string? PhoneNumber { get; init; }
    public string? TaxNumber { get; init; }
    public string? ContactPerson { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? Notes { get; init; }
    public bool IsActive { get; init; }
    public int OrderCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>"12 - Star Medical Center", for pickers.</summary>
    [JsonIgnore] public string Display => CustomerNumber is { } n ? $"{n} - {CompanyName}" : CompanyName;
}

public sealed class SaveCustomerRequest
{
    /// <summary>Leave empty on create to get the next free number.</summary>
    [Range(1, int.MaxValue)] public int? CustomerNumber { get; set; }
    [Required, MaxLength(200)] public string CompanyName { get; set; } = "";
    [MaxLength(50)] public string? PhoneNumber { get; set; }
    [MaxLength(50)] public string? TaxNumber { get; set; }
    [MaxLength(150)] public string? ContactPerson { get; set; }
    [EmailAddress, MaxLength(256)] public string? Email { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Bulk-adds customers, e.g. from the old Excel sheet. Rows whose number already exists are skipped, so the same file can be imported twice safely.</summary>
public sealed class ImportCustomersRequest
{
    [Required] public List<ImportCustomerRow> Rows { get; set; } = new();
}

public sealed class ImportCustomerRow
{
    public int? CustomerNumber { get; set; }
    public string? CompanyName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? TaxNumber { get; set; }
}

public sealed class ImportCustomersResult
{
    public int Added { get; init; }
    public int Skipped { get; init; }
    /// <summary>One line per skipped row, e.g. "Row 12: number 7 already exists".</summary>
    public List<string> Messages { get; init; } = new();
}
