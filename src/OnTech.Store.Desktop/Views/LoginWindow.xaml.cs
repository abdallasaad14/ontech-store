using System.Windows;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Desktop.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        vm.LoggedIn += (_, _) => DialogResult = true;
        Loaded += (_, _) =>
        {
            if (string.IsNullOrEmpty(vm.Email)) EmailBox.Focus();
            else PasswordBox.Focus();
        };
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => _vm.Password = PasswordBox.Password;
}
