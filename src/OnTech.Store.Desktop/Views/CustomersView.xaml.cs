using System.Windows.Controls;
using System.Windows.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Desktop.Views;

public partial class CustomersView : UserControl
{
    public CustomersView() => InitializeComponent();

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CustomersViewModel vm && CustomersGrid.SelectedItem is CustomerDto c)
            vm.EditCommand.Execute(c);
    }
}
