using OnTech.Store.Api.Data;
using OnTech.Store.Api.Infrastructure;
using OnTech.Store.Contracts;

namespace OnTech.Store.Api.Services;

public static class Mapping
{
    public static UserDto ToDto(this User u) => new()
    {
        Id = u.Id,
        FullName = u.FullName,
        Username = u.Username,
        IsAdmin = u.IsAdmin,
        IsActive = u.IsActive,
        MustChangePassword = u.MustChangePassword,
        Permissions = u.EffectivePermissions,
        CreatedAt = u.CreatedAt.ToUtcOffset(),
        LastLoginAt = u.LastLoginAt.ToUtcOffset(),
        Version = u.Version,
    };

    public static ItemDto ToDto(this Item i) => new()
    {
        Id = i.Id,
        CategoryId = i.CategoryId,
        CategoryName = i.Category?.Name ?? "",
        Name = i.Name,
        Code = i.Code,
        Description = i.Description,
        Quantity = i.Quantity,
        LowStockThreshold = i.LowStockThreshold,
        IsLowStock = i.LowStockThreshold > 0 && i.Quantity <= i.LowStockThreshold,
        IsActive = i.IsActive,
        UpdatedAt = i.UpdatedAt.ToUtcOffset(),
        Version = i.Version,
    };

    public static OrderDto ToDto(this Order o) => new()
    {
        Id = o.Id,
        OrderNumber = o.OrderNumber,
        CustomerId = o.CustomerId,
        CustomerName = o.Customer?.CompanyName ?? "",
        CustomerTaxNumber = o.Customer?.TaxNumber,
        CustomerPhoneNumber = o.Customer?.PhoneNumber,
        IssueVoucherNumber = o.IssueVoucherNumber,
        Type = o.Type,
        WithReceipt = o.WithReceipt,
        ReceiptNumber = o.ReceiptNumber,
        Notes = o.Notes,
        Status = o.Status,
        TotalQuantity = o.Lines.Sum(l => l.Quantity),
        ReturnedQuantity = o.Lines.Sum(l => l.ReturnedQuantity),
        OpenQuantity = o.OpenQuantity(),
        CreatedByUserId = o.CreatedByUserId,
        CreatedByUserName = o.CreatedByUser?.FullName ?? "",
        CreatedAt = o.CreatedAt.ToUtcOffset(),
        CancelledAt = o.CancelledAt.ToUtcOffset(),
        CancelledByUserName = o.CancelledByUser?.FullName,
        CancelReason = o.CancelReason,
        Lines = o.Lines.OrderBy(l => l.Id).Select(l => new OrderLineDto
        {
            Id = l.Id,
            ItemId = l.ItemId,
            ItemName = l.ItemName,
            ItemCode = l.ItemCode,
            CategoryId = l.CategoryId,
            CategoryName = l.CategoryName,
            Quantity = l.Quantity,
            ReturnedQuantity = l.ReturnedQuantity,
            OpenQuantity = o.IsOpenDemo() ? l.Quantity - l.ReturnedQuantity : 0,
        }).ToList(),
    };

    public static StockInDto ToDto(this StockIn s) => new()
    {
        Id = s.Id,
        StockInNumber = s.StockInNumber,
        Type = s.Type,
        SupplierId = s.SupplierId,
        CustomerId = s.CustomerId,
        CustomerName = s.Customer?.CompanyName,
        PartyName = s.Supplier?.CompanyName ?? s.Customer?.CompanyName ?? "",
        SupplierName = s.Supplier?.CompanyName ?? "",
        SupplierTaxNumber = s.Supplier?.TaxNumber,
        VoucherNumber = s.VoucherNumber,
        SupplierInvoiceNumber = s.SupplierInvoiceNumber,
        Notes = s.Notes,
        Status = s.Status,
        TotalQuantity = s.Lines.Sum(l => l.Quantity),
        CreatedByUserId = s.CreatedByUserId,
        CreatedByUserName = s.CreatedByUser?.FullName ?? "",
        CreatedAt = s.CreatedAt.ToUtcOffset(),
        CancelledAt = s.CancelledAt.ToUtcOffset(),
        CancelledByUserName = s.CancelledByUser?.FullName,
        CancelReason = s.CancelReason,
        Lines = s.Lines.OrderBy(l => l.Id).Select(l => new OrderLineDto
        {
            Id = l.Id,
            ItemId = l.ItemId,
            ItemName = l.ItemName,
            ItemCode = l.ItemCode,
            CategoryId = l.CategoryId,
            CategoryName = l.CategoryName,
            Quantity = l.Quantity,
            DemoOrderLineId = l.DemoOrderLineId,
            DemoOrderId = l.DemoOrderLine?.OrderId,
            DemoIssueVoucherNumber = l.DemoOrderLine?.Order?.IssueVoucherNumber,
        }).ToList(),
    };

    /// <summary>A demo that is not cancelled: its lines are still (partly) with the customer.</summary>
    public static bool IsOpenDemo(this Order o) => o.Type == IssueType.Demo && o.Status == OrderStatus.Completed;

    /// <summary>Units of a demo still with the customer; 0 for a sale or a cancelled demo.</summary>
    public static int OpenQuantity(this Order o) => o.IsOpenDemo() ? o.Lines.Sum(l => l.Quantity - l.ReturnedQuantity) : 0;

    public static string? Clean(this string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// A document number as stored: trimmed and upper-case (invariant), so "ab-1" and "AB-1" are the same voucher on every database.
    /// Used for issue vouchers (= order numbers), receipt numbers, stock-in vouchers and supplier invoice numbers.
    /// </summary>
    public static string? CleanDocNumber(this string? s) => s.Clean()?.ToUpperInvariant();
}
