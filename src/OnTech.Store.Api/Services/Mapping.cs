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
    };

    public static OrderDto ToDto(this Order o) => new()
    {
        Id = o.Id,
        OrderNumber = o.OrderNumber,
        CustomerId = o.CustomerId,
        CustomerName = o.Customer?.CompanyName ?? "",
        CustomerTaxNumber = o.Customer?.TaxNumber,
        CustomerPhoneNumber = o.Customer?.PhoneNumber,
        WithReceipt = o.WithReceipt,
        ReceiptNumber = o.ReceiptNumber,
        Notes = o.Notes,
        Status = o.Status,
        TotalQuantity = o.Lines.Sum(l => l.Quantity),
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
        }).ToList(),
    };

    public static string? Clean(this string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
