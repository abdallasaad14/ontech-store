using System.Windows;

namespace OnTech.Store.Desktop.Views;

/// <summary>Asks for one line of text (or a password). Used through <see cref="Services.Dialogs.Prompt"/>.</summary>
public partial class PromptWindow : Window
{
    private readonly bool _isPassword;

    public PromptWindow(string title, string label, string? initial, bool isPassword, string okText)
    {
        InitializeComponent();
        Title = title;
        LabelText.Text = label;
        OkButton.Content = okText;
        _isPassword = isPassword;
        InputBox.Text = initial ?? "";
        if (isPassword)
        {
            InputBox.Visibility = Visibility.Collapsed;
            PasswordInput.Visibility = Visibility.Visible;
        }
        Loaded += (_, _) =>
        {
            if (_isPassword) PasswordInput.Focus();
            else
            {
                InputBox.Focus();
                InputBox.SelectAll();
            }
        };
    }

    public string Value => _isPassword ? PasswordInput.Password : InputBox.Text;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
