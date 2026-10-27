using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Data;

// All timestamps are stored as UTC DateTime so they sort and filter the same way on every database provider.

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    /// <summary>Upper-cased email, used for case-insensitive unique lookups.</summary>
    public string NormalizedEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public Permission Permissions { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public Permission EffectivePermissions => IsAdmin ? Permission.All : Permissions;
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public List<Item> Items { get; set; } = new();
}

public class Item
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public string? Description { get; set; }
    /// <summary>Units currently in the store. Never negative (enforced by a check constraint).</summary>
    public int Quantity { get; set; }
    public int LowStockThreshold { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Customer
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string? PhoneNumber { get; set; }
    public string? TaxNumber { get; set; }
    public string? ContactPerson { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public List<Order> Orders { get; set; } = new();
}

public class Order
{
    public int Id { get; set; }
    /// <summary>Human-friendly number, e.g. ORD-000123. Assigned from the Id when the order is saved.</summary>
    public string OrderNumber { get; set; } = "";
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public bool WithReceipt { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? Notes { get; set; }
    public OrderStatus Status { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public int? CancelledByUserId { get; set; }
    public User? CancelledByUser { get; set; }
    public string? CancelReason { get; set; }
    public List<OrderLine> Lines { get; set; } = new();
}

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int Quantity { get; set; }
    // Snapshots keep the log and receipts correct even if an item or category is renamed later.
    public string ItemName { get; set; } = "";
    public string? ItemCode { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
}

/// <summary>Every change to an item's quantity, so stock can always be explained.</summary>
public class StockMovement
{
    public long Id { get; set; }
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public StockMovementType Type { get; set; }
    public int Change { get; set; }
    public int QuantityAfter { get; set; }
    public int? OrderId { get; set; }
    public Order? Order { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}
