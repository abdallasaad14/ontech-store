using System.Windows;
using System.Windows.Threading;
using OnTech.Store.Desktop.Services;
using OnTech.Store.Desktop.ViewModels;
using OnTech.Store.Desktop.Views;

namespace OnTech.Store.Desktop;

public partial class App : Application
{
    private readonly AppSettings _settings = AppSettings.Load();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        ShowLogin();
    }

    private void ShowLogin()
    {
        var vm = new LoginViewModel(_settings);
        var login = new LoginWindow(vm);
        if (login.ShowDialog() == true && vm.Result is { } session)
            StartSession(session);
        else
            Shutdown();
    }

    private void StartSession(Session session)
    {
        if (session.User.MustChangePassword)
        {
            var change = new ChangePasswordWindow(new ChangePasswordViewModel(session, isRequired: true))
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = true,
            };
            if (change.ShowDialog() != true)
            {
                session.Api.Logout();
                ShowLogin();
                return;
            }
        }

        var shell = new ShellViewModel(session);
        var main = new MainWindow(shell);
        var leaving = false;

        void BackToLogin(string? message)
        {
            if (leaving) return;
            leaving = true;
            session.Api.Logout();
            main.Close();
            if (message is not null)
                MessageBox.Show(message, "OnTech Store", MessageBoxButton.OK, MessageBoxImage.Information);
            ShowLogin();
        }

        shell.SignOutRequested += (_, _) => BackToLogin(null);
        shell.ChangePasswordRequested += (_, _) =>
            new ChangePasswordWindow(new ChangePasswordViewModel(session, isRequired: false)) { Owner = main }.ShowDialog();
        session.Expired += (_, _) => main.Dispatcher.BeginInvoke(() =>
            BackToLogin("Your sign-in has ended (it expired, or your account was changed by an admin). Please sign in again."));
        main.Closed += (_, _) =>
        {
            if (!leaving) Shutdown();
        };

        MainWindow = main;
        main.Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "OnTech Store", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
