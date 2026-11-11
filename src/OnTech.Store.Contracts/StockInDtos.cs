using System.ComponentModel.DataAnnotations;

namespace OnTech.Store.Contracts;

/// <summary>
/// Stock received: bought from a supplier (Purchase) or a demo coming back from a customer (DemoReturn).
/// This and orders are the only ways quantities change.
/// </summary>
public sealed class CreateStockInRequest
{
    /// <summary>Purchase (the default) or DemoReturn.</summary>
    public ReceiveType Type { get; set; } = ReceiveType.Purchase;
    /// <summary>Required for a purchase; must be empty for a demo return.</summary>
    public int? SupplierId { get; set; }
    /// <summary>Required for a demo return: the customer the demo went out to. Must be empty for a purchase.</summary>
    public int? CustomerId { get; set; }
    [Required, MinLength(1)] public List<StockInLineRequest> Lines { get; set; } = new();
    /// <summary>The warehouse receipt voucher number (إذن إضافة). Required and unique.</summary>
    [Required, MaxLength(50)] public string VoucherNumber { get; set; } = "";
    /// <summary>The supplier's invoice or delivery note number, if there is one.</summary>
    [MaxLength(50)] public string? SupplierInvoiceNumber { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
}

public sealed class StockInLineRequest
{
    [Range(1, int.MaxValue)] public int ItemId { get; set; }
    [Range(1, 1_000_000)] public int Quantity { get; set; }
    /// <summary>Demo return only (required there): the demo line this quantity comes back on.</summary>
    public int? DemoOrderLineId { get; set; }
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
    public ReceiveType Type { get; init; }
    /// <summary>Set on a purchase.</summary>
    public int? SupplierId { get; init; }
    public string SupplierName { get; init; } = "";
    public string? SupplierTaxNumber { get; init; }
    /// <summary>Set on a demo return.</summary>
    public int? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    /// <summary>The supplier on a purchase, the customer on a demo return.</summary>
    public string PartyName { get; init; } = "";
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
    public int? CustomerId { get; set; }
    public int? ItemId { get; set; }
    public OrderStatus? Status { get; set; }
    public ReceiveType? Type { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 200)] public int PageSize { get; set; } = 50;
}
