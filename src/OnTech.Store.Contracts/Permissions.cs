namespace OnTech.Store.Contracts;

/// <summary>
/// What a user is allowed to do. Stored as a bit mask, so a user can hold any combination.
/// Admins implicitly have every permission.
/// </summary>
[Flags]
public enum Permission
{
    None = 0,
    ViewStock = 1 << 0,
    ManageCustomers = 1 << 1,
    CreateOrders = 1 << 2,
    AddStock = 1 << 3,
    ViewOrders = 1 << 4,
    CancelOrders = 1 << 5,
    ManageCatalog = 1 << 6,
    ManageUsers = 1 << 7,
    ManageSuppliers = 1 << 8,

    /// <summary>The default set for a normal store user: the home page actions plus the logs.</summary>
    StandardUser = ViewStock | ManageCustomers | ManageSuppliers | CreateOrders | AddStock | ViewOrders,
    All = ViewStock | ManageCustomers | ManageSuppliers | CreateOrders | AddStock | ViewOrders | CancelOrders | ManageCatalog | ManageUsers
}

public enum OrderStatus
{
    Completed = 0,
    Cancelled = 1
}

public enum StockMovementType
{
    /// <summary>Quantity the item was created with.</summary>
    Initial = 0,
    /// <summary>Quantity added to an existing item (the old "Add Quantity" screen, kept for history).</summary>
    Restock = 1,
    /// <summary>Quantity taken out by an order.</summary>
    Order = 2,
    /// <summary>Quantity returned because an order was cancelled.</summary>
    OrderCancelled = 3,
    /// <summary>Manual correction after a stock count (no longer possible, kept for history).</summary>
    Adjustment = 4,
    /// <summary>Quantity received from a supplier on a stock-in voucher.</summary>
    StockIn = 5,
    /// <summary>Quantity taken back out because a stock-in voucher was cancelled.</summary>
    StockInCancelled = 6
}
