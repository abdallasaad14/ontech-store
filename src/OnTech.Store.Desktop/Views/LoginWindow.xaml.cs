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

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _vm.Password = PasswordBox.Password;
        if (PasswordText.Text != PasswordBox.Password) PasswordText.Text = PasswordBox.Password;
    }

    private void OnPasswordTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _vm.Password = PasswordText.Text;
        if (PasswordBox.Password != PasswordText.Text) PasswordBox.Password = PasswordText.Text;
    }

    private void OnShowPasswordChanged(object sender, RoutedEventArgs e)
    {
        var show = ShowPassword.IsChecked == true;
        PasswordText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PasswordBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        ShowPassword.ToolTip = show ? "Hide password" : "Show password";
        if (show)
        {
            PasswordText.Focus();
            PasswordText.CaretIndex = PasswordText.Text.Length;
        }
        else
        {
            PasswordBox.Focus();
        }
    }
}
