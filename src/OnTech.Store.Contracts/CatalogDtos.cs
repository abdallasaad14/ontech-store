using System.ComponentModel.DataAnnotations;

namespace OnTech.Store.Contracts;

public sealed class CategoryDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public int ItemCount { get; init; }
    public int TotalQuantity { get; init; }
}

public sealed class SaveCategoryRequest
{
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
    /// <summary>Optional model number, SKU or part code.</summary>
    [MaxLength(64)] public string? Code { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    [Range(0, 1_000_000)] public int InitialQuantity { get; set; }
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

public sealed class AddQuantityRequest
{
    [Range(1, 1_000_000)] public int Quantity { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
}

public sealed class AdjustQuantityRequest
{
    /// <summary>The real counted quantity; the difference is recorded as an adjustment.</summary>
    [Range(0, 1_000_000)] public int NewQuantity { get; set; }
    [Required, MaxLength(500)] public string Reason { get; set; } = "";
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
    public int UserId { get; init; }
    public string UserName { get; init; } = "";
    public string? Note { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
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
