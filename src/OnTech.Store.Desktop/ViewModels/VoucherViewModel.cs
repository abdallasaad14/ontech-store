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
    /// <summary>Sale, Demo, Purchase or Demo return, in the current language.</summary>
    [ObservableProperty] private string? _typeText;
    [ObservableProperty] private bool _isDemoKind;
    /// <summary>For a demo: how much is back and how much is still with the customer.</summary>
    [ObservableProperty] private string? _demoInfo;

    public override Task LoadAsync() => RunAsync(async () =>
    {
        Lines.Clear();
        if (orderId is { } oid)
        {
            var o = await Api.GetOrderAsync(oid);
            IsStockIn = false;
            TypeText = VoucherTypeConverter.Describe(o.Type);
            IsDemoKind = o.Type == IssueType.Demo;
            DemoInfo = IsDemoKind ? L.T("Back: {0} · Still with the customer: {1}", o.ReturnedQuantity, o.OpenQuantity) : null;
            Heading = IsDemoKind ? L.T("Demo voucher {0}", o.IssueVoucherNumber) : L.T("Issue voucher {0}", o.IssueVoucherNumber);
            Fill(o.CreatedAt, L.T("CUSTOMER"), o.CustomerName, L.T("ISSUE VOUCHER"), o.IssueVoucherNumber, L.T("RECEIPT"), o.ReceiptNumber,
                o.Status, o.CancelledAt, o.CancelledByUserName, o.CancelReason, o.Notes, o.CreatedByUserName, o.Lines);
        }
        else if (stockInId is { } sid)
        {
            var s = await Api.GetStockInAsync(sid);
            IsStockIn = true;
            TypeText = VoucherTypeConverter.Describe(s.Type);
            IsDemoKind = s.Type == ReceiveType.DemoReturn;
            DemoInfo = null;
            Heading = IsDemoKind ? L.T("Demo return {0}", s.StockInNumber) : L.T("Stock in {0}", s.StockInNumber);
            Fill(s.CreatedAt, IsDemoKind ? L.T("CUSTOMER") : L.T("SUPPLIER"), s.PartyName, L.T("RECEIPT VOUCHER"), s.VoucherNumber, L.T("SUPPLIER INVOICE"), s.SupplierInvoiceNumber,
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
