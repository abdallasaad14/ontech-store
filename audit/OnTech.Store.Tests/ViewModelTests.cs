using System.Globalization;
using OnTech.Store.Client;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Tests;

/// <summary>Phase 6a: ViewModels against the real SQL Server-backed API (port 5081). No windows are opened.</summary>
[Collection("ui-culture")] // L.Set changes the process culture; keep these tests serial
public class ViewModelTests
{
    private static async Task<(Session Session, ShellViewModel Shell)> SignInAsync()
    {
        var api = StoreApiClient.Create(Server.SqlServer.Url);
        var user = await api.LoginAsync("admin", Server.AdminPassword);
        var session = new Session(api, user);
        return (session, new ShellViewModel(session, Page.Orders));
    }

    [Fact]
    public async Task Login_with_wrong_password_shows_a_message_and_no_session()
    {
        L.Set("en");
        var settings = new AppSettings { ServerUrl = Server.SqlServer.Url };
        var vm = new LoginViewModel(settings) { Username = "admin", Password = "wrong" };
        await vm.SignInCommand.ExecuteAsync(null);
        Assert.Null(vm.Result);
        Assert.Equal("Username or password is incorrect.", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Login_error_is_shown_in_Arabic_when_Arabic_is_on()
    {
        L.Set("ar");
        try
        {
            var vm = new LoginViewModel(new AppSettings { ServerUrl = Server.SqlServer.Url }) { Username = "admin", Password = "wrong" };
            await vm.SignInCommand.ExecuteAsync(null);
            Evidence.Log("viewmodels", $"Arabic login error: {vm.ErrorMessage}");
            Assert.NotEqual("Username or password is incorrect.", vm.ErrorMessage);
        }
        finally { L.Set("en"); }
    }

    [Fact]
    public async Task Server_down_message_is_readable()
    {
        L.Set("en");
        var vm = new LoginViewModel(new AppSettings { ServerUrl = "127.0.0.1:5999" }) { Username = "admin", Password = "x" };
        await vm.SignInCommand.ExecuteAsync(null);
        Evidence.Log("viewmodels", $"server down on login: {vm.ErrorMessage}");
        Assert.StartsWith("Cannot reach the server", vm.ErrorMessage);
    }

    /// <summary>An Arabic keyboard types ٥ (Arabic-Indic five). int.TryParse in ar-EG does not accept it.</summary>
    [Fact]
    public void Arabic_Indic_digits_are_accepted_as_a_quantity()
    {
        L.Set("ar");
        try
        {
            var parsed = OnTech.Store.Client.Numbers.TryParseInt("٥", out var v); // the helper every number field uses since H-3
            Evidence.Log("viewmodels", $"Numbers.TryParseInt(\"٥\") in {CultureInfo.CurrentCulture.Name}: {parsed} ({v})");
            Assert.True(parsed && v == 5);
        }
        finally { L.Set("en"); }
    }

    [Fact]
    public async Task New_order_saves_through_the_viewmodel_and_shows_success()
    {
        L.Set("en");
        var s = Server.SqlServer;
        var item = await Api.ItemAsync(s, 7);
        var cust = await Api.CustomerAsync(s);
        var (session, shell) = await SignInAsync();
        var vm = new NewOrderViewModel(session, shell);
        await vm.LoadAsync();
        vm.SelectedCategory = vm.Categories.Single(c => c.Id == item.CategoryId);
        await WaitFor(() => vm.Items.Any(i => i.Id == item.Id));
        vm.SelectedItem = vm.Items.Single(i => i.Id == item.Id);
        vm.QuantityText = "2";
        vm.CustomerSearch = cust.CompanyName;
        vm.IssueVoucherNumber = Api.U("VM");
        Assert.Equal(cust.Id, vm.SelectedCustomer?.Id);
        Assert.Contains("5 left", vm.Summary);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.NotNull(vm.LastOrder);
        Assert.Equal(5, s.ItemQty(item.Id));
        Assert.Empty(vm.Lines);
    }

    [Fact]
    public async Task New_order_blocks_more_than_available_before_calling_the_server()
    {
        L.Set("en");
        var s = Server.SqlServer;
        var item = await Api.ItemAsync(s, 2);
        var (session, shell) = await SignInAsync();
        var vm = new NewOrderViewModel(session, shell);
        await vm.LoadAsync();
        vm.SelectedCategory = vm.Categories.Single(c => c.Id == item.CategoryId);
        await WaitFor(() => vm.Items.Any(i => i.Id == item.Id));
        vm.SelectedItem = vm.Items.Single(i => i.Id == item.Id);
        vm.QuantityText = "3";
        vm.AddLineCommand.Execute(null);
        Assert.Equal($"Only 2 {item.Name} available.", vm.ErrorMessage);
        Assert.Empty(vm.Lines);
    }

    [Fact]
    public async Task Save_command_cannot_run_twice_at_the_same_time()
    {
        L.Set("en");
        var s = Server.SqlServer;
        var item = await Api.ItemAsync(s, 5);
        var cust = await Api.CustomerAsync(s);
        var (session, shell) = await SignInAsync();
        var vm = new NewOrderViewModel(session, shell);
        await vm.LoadAsync();
        vm.SelectedCategory = vm.Categories.Single(c => c.Id == item.CategoryId);
        await WaitFor(() => vm.Items.Any(i => i.Id == item.Id));
        vm.SelectedItem = vm.Items.Single(i => i.Id == item.Id);
        vm.QuantityText = "1";
        vm.CustomerSearch = cust.CompanyName;
        vm.IssueVoucherNumber = Api.U("DBL");
        var first = vm.SaveCommand.ExecuteAsync(null);     // a real server call: it awaits
        var canSecond = vm.SaveCommand.CanExecute(null);
        var busy = vm.IsBusy;
        await first;
        Evidence.Log("viewmodels", $"SaveCommand.CanExecute while a save is running: {canSecond}, IsBusy={busy}");
        Assert.False(canSecond);
        Assert.Equal(4, s.ItemQty(item.Id));
    }

    [Fact]
    public async Task Expired_or_disabled_login_sends_the_user_back_to_sign_in()
    {
        L.Set("en");
        var s = Server.SqlServer;
        var (client, user) = await Api.UserAsync(s, Permission.StandardUser);
        var session = new Session(client, user);
        var expired = false;
        session.Expired += (_, _) => expired = true;
        await (await Api.AdminAsync(s)).DisableUserAsync(user.Id);
        var vm = new OrdersViewModel(session);
        await vm.LoadAsync();
        Assert.True(expired);
    }

    /// <summary>Customers page and New Order load every customer in one call (no paging).</summary>
    [Fact]
    public async Task Customers_page_does_not_load_every_customer_at_once()
    {
        L.Set("en");
        await PerformanceTests.SeedAsync(Server.SqlServer);
        var (session, _) = await SignInAsync();
        var vm = new CustomersViewModel(session);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await vm.LoadAsync();
        Evidence.Log("viewmodels", $"CustomersViewModel.LoadAsync: {vm.Customers.Count} rows in {sw.ElapsedMilliseconds} ms (no paging)");
        Assert.True(vm.Customers.Count <= 1000, $"{vm.Customers.Count} customers loaded into one ObservableCollection");
    }

    private static async Task WaitFor(Func<bool> cond)
    {
        for (var i = 0; i < 100 && !cond(); i++) await Task.Delay(100);
        Assert.True(cond());
    }
}

[CollectionDefinition("ui-culture", DisableParallelization = true)]
public class UiCultureCollection;
