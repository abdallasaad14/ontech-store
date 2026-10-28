using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Desktop.Views;

/// <summary>Shows one item's card. Opened through <see cref="ItemCards.Open"/>.</summary>
public partial class ItemCardWindow : Window
{
    public ItemCardWindow(ItemCardViewModel vm)
    {
        InitializeComponent();
        L.Apply(this);
        DataContext = vm;
        vm.ShowVoucher = voucher => new VoucherWindow(voucher) { Owner = this }.ShowDialog();
        // The newest movement is at the bottom, like a paper card; keep it in view.
        vm.Lines.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && vm.Lines.Count > 0) LinesGrid.ScrollIntoView(vm.Lines[^1]);
        };
        Loaded += async (_, _) => await vm.LoadAsync();
    }

    private void OnLineDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ItemCardViewModel vm && LinesGrid.SelectedItem is ItemCardLineDto line) vm.OpenLineCommand.Execute(line);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
