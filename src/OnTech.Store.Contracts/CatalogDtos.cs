using System.ComponentModel.DataAnnotations;

namespace OnTech.Store.Contracts;

public sealed class CategoryDto
{
    public int Id { get; init; }
    public string? Code { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public int ItemCount { get; init; }
    public int TotalQuantity { get; init; }
}

public sealed class SaveCategoryRequest
{
    /// <summary>Digits only. Leave empty to get the next free code (01, 02, ...) on create, or to keep the current one on edit.</summary>
    [MaxLength(10)] public string? Code { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ItemDto
{
    public int Id { get; init; }
    public int CategoryId { get; init; }
    public string CategoryName { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Code { get; init; }
    public string? Description { get; init; }
    public int Quantity { get; init; }
    public int LowStockThreshold { get; init; }
    public bool IsLowStock { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class CreateItemRequest
{
    [Range(1, int.MaxValue)] public int CategoryId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    /// <summary>Digits only, unique across all items. Leave empty to get the next one (category code + 3 digits, e.g. 01007).</summary>
    [MaxLength(64)] public string? Code { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    /// <summary>Show a warning when stock falls to or below this number (0 = no warning).</summary>
    [Range(0, 1_000_000)] public int LowStockThreshold { get; set; }
}

public sealed class UpdateItemRequest
{
    [Range(1, int.MaxValue)] public int CategoryId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    [MaxLength(64)] public string? Code { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    [Range(0, 1_000_000)] public int LowStockThreshold { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class StockMovementDto
{
    public long Id { get; init; }
    public int ItemId { get; init; }
    public string ItemName { get; init; } = "";
    public string CategoryName { get; init; } = "";
    public StockMovementType Type { get; init; }
    /// <summary>Positive for stock in, negative for stock out.</summary>
    public int Change { get; init; }
    public int QuantityAfter { get; init; }
    public int? OrderId { get; init; }
    public string? OrderNumber { get; init; }
    public int? StockInId { get; init; }
    public string? StockInNumber { get; init; }
    public int UserId { get; init; }
    public string UserName { get; init; } = "";
    public string? Note { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>An item's card (كارت الصنف): every movement in and out, oldest first, with the balance after each one.</summary>
public sealed class ItemCardDto
{
    public ItemDto Item { get; init; } = null!;
    public int TotalIn { get; init; }
    public int TotalOut { get; init; }
    public IReadOnlyList<ItemCardLineDto> Lines { get; init; } = Array.Empty<ItemCardLineDto>();
}

public sealed class ItemCardLineDto
{
    public long Id { get; init; }
    public DateTimeOffset Date { get; init; }
    public StockMovementType Type { get; init; }
    /// <summary>Units that came in (0 when the movement took stock out).</summary>
    public int In { get; init; }
    /// <summary>Units that went out (0 when the movement brought stock in).</summary>
    public int Out { get; init; }
    /// <summary>Quantity in the store right after this movement.</summary>
    public int Balance { get; init; }
    /// <summary>The order or stock-in number, e.g. ORD-000012 or IN-000003.</summary>
    public string? DocumentNumber { get; init; }
    public int? OrderId { get; init; }
    public int? StockInId { get; init; }
    /// <summary>Issue voucher number for an order, receipt voucher number for a stock-in.</summary>
    public string? VoucherNumber { get; init; }
    /// <summary>Customer receipt number for an order, supplier invoice number for a stock-in.</summary>
    public string? InvoiceNumber { get; init; }
    /// <summary>The customer who took the stock or the supplier who sent it.</summary>
    public string? PartyName { get; init; }
    public string UserName { get; init; } = "";
    public string? Note { get; init; }
}

public sealed class StockMovementQuery
{
    public int? ItemId { get; set; }
    public int? CategoryId { get; set; }
    public StockMovementType? Type { get; set; }
    public int? UserId { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 200)] public int PageSize { get; set; } = 50;
}
