using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OnTech.Store.Client;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

/// <summary>An item's card (كارت الصنف): every movement in and out, oldest first, with the balance after each one. Read only.</summary>
public sealed partial class ItemCardViewModel(Session session, ItemDto item) : PageViewModel(session)
{
    public ObservableCollection<ItemCardLineDto> Lines { get; } = new();
    public ObservableCollection<ItemCardCustomerDto> TopCustomers { get; } = new();

    [ObservableProperty] private ItemDto _item = item;
    [ObservableProperty] private int _totalIn;
    [ObservableProperty] private int _totalOut;
    [ObservableProperty] private int _openingBalance;
    [ObservableProperty] private int _closingBalance;
    [ObservableProperty] private bool _hasPeriodStart;
    [ObservableProperty] private string _periodText = "";
    [ObservableProperty] private ItemCardLineDto? _selectedLine;

    // The period being picked; it applies when the user presses Show.
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;

    public string Title => L.T("Item card - {0}", Item.Name);
    public string SubTitle => string.Join("  ·  ", new[] { Item.Code, Item.CategoryName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>Asks the window to show an order or stock-in (set by the window).</summary>
    public Action<VoucherViewModel>? ShowVoucher { get; set; }

    public override Task LoadAsync() => RunAsync(ReloadAsync);

    [RelayCommand]
    private Task RefreshAsync() => RunAsync(ReloadAsync);

    [RelayCommand]
    private Task ShowPeriodAsync() => RunAsync(async () =>
    {
        if (FromDate is { } f && ToDate is { } t && f.Date > t.Date) throw new ArgumentException(L.T("The start date is after the end date."));
        await ReloadAsync();
    });

    [RelayCommand]
    private Task ShowAllDatesAsync()
    {
        FromDate = ToDate = null;
        return RunAsync(ReloadAsync);
    }

    private async Task ReloadAsync()
    {
        // Whole days on this PC: from the start of the first day to the end of the last.
        var query = new ItemCardQuery
        {
            From = FromDate is { } f ? new DateTimeOffset(f.Date) : null,
            To = ToDate is { } t ? new DateTimeOffset(t.Date.AddDays(1).AddTicks(-1)) : null,
        };
        var card = await Api.GetItemCardAsync(Item.Id, query);
        Item = card.Item;
        TotalIn = card.TotalIn;
        TotalOut = card.TotalOut;
        OpeningBalance = card.OpeningBalance;
        ClosingBalance = card.ClosingBalance;
        HasPeriodStart = query.From is not null;
        PeriodText = DescribePeriod(FromDate, ToDate);
        Lines.Clear();
        foreach (var l in card.Lines) Lines.Add(l);
        TopCustomers.Clear();
        foreach (var c in card.TopCustomers) TopCustomers.Add(c);
        OnPropertyChanged(nameof(SubTitle));
        OnPropertyChanged(nameof(Title));
    }

    private static string DescribePeriod(DateTime? from, DateTime? to) => (from, to) switch
    {
        (null, null) => L.T("All dates"),
        ({ } f, null) => L.T("From {0}", Day(f)),
        (null, { } t) => L.T("Up to {0}", Day(t)),
        ({ } f, { } t) => L.T("From {0} to {1}", Day(f), Day(t)),
    };

    private static string Day(DateTime d) => d.ToString("d MMM yyyy", L.Culture);

    /// <summary>Opens the order or stock-in behind a movement (the opening balance has none).</summary>
    [RelayCommand]
    private void OpenLine(ItemCardLineDto? line)
    {
        if (line is null) return;
        if (line.OrderId is null && line.StockInId is null)
        {
            ErrorMessage = L.T("This movement has no order or stock-in to open.");
            return;
        }
        ShowVoucher?.Invoke(new VoucherViewModel(Session, line.OrderId, line.StockInId));
    }

    // ---- The same columns for print and Excel ----

    public static IReadOnlyList<string> Headers =>
    [
        L.T("DATE"), L.T("MOVEMENT"), L.T("IN"), L.T("OUT"), L.T("BALANCE"), L.T("CUSTOMER / SUPPLIER"),
        L.T("VOUCHER NO."), L.T("ORDER / STOCK-IN NO."), L.T("RECEIPT / INVOICE"), L.T("BY"),
    ];

    public static IReadOnlyList<object?> Cells(ItemCardLineDto l) =>
    [
        l.Date.ToLocalTime().ToString("dd MMM yyyy HH:mm", L.Culture), Describe(l.Type),
        l.In == 0 ? null : l.In, l.Out == 0 ? null : l.Out, l.Balance, l.PartyName, l.VoucherNumber, l.DocumentNumber, l.InvoiceNumber, l.UserName,
    ];

    /// <summary>Lines above the table on paper and in Excel: the item, the period and the totals.</summary>
    public IReadOnlyList<string> HeaderLines()
    {
        var lines = new List<string> { L.T("Item card - {0}", Item.Name) };
        if (SubTitle.Length > 0) lines.Add(SubTitle);
        lines.Add(L.T("Period: {0}", PeriodText));
        if (HasPeriodStart) lines.Add(L.T("Opening balance: {0}", OpeningBalance));
        lines.Add(L.T("Total in: {0}   Total out: {1}   Balance: {2}", TotalIn, TotalOut, ClosingBalance));
        lines.Add(L.T("Printed {0}", DateTime.Now.ToString("d MMM yyyy HH:mm", L.Culture)));
        return lines;
    }

    [RelayCommand]
    private Task PrintAsync() => RunAsync(() =>
    {
        ItemCardPrinter.Print(this);
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task ExportExcelAsync() => RunAsync(() =>
    {
        var safeName = string.Concat((Item.Code ?? Item.Name).Split(System.IO.Path.GetInvalidFileNameChars()));
        var dlg = new SaveFileDialog
        {
            Title = L.T("Save the item card"),
            FileName = L.T("Item card {0}", safeName) + ".xlsx",
            Filter = L.T("Excel workbook") + "|*.xlsx",
        };
        if (dlg.ShowDialog() != true) return Task.CompletedTask;

        var rows = new List<IReadOnlyList<object?>>();
        if (HasPeriodStart) rows.Add([null, L.T("Opening balance"), null, null, OpeningBalance]);
        rows.AddRange(Lines.Select(Cells));
        rows.Add([null, L.T("Total"), TotalIn, TotalOut, ClosingBalance]);
        if (TopCustomers.Count > 0)
        {
            rows.Add([]);
            rows.Add([L.T("TOP CUSTOMERS"), L.T("UNITS"), L.T("ORDERS")]);
            rows.AddRange(TopCustomers.Select(c => (IReadOnlyList<object?>)[c.CustomerName, c.Quantity, c.Orders]));
        }
        try
        {
            XlsxWriter.Write(dlg.FileName, L.T("Item card {0}", safeName), HeaderLines(), Headers, rows, rightToLeft: L.IsArabic);
        }
        catch (System.IO.IOException)
        {
            throw new ArgumentException(L.T("Couldn't save the file. If it is open in Excel, close it and try again."));
        }
        SuccessMessage = L.T("Saved {0}.", System.IO.Path.GetFileName(dlg.FileName));
        return Task.CompletedTask;
    });

    public static string Describe(StockMovementType type) => L.T(type switch
    {
        StockMovementType.Initial => "Opening balance",
        StockMovementType.Restock => "Quantity added",
        StockMovementType.Order => "Issued to customer",
        StockMovementType.OrderCancelled => "Order cancelled",
        StockMovementType.Adjustment => "Stock count correction",
        StockMovementType.StockIn => "Received from supplier",
        StockMovementType.StockInCancelled => "Stock-in cancelled",
        _ => type.ToString(),
    });
}
