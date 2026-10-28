using System.ComponentModel.DataAnnotations;

namespace OnTech.Store.Contracts;

/// <summary>Stock received from a supplier. This and orders are the only ways quantities change.</summary>
public sealed class CreateStockInRequest
{
    [Range(1, int.MaxValue)] public int SupplierId { get; set; }
    [Required, MinLength(1)] public List<OrderLineRequest> Lines { get; set; } = new();
    /// <summary>The warehouse receipt voucher number (إذن إضافة). Required and unique.</summary>
    [Required, MaxLength(50)] public string VoucherNumber { get; set; } = "";
    /// <summary>The supplier's invoice or delivery note number, if there is one.</summary>
    [MaxLength(50)] public string? SupplierInvoiceNumber { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
}

public sealed class CancelStockInRequest
{
    [Required, MaxLength(500)] public string Reason { get; set; } = "";
}

public sealed class StockInDto
{
    public int Id { get; init; }
    /// <summary>Human-friendly number, e.g. IN-000012.</summary>
    public string StockInNumber { get; init; } = "";
    public int SupplierId { get; init; }
    public string SupplierName { get; init; } = "";
    public string? SupplierTaxNumber { get; init; }
    public string VoucherNumber { get; init; } = "";
    public string? SupplierInvoiceNumber { get; init; }
    public string? Notes { get; init; }
    public OrderStatus Status { get; init; }
    public int TotalQuantity { get; init; }
    public int CreatedByUserId { get; init; }
    public string CreatedByUserName { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
    public string? CancelledByUserName { get; init; }
    public string? CancelReason { get; init; }
    public IReadOnlyList<OrderLineDto> Lines { get; init; } = Array.Empty<OrderLineDto>();
}

/// <summary>Filters for the stock-in log. Every field is optional and they combine with AND.</summary>
public sealed class StockInQuery
{
    /// <summary>Free text: matches stock-in number, voucher number, supplier invoice, supplier name/tax number, item name/code, notes.</summary>
    [MaxLength(100)] public string? Search { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int? SupplierId { get; set; }
    public int? ItemId { get; set; }
    public OrderStatus? Status { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 200)] public int PageSize { get; set; } = 50;
}
