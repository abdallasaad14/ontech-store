using System.Windows;
using OnTech.Store.Desktop.Views;

namespace OnTech.Store.Desktop.Services;

public static class Dialogs
{
    private static Window? Owner => Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;

    public static bool Confirm(string message, string title = "OnTech Store") =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>Asks for one line of text. Returns null if cancelled.</summary>
    public static string? Prompt(string title, string label, string? initial = null, bool isPassword = false, string okText = "OK")
    {
        var dlg = new PromptWindow(title, label, initial, isPassword, okText) { Owner = Owner };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }
}
