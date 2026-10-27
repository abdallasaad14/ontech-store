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

    /// <summary>The default set for a normal store user: the home page actions plus the orders log.</summary>
    StandardUser = ViewStock | ManageCustomers | CreateOrders | AddStock | ViewOrders,
    All = ViewStock | ManageCustomers | CreateOrders | AddStock | ViewOrders | CancelOrders | ManageCatalog | ManageUsers
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
    /// <summary>Quantity added to an existing item.</summary>
    Restock = 1,
    /// <summary>Quantity taken out by an order.</summary>
    Order = 2,
    /// <summary>Quantity returned because an order was cancelled.</summary>
    OrderCancelled = 3,
    /// <summary>Manual correction after a stock count.</summary>
    Adjustment = 4
}
