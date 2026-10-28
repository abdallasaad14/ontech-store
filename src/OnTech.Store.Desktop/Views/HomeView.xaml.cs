using System.Windows.Controls;
using System.Windows.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Desktop.Views;

public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();

    private void OnStockDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is HomeViewModel vm && StockGrid.SelectedItem is ItemDto item)
            vm.OrderItemCommand.Execute(item);
    }
}
