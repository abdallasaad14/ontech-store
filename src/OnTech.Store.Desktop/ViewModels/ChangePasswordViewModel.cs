using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.ViewModels;

public sealed partial class ChangePasswordViewModel(Session session, bool isRequired) : ViewModelBase
{
    public bool IsRequired { get; } = isRequired;
    public string Intro => IsRequired
        ? "For security, choose your own password before you start. It must be at least 8 characters."
        : "Your new password must be at least 8 characters.";

    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";

    public event EventHandler? Done;

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (NewPassword.Length < 8) throw new ArgumentException("The new password must be at least 8 characters.");
        if (NewPassword != ConfirmPassword) throw new ArgumentException("The two new passwords don't match.");
        await session.Api.ChangePasswordAsync(CurrentPassword, NewPassword);
        session.Update(await session.Api.RefreshMeAsync());
        Done?.Invoke(this, EventArgs.Empty);
    });
}
