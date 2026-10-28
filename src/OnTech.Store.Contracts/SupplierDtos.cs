using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OnTech.Store.Contracts;

public sealed class SupplierDto
{
    public int Id { get; init; }
    public int? SupplierNumber { get; init; }
    public string CompanyName { get; init; } = "";
    public string? PhoneNumber { get; init; }
    public string? TaxNumber { get; init; }
    public string? ContactPerson { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? Notes { get; init; }
    public bool IsActive { get; init; }
    /// <summary>Stock-in vouchers from this supplier (not counting cancelled ones).</summary>
    public int StockInCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>"3 - Zebra Egypt", for pickers.</summary>
    [JsonIgnore] public string Display => SupplierNumber is { } n ? $"{n} - {CompanyName}" : CompanyName;
}

public sealed class SaveSupplierRequest
{
    /// <summary>Leave empty on create to get the next free number.</summary>
    [Range(1, int.MaxValue)] public int? SupplierNumber { get; set; }
    [Required, MaxLength(200)] public string CompanyName { get; set; } = "";
    [MaxLength(50)] public string? PhoneNumber { get; set; }
    [MaxLength(50)] public string? TaxNumber { get; set; }
    [MaxLength(150)] public string? ContactPerson { get; set; }
    [EmailAddress, MaxLength(256)] public string? Email { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}
