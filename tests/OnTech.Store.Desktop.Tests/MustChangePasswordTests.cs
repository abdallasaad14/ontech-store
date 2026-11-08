using System.Net;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;
using OnTech.Store.Desktop.ViewModels;
using Xunit;

namespace OnTech.Store.Desktop.Tests;

/// <summary>A 403 MustChangePassword from any call asks for a new password instead of showing "no permission".</summary>
public class MustChangePasswordTests
{
    private static FakeServer ServerThatRequiresANewPassword() => new FakeServer()
        .On("GET", "api/customers", _ => Task.FromResult(FakeServer.Problem(HttpStatusCode.Forbidden,
            "You must change your password before you continue.", ErrorCodes.MustChangePassword)));

    [Fact]
    public async Task Page_call_raises_the_password_change_and_shows_no_error()
    {
        var session = TestSession.Create(ServerThatRequiresANewPassword(), Permission.ManageCustomers);
        var raised = 0;
        session.PasswordChangeRequired += (_, _) => raised++;
        var vm = new CustomersViewModel(session);
        await vm.LoadAsync();
        Assert.Equal(1, raised);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Plain_403_still_shows_the_permission_message()
    {
        var server = new FakeServer().On("GET", "api/customers", _ => Task.FromResult(FakeServer.Problem(HttpStatusCode.Forbidden, "Forbidden")));
        var session = TestSession.Create(server, Permission.ManageCustomers);
        var raised = 0;
        session.PasswordChangeRequired += (_, _) => raised++;
        var vm = new CustomersViewModel(session);
        await vm.LoadAsync();
        Assert.Equal(0, raised);
        Assert.Equal("You don't have permission to do this. Ask an admin.", vm.ErrorMessage);
    }

    [Fact]
    public void Gate_asks_once_even_when_several_calls_fail_together_and_continues_after_success()
    {
        PasswordChangeGate? gate = null;
        int asked = 0, changed = 0, refused = 0;
        gate = new PasswordChangeGate(() =>
        {
            asked++;
            gate!.Request(); // a second failing call while the window is open
            return true;
        }, () => changed++, () => refused++);
        gate.Request();
        Assert.Equal((1, 1, 0), (asked, changed, refused));
        Assert.False(gate.IsOpen);
    }

    [Fact]
    public void Gate_signs_out_when_the_user_closes_the_window()
    {
        int changed = 0, refused = 0;
        new PasswordChangeGate(() => false, () => changed++, () => refused++).Request();
        Assert.Equal((0, 1), (changed, refused));
    }
}
