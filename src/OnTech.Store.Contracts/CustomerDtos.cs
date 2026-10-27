using System.ComponentModel.DataAnnotations;

namespace OnTech.Store.Contracts;

public sealed class CustomerDto
{
    public int Id { get; init; }
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
}

public sealed class SaveCustomerRequest
{
    [Required, MaxLength(200)] public string CompanyName { get; set; } = "";
    [MaxLength(50)] public string? PhoneNumber { get; set; }
    [MaxLength(50)] public string? TaxNumber { get; set; }
    [MaxLength(150)] public string? ContactPerson { get; set; }
    [EmailAddress, MaxLength(256)] public string? Email { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}
