namespace OnTech.Store.Contracts;

/// <summary>
/// The "code" extension the server puts in a ProblemDetails error body ({ status, title, code }), so the desktop app can react to
/// the kind of error without reading the English title. The title stays the readable message.
/// </summary>
public static class ErrorCodes
{
    /// <summary>409: the record was changed (or disabled) on another PC after this form loaded it. Reload and try again.</summary>
    public const string ConcurrencyConflict = "ConcurrencyConflict";
    /// <summary>409: a number, code, voucher or username that must be unique is already used.</summary>
    public const string Duplicate = "Duplicate";
    /// <summary>403: the user must change their password before doing anything else.</summary>
    public const string MustChangePassword = "MustChangePassword";
}

/// <summary>
/// Which permissions may READ which lists. The server's GET endpoints and the desktop menu both use this table,
/// so a page is only in the menu when every list it loads will answer. A user may read a list when they hold ANY of the bits.
/// </summary>
public static class ReadAccess
{
    /// <summary>GET api/categories, api/categories/{id}, api/items, api/items/{id}: the item pickers on New Order, Stock In and Items &amp; Categories.</summary>
    public const Permission Catalog = Permission.ViewStock | Permission.CreateOrders | Permission.AddStock | Permission.ManageCatalog;

    /// <summary>GET api/customers, api/customers/{id}: the Customers page and the customer picker on New Order.</summary>
    public const Permission Customers = Permission.ViewStock | Permission.ManageCustomers | Permission.CreateOrders;

    /// <summary>GET api/suppliers, api/suppliers/{id}: the Suppliers page and the supplier picker on Stock In.</summary>
    public const Permission Suppliers = Permission.ViewStock | Permission.ManageSuppliers | Permission.AddStock;

    /// <summary>GET api/dashboard/summary, api/items/{id}/card, api/stock-movements: stock figures. ViewStock only.</summary>
    public const Permission StockFigures = Permission.ViewStock;

    /// <summary>True when <paramref name="held"/> has at least one of the bits in <paramref name="anyOf"/>.</summary>
    public static bool HasAny(this Permission held, Permission anyOf) => (held & anyOf) != 0;
}
