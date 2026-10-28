using System.Windows.Controls;
using System.Windows.Input;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Desktop.Views;

public partial class UsersView : UserControl
{
    public UsersView() => InitializeComponent();

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is UsersViewModel vm && UsersGrid.SelectedItem is UserDto u)
            vm.EditCommand.Execute(u);
    }
}
