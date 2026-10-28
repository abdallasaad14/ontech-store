using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Client;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly AppSettings _settings;

    /// <param name="previous">The form being replaced after a language change; its typed values are kept.</param>
    public LoginViewModel(AppSettings settings, LoginViewModel? previous = null)
    {
        _settings = settings;
        _serverUrl = previous?.ServerUrl ?? settings.ServerUrl;
        _username = previous?.Username ?? settings.LastUsername ?? "";
        _isEditingServer = previous?.IsEditingServer ?? string.IsNullOrWhiteSpace(settings.LastUsername);
    }

    [ObservableProperty] private string _serverUrl;
    [ObservableProperty] private string _username;
    [ObservableProperty] private bool _isEditingServer;

    /// <summary>Set from the PasswordBox in code-behind (PasswordBox can't be bound).</summary>
    public string Password { get; set; } = "";

    public Session? Result { get; private set; }

    public event EventHandler? LoggedIn;

    public string OtherLanguage => L.OtherLanguageName;
    public bool LanguageChanged { get; private set; }
    public event EventHandler? LanguageChangeRequested;

    [RelayCommand]
    private void SwitchLanguage()
    {
        L.Switch(_settings);
        LanguageChanged = true;
        LanguageChangeRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void EditServer() => IsEditingServer = true;

    [RelayCommand]
    private Task TestConnectionAsync() => RunAsync(async () =>
    {
        var api = StoreApiClient.Create(ServerUrl, TimeSpan.FromSeconds(8));
        await api.CheckHealthAsync();
        ServerUrl = api.ServerUrl!.ToString().TrimEnd('/');
        SuccessMessage = L.T("Connected to the server.");
    });

    [RelayCommand]
    private Task SignInAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
            throw new ArgumentException(L.T("Enter your username and password."));
        var api = StoreApiClient.Create(ServerUrl);
        var user = await api.LoginAsync(Username, Password);
        ServerUrl = api.ServerUrl!.ToString().TrimEnd('/');
        _settings.ServerUrl = ServerUrl;
        _settings.LastUsername = user.Username;
        _settings.Save();
        Result = new Session(api, user);
        LoggedIn?.Invoke(this, EventArgs.Empty);
    });
}
