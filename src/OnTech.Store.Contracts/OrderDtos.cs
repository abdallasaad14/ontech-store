using System.ComponentModel.DataAnnotations;

namespace OnTech.Store.Contracts;

public sealed class CreateOrderRequest
{
    [Range(1, int.MaxValue)] public int CustomerId { get; set; }
    /// <summary>Sale (the default) or Demo. A demo can't have a receipt.</summary>
    public IssueType Type { get; set; } = IssueType.Sale;
    [Required, MinLength(1)] public List<OrderLineRequest> Lines { get; set; } = new();
    /// <summary>The warehouse issue voucher number (إذن صرف). Required on every order and unique.</summary>
    [Required, MaxLength(30)] public string IssueVoucherNumber { get; set; } = "";
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

/// <summary>Admin only: marks an order saved with the wrong type (e.g. an old demo saved as a sale).</summary>
public sealed class ChangeOrderTypeRequest
{
    public IssueType Type { get; set; }
}

public sealed class CancelOrderRequest
{
    [Required, MaxLength(500)] public string Reason { get; set; } = "";
}

public sealed class OrderDto
{
    public int Id { get; init; }
    /// <summary>Same as <see cref="IssueVoucherNumber"/>: the issue voucher number is the order's number.</summary>
    public string OrderNumber { get; init; } = "";
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public string? CustomerTaxNumber { get; init; }
    public string? CustomerPhoneNumber { get; init; }
    public string? IssueVoucherNumber { get; init; }
    public IssueType Type { get; init; }
    public bool WithReceipt { get; init; }
    public string? ReceiptNumber { get; init; }
    public string? Notes { get; init; }
    public OrderStatus Status { get; init; }
    public int TotalQuantity { get; init; }
    /// <summary>Demo only: units that came back on demo returns.</summary>
    public int ReturnedQuantity { get; init; }
    /// <summary>Demo only: units still with the customer (0 for a sale, and for a cancelled demo).</summary>
    public int OpenQuantity { get; init; }
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
    /// <summary>On a demo line: units that came back. 0 otherwise.</summary>
    public int ReturnedQuantity { get; init; }
    /// <summary>On a demo line: units still with the customer. 0 otherwise.</summary>
    public int OpenQuantity { get; init; }
    /// <summary>On a demo return line: the demo line it closes, and that demo's issue voucher number.</summary>
    public int? DemoOrderLineId { get; init; }
    public int? DemoOrderId { get; init; }
    public string? DemoIssueVoucherNumber { get; init; }
}

/// <summary>A demo line that is still (partly) with the customer. What the demo return screen lists.</summary>
public sealed class OpenDemoLineDto
{
    public int OrderLineId { get; init; }
    public int OrderId { get; init; }
    public string IssueVoucherNumber { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public int CustomerId { get; init; }
    public int? CustomerNumber { get; init; }
    public string CustomerName { get; init; } = "";
    public int ItemId { get; init; }
    public string ItemName { get; init; } = "";
    public string? ItemCode { get; init; }
    public string CategoryName { get; init; } = "";
    public int Quantity { get; init; }
    public int ReturnedQuantity { get; init; }
    public int OpenQuantity { get; init; }
}

public sealed class OpenDemoQuery
{
    public int? CustomerId { get; set; }
    public int? ItemId { get; set; }
    /// <summary>Free text: matches the issue voucher number, customer name or number, item name or code.</summary>
    [MaxLength(100)] public string? Search { get; set; }
}

/// <summary>Where a demo issue voucher stands. Filtering by it also means: demos only, cancelled ones left out.</summary>
public enum DemoState
{
    /// <summary>Something is still with the customer (nothing back yet, or part back).</summary>
    StillOut = 0,
    /// <summary>Nothing has come back yet.</summary>
    NothingBack = 1,
    /// <summary>Some came back, some is still out.</summary>
    PartlyBack = 2,
    /// <summary>Everything came back.</summary>
    AllBack = 3
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
    public IssueType? Type { get; set; }
    /// <summary>Demos only, by how much came back. Leaves out sales and cancelled demos.</summary>
    public DemoState? DemoState { get; set; }
    public OrderSort Sort { get; set; } = OrderSort.NewestFirst;
    /// <summary>1 to <see cref="Paging.MaxPage"/>; anything else is a 400.</summary>
    [Range(1, Paging.MaxPage)] public int Page { get; set; } = 1;
    /// <summary>1 to <see cref="Paging.MaxPageSize"/>.</summary>
    [Range(1, Paging.MaxPageSize)] public int PageSize { get; set; } = 50;
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
    /// <summary>Demo units still with customers.</summary>
    public int OpenDemoQuantity { get; init; }
    /// <summary>How many customers hold at least one demo unit.</summary>
    public int OpenDemoCustomers { get; init; }
    public IReadOnlyList<ItemDto> LowStockItems { get; init; } = Array.Empty<ItemDto>();
}
