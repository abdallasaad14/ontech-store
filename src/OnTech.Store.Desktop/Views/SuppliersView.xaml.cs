using System.Windows.Controls;
using System.Windows.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Desktop.Views;

public partial class SuppliersView : UserControl
{
    public SuppliersView() => InitializeComponent();

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SuppliersViewModel vm && SuppliersGrid.SelectedItem is SupplierDto c)
            vm.EditCommand.Execute(c);
    }
}
