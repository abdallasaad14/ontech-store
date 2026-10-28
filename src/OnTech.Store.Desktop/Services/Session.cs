using OnTech.Store.Client;
using OnTech.Store.Contracts;

namespace OnTech.Store.Desktop.Services;

/// <summary>The signed-in user and their connection to the server.</summary>
public sealed class Session(StoreApiClient api, UserDto user)
{
    public StoreApiClient Api { get; } = api;
    public UserDto User { get; private set; } = user;

    /// <summary>Raised when the server says the login is no longer valid (expired token or disabled account).</summary>
    public event EventHandler? Expired;

    public bool Can(Permission permission) => User.IsAdmin || (User.Permissions & permission) == permission;

    public void Update(UserDto user) => User = user;

    public void NotifyExpired() => Expired?.Invoke(this, EventArgs.Empty);
}
