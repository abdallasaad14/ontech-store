using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

/// <summary>An item's card (كارت الصنف): every movement in and out, oldest first, with the balance after each one. Read only.</summary>
public sealed partial class ItemCardViewModel(Session session, ItemDto item) : PageViewModel(session)
{
    public ObservableCollection<ItemCardLineDto> Lines { get; } = new();

    [ObservableProperty] private ItemDto _item = item;
    [ObservableProperty] private int _totalIn;
    [ObservableProperty] private int _totalOut;

    public string Title => L.T("Item card - {0}", Item.Name);
    public string SubTitle => string.Join("  ·  ", new[] { Item.Code, Item.CategoryName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public override Task LoadAsync() => RunAsync(ReloadAsync);

    [RelayCommand]
    private Task RefreshAsync() => RunAsync(ReloadAsync);

    private async Task ReloadAsync()
    {
        var card = await Api.GetItemCardAsync(Item.Id);
        Item = card.Item;
        TotalIn = card.TotalIn;
        TotalOut = card.TotalOut;
        Lines.Clear();
        foreach (var l in card.Lines) Lines.Add(l);
        OnPropertyChanged(nameof(SubTitle));
    }

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
