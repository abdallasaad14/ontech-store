using System.Windows.Controls;
using System.Windows.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Desktop.Views;

public partial class CatalogView : UserControl
{
    public CatalogView() => InitializeComponent();

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CatalogViewModel vm && ItemsGrid.SelectedItem is ItemDto item)
            vm.EditItemCommand.Execute(item);
    }
}
