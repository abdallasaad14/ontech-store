using System.ComponentModel.DataAnnotations;

namespace OnTech.Store.Contracts;

public sealed class CreateOrderRequest
{
    [Range(1, int.MaxValue)] public int CustomerId { get; set; }
    [Required, MinLength(1)] public List<OrderLineRequest> Lines { get; set; } = new();
    /// <summary>The warehouse issue voucher number (إذن صرف). Required on every order and unique.</summary>
    [Required, MaxLength(50)] public string IssueVoucherNumber { get; set; } = "";
    public bool WithReceipt { get; set; }
    /// <summary>Required when <see cref="WithReceipt"/> is true; must be unique.</summary>
    [MaxLength(50)] public string? ReceiptNumber { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
}

public sealed class OrderLineRequest
{
    [Range(1, int.MaxValue)] public int ItemId { get; set; }
    [Range(1, 1_000_000)] public int Quantity { get; set; }
}

public sealed class CancelOrderRequest
{
    [Required, MaxLength(500)] public string Reason { get; set; } = "";
}

public sealed class OrderDto
{
    public int Id { get; init; }
    public string OrderNumber { get; init; } = "";
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public string? CustomerTaxNumber { get; init; }
    public string? CustomerPhoneNumber { get; init; }
    /// <summary>Empty only for orders saved before the voucher number was required.</summary>
    public string? IssueVoucherNumber { get; init; }
    public bool WithReceipt { get; init; }
    public string? ReceiptNumber { get; init; }
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

public sealed class OrderLineDto
{
    public int Id { get; init; }
    public int ItemId { get; init; }
    public string ItemName { get; init; } = "";
    public string? ItemCode { get; init; }
    public int CategoryId { get; init; }
    public string CategoryName { get; init; } = "";
    public int Quantity { get; init; }
}

public enum OrderSort
{
    NewestFirst = 0,
    OldestFirst = 1,
    CustomerName = 2,
    ReceiptNumber = 3,
    TotalQuantity = 4
}

/// <summary>Filters for the orders log. Every field is optional and they combine with AND.</summary>
public sealed class OrderQuery
{
    /// <summary>Free text: matches order number, issue voucher number, receipt number, customer name/tax number, item name/code, notes.</summary>
    [MaxLength(100)] public string? Search { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int? CustomerId { get; set; }
    public int? CategoryId { get; set; }
    public int? ItemId { get; set; }
    public int? CreatedByUserId { get; set; }
    public bool? WithReceipt { get; set; }
    /// <summary>Partial match on the receipt number.</summary>
    [MaxLength(50)] public string? ReceiptNumber { get; set; }
    [MaxLength(30)] public string? OrderNumber { get; set; }
    /// <summary>Partial match on the issue voucher number.</summary>
    [MaxLength(50)] public string? IssueVoucherNumber { get; set; }
    public OrderStatus? Status { get; set; }
    public OrderSort Sort { get; set; } = OrderSort.NewestFirst;
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 200)] public int PageSize { get; set; } = 50;
}

public sealed class DashboardSummaryDto
{
    public int CategoryCount { get; init; }
    public int ItemCount { get; init; }
    public int TotalUnitsInStock { get; init; }
    public int LowStockItemCount { get; init; }
    public int OutOfStockItemCount { get; init; }
    public int CustomerCount { get; init; }
    public int OrdersToday { get; init; }
    public int UnitsOutToday { get; init; }
    public int OrdersThisMonth { get; init; }
    public IReadOnlyList<ItemDto> LowStockItems { get; init; } = Array.Empty<ItemDto>();
}
