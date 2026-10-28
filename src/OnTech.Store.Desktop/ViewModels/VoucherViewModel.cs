using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

/// <summary>One order or stock-in, read only, opened from a line on the item card.</summary>
public sealed partial class VoucherViewModel(Session session, int? orderId, int? stockInId) : PageViewModel(session)
{
    public ObservableCollection<OrderLineDto> Lines { get; } = new();

    [ObservableProperty] private string _heading = "";
    [ObservableProperty] private DateTimeOffset? _date;
    [ObservableProperty] private string _partyLabel = "";
    [ObservableProperty] private string? _partyName;
    [ObservableProperty] private string _voucherLabel = "";
    [ObservableProperty] private string? _voucherNumber;
    [ObservableProperty] private string _invoiceLabel = "";
    [ObservableProperty] private string? _invoiceNumber;
    [ObservableProperty] private OrderStatus _status;
    [ObservableProperty] private string? _cancelInfo;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string? _createdBy;
    [ObservableProperty] private bool _isStockIn;

    public override Task LoadAsync() => RunAsync(async () =>
    {
        Lines.Clear();
        if (orderId is { } oid)
        {
            var o = await Api.GetOrderAsync(oid);
            IsStockIn = false;
            Heading = L.T("Order {0}", o.OrderNumber);
            Fill(o.CreatedAt, L.T("CUSTOMER"), o.CustomerName, L.T("ISSUE VOUCHER"), o.IssueVoucherNumber, L.T("RECEIPT"), o.ReceiptNumber,
                o.Status, o.CancelledAt, o.CancelledByUserName, o.CancelReason, o.Notes, o.CreatedByUserName, o.Lines);
        }
        else if (stockInId is { } sid)
        {
            var s = await Api.GetStockInAsync(sid);
            IsStockIn = true;
            Heading = L.T("Stock in {0}", s.StockInNumber);
            Fill(s.CreatedAt, L.T("SUPPLIER"), s.SupplierName, L.T("RECEIPT VOUCHER"), s.VoucherNumber, L.T("SUPPLIER INVOICE"), s.SupplierInvoiceNumber,
                s.Status, s.CancelledAt, s.CancelledByUserName, s.CancelReason, s.Notes, s.CreatedByUserName, s.Lines);
        }
    }, clearMessages: false);

    private void Fill(DateTimeOffset date, string partyLabel, string party, string voucherLabel, string? voucher, string invoiceLabel, string? invoice,
        OrderStatus status, DateTimeOffset? cancelledAt, string? cancelledBy, string? cancelReason, string? notes, string createdBy,
        IEnumerable<OrderLineDto> lines)
    {
        Date = date;
        PartyLabel = partyLabel;
        PartyName = party;
        VoucherLabel = voucherLabel;
        VoucherNumber = voucher;
        InvoiceLabel = invoiceLabel;
        InvoiceNumber = invoice;
        Status = status;
        CancelInfo = cancelledAt is { } at
            ? L.T("Cancelled on {0} by {1}: {2}", at.ToLocalTime().ToString("d MMM yyyy HH:mm", L.Culture), cancelledBy ?? "", cancelReason ?? "")
            : null;
        Notes = notes;
        CreatedBy = createdBy;
        foreach (var l in lines) Lines.Add(l);
    }
}
