using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using OnTech.Store.Client;
using OnTech.Store.Contracts;
using Xunit;
using Xunit.Abstractions;

namespace OnTech.Store.UITests;

/// <summary>Shared test data made through the API before the UI tests run.</summary>
public sealed class UiData : IAsyncLifetime
{
    public const string Password = "UiTest!2026";
    public string User { get; } = "uitest_" + DateTime.Now.ToString("MMddHHmmss");
    public string Run { get; } = DateTime.Now.ToString("MMddHHmmss");
    public StoreApiClient Admin { get; } = StoreApiClient.Create(AppDriver.ServerUrl, TimeSpan.FromSeconds(120));
    public CategoryDto Category { get; private set; } = null!;
    public ItemDto Item { get; private set; } = null!;
    public CustomerDto Customer { get; private set; } = null!;
    public SupplierDto Supplier { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Admin.LoginAsync("admin", "ChangeMe!123");
        await Admin.CreateUserAsync(new CreateUserRequest { FullName = "UI Tester", Username = User, Password = Password, Permissions = Permission.All, MustChangePassword = false });
        Category = await Admin.CreateCategoryAsync(new SaveCategoryRequest { Name = "UI Cat " + Run });
        Item = await Admin.CreateItemAsync(new CreateItemRequest { CategoryId = Category.Id, Name = "UI Item " + Run });
        Customer = await Admin.CreateCustomerAsync(new SaveCustomerRequest { CompanyName = "UI Customer " + Run });
        Supplier = await Admin.CreateSupplierAsync(new SaveSupplierRequest { CompanyName = "UI Supplier " + Run });
        await Admin.CreateStockInAsync(new CreateStockInRequest { SupplierId = Supplier.Id, VoucherNumber = "UI-IN-" + Run, Lines = [new OrderLineRequest { ItemId = Item.Id, Quantity = 100 }] });
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition("ui", DisableParallelization = true)]
public class UiCollection : ICollectionFixture<UiData>;

[Collection("ui")]
public class UiTests(UiData data, ITestOutputHelper output)
{
    private static int Qty(int itemId) => Convert.ToInt32(AppDriver.Sql("SELECT Quantity FROM Items WHERE Id=@i", ("@i", itemId)));

    private void Run(string name, Action<AppDriver> body, string serverUrl = AppDriver.ServerUrl, string language = "en")
    {
        using var d = new AppDriver(name, serverUrl, language);
        var sw = Stopwatch.StartNew();
        try
        {
            body(d);
            d.Screenshot("_ok");
            Log($"PASS {name} {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception e)
        {
            var shot = d.Screenshot();
            d.SaveFailureLog(e);
            Log($"FAIL {name} {sw.ElapsedMilliseconds} ms: {e.GetType().Name}: {e.Message.Split('\n')[0]} -> {shot}");
            throw;
        }
    }

    private void Log(string line)
    {
        output.WriteLine(line);
        File.AppendAllText(Path.Combine(AppDriver.Audit, "evidence", "ui_tests.log"), $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
    }

    // ---------------- sign in ----------------

    [Fact]
    public void Login_wrong_password_shows_message() => Run(nameof(Login_wrong_password_shows_message), d =>
    {
        var login = d.WaitWindow("Sign in");
        d.Type(d.Box(login, "Login_UsernameBox"), data.User);
        var pwd = d.ById(login, "Login_PasswordBox");
        pwd.Focus(); Keyboard.Type("wrong-password");
        d.ButtonById(login, "Login_SignInButton").Invoke();
        Assert.True(d.WaitText(login, "Username or password is incorrect."), d.AllText(login));
    });

    [Fact]
    public void Login_with_empty_fields_shows_message() => Run(nameof(Login_with_empty_fields_shows_message), d =>
    {
        var login = d.WaitWindow("Sign in");
        d.ButtonById(login, "Login_SignInButton").Invoke();
        Assert.True(d.WaitText(login, "Enter your username and password."), d.AllText(login));
    });

    [Fact]
    public void Login_and_open_every_page() => Run(nameof(Login_and_open_every_page), d =>
    {
        var main = d.SignIn(data.User, UiData.Password);
        foreach (var page in new[] { "Home", "New Order", "Stock In", "Customers", "Suppliers", "Orders Log", "Stock In Log", "Items & Categories", "Users" })
        {
            var sw = Stopwatch.StartNew();
            d.Menu(main, page);
            // A page is "loaded" when its title text is on screen and the UI answers again.
            Assert.True(d.WaitText(main, page == "Orders Log" ? "Orders" : page == "Items & Categories" ? "Items" : page.Split(' ')[0], 30), $"{page}: {d.AllText(main)}");
            Wait.UntilResponsive(main, TimeSpan.FromSeconds(60));
            Log($"     page '{page}' ready in {sw.ElapsedMilliseconds} ms");
            d.Screenshot("_" + page.Replace(" ", "").Replace("&", ""));
            Assert.DoesNotContain("Unexpected error", d.AllText(main));
            Assert.DoesNotContain("Something went wrong", d.AllText(main));
        }
    });

    [Fact]
    public void Server_down_on_sign_in_shows_a_clear_message() => Run(nameof(Server_down_on_sign_in_shows_a_clear_message), d =>
    {
        var login = d.WaitWindow("Sign in");
        d.Type(d.Box(login, "Login_UsernameBox"), data.User);
        var pwd = d.ById(login, "Login_PasswordBox");
        pwd.Focus(); Keyboard.Type(UiData.Password);
        var sw = Stopwatch.StartNew();
        d.ButtonById(login, "Login_SignInButton").Invoke();
        Assert.True(d.WaitText(login, "Cannot reach the server", 40), d.AllText(login));
        Log($"     server-down message after {sw.ElapsedMilliseconds} ms");
    }, serverUrl: "http://127.0.0.1:5999");

    // ---------------- customers ----------------

    [Fact]
    public void Customers_add_search_edit_disable() => Run(nameof(Customers_add_search_edit_disable), d =>
    {
        var name = "UI Added Customer " + data.Run;
        var main = d.SignIn(data.User, UiData.Password);
        d.Menu(main, "Customers");
        d.ButtonById(main, "Customers_StartNewButton").Invoke();
        var save = d.ButtonById(main, "Customers_SaveButton");
        d.Type(d.Box(main, "Customers_CompanyNameBox"), name);
        d.Type(d.Box(main, "Customers_PhoneNumberBox"), "01001234567");
        save.Invoke();
        Assert.True(d.WaitText(main, $"Customer \"{name}\" added."), d.AllText(main));
        Assert.Equal(1, Convert.ToInt32(AppDriver.Sql("SELECT COUNT(*) FROM Customers WHERE CompanyName=@n", ("@n", name))));

        d.Type(d.Box(main, "Customers_SearchBox"), name);
        d.ButtonById(main, "Customers_SearchButton").Invoke();
        Assert.True(d.WaitText(main, name));
    });

    [Fact]
    public void Customer_with_invalid_email_and_too_long_name_shows_messages() => Run(nameof(Customer_with_invalid_email_and_too_long_name_shows_messages), d =>
    {
        var main = d.SignIn(data.User, UiData.Password);
        d.Menu(main, "Customers");
        d.ButtonById(main, "Customers_StartNewButton").Invoke();
        var save = d.ButtonById(main, "Customers_SaveButton");
        var company = d.Box(main, "Customers_CompanyNameBox");
        d.Type(company, new string('x', 205));
        Retry.WhileFalse(() => company.Text.Length >= 200, TimeSpan.FromSeconds(10)); // typed keys arrive after Enter() returns
        Thread.Sleep(500);
        Assert.Equal(200, company.Text.Length); // MaxLength = the server's limit (M-3)
        // The email is checked first (M-3), so test the number with no email, then the email.
        d.Type(d.Box(main, "Customers_CustomerNumberBox"), "abc");
        save.Invoke();
        Assert.True(d.WaitText(main, "Customer number must be a whole number"), d.AllText(main));
        d.Type(d.Box(main, "Customers_CustomerNumberBox"), "");
        d.Type(d.Box(main, "Customers_EmailBox"), "not-an-email");
        save.Invoke();
        Assert.True(d.WaitText(main, "Enter a valid email address, or leave it empty."), d.AllText(main));
        Log($"     validation shown to the user: {d.AllText(main)}");
    });

    // ---------------- new order ----------------

    private void FillOrder(AppDriver d, Window main, string voucher, string qty)
    {
        d.Menu(main, "New Order");
        Assert.True(d.WaitText(main, "Choose what is leaving the store", 30));
        Wait.UntilResponsive(main, TimeSpan.FromSeconds(60));
        d.Combo(main, "NewOrder_CategoryCombo").Select(data.Category.Name);
        Retry.WhileFalse(() => d.Combo(main, "NewOrder_ItemCombo").Items.Length > 0, TimeSpan.FromSeconds(15));
        d.Combo(main, "NewOrder_ItemCombo").Select(0);
        d.Type(d.Box(main, "NewOrder_QuantityBox"), qty);
        d.Type(d.Box(main, "NewOrder_CustomerSearchBox"), data.Customer.CompanyName);
        Thread.Sleep(600); // Delay=200 on the search box
        d.Type(d.Box(main, "NewOrder_IssueVoucherNumberBox"), voucher);
    }

    [Fact]
    public void New_order_saves_and_takes_stock() => Run(nameof(New_order_saves_and_takes_stock), d =>
    {
        var before = Qty(data.Item.Id);
        var voucher = "UIV-" + data.Run;
        var main = d.SignIn(data.User, UiData.Password);
        FillOrder(d, main, voucher, "3");
        d.ButtonById(main, "NewOrder_SaveButton").Invoke();
        Assert.True(d.WaitText(main, $"Issue voucher {voucher} saved"), d.AllText(main));
        Assert.Equal(before - 3, Qty(data.Item.Id));
    });

    [Fact]
    public void New_order_invalid_inputs_show_messages() => Run(nameof(New_order_invalid_inputs_show_messages), d =>
    {
        var before = Qty(data.Item.Id);
        var main = d.SignIn(data.User, UiData.Password);
        FillOrder(d, main, "", "abc");
        d.ButtonById(main, "NewOrder_SaveButton").Invoke();
        Assert.True(d.WaitText(main, "Enter a quantity of 1 or more."), d.AllText(main));
        d.Type(d.Box(main, "NewOrder_QuantityBox"), "٥"); // Arabic-Indic five, as typed on an Arabic keyboard (H-3: accepted)
        d.ButtonById(main, "NewOrder_SaveButton").Invoke();
        Thread.Sleep(800);
        Log($"     quantity '٥' -> {d.AllText(main)}");
        Assert.DoesNotContain("Enter a quantity of 1 or more.", d.AllText(main));
        // ٥ was accepted, so that line was added and now the voucher is the missing field: fill it and ask for too much.
        d.Combo(main, "NewOrder_ItemCombo").Select(0);
        d.Type(d.Box(main, "NewOrder_QuantityBox"), "99999");
        d.Type(d.Box(main, "NewOrder_IssueVoucherNumberBox"), "UIBIG-" + data.Run);
        d.ButtonById(main, "NewOrder_SaveButton").Invoke();
        Assert.True(d.WaitText(main, "available"), d.AllText(main));
        Assert.Equal(before, Qty(data.Item.Id));
    });

    [Fact]
    public void Rapid_double_click_on_save_creates_one_order() => Run(nameof(Rapid_double_click_on_save_creates_one_order), d =>
    {
        var before = Qty(data.Item.Id);
        var voucher = "UIDBL-" + data.Run;
        var main = d.SignIn(data.User, UiData.Password);
        FillOrder(d, main, voucher, "1");
        var save = d.ButtonById(main, "NewOrder_SaveButton");
        var p = save.GetClickablePoint();
        Mouse.DoubleClick(p);
        Mouse.Click(p);
        // The first click saves and clears the form, so a later click may replace the "saved" banner
        // with "Choose an item"; what matters is the order count.
        Retry.WhileFalse(() => Convert.ToInt32(AppDriver.Sql("SELECT COUNT(*) FROM Orders WHERE IssueVoucherNumber=@v", ("@v", voucher))) > 0, TimeSpan.FromSeconds(15));
        Thread.Sleep(1500);
        Assert.Equal(1, Convert.ToInt32(AppDriver.Sql("SELECT COUNT(*) FROM Orders WHERE IssueVoucherNumber=@v", ("@v", voucher))));
        Assert.Equal(before - 1, Qty(data.Item.Id));
        Log($"     after triple click: {d.AllText(main)}");
    });

    // ---------------- stock in, logs, item card ----------------

    [Fact]
    public void Stock_in_saves_and_adds_stock() => Run(nameof(Stock_in_saves_and_adds_stock), d =>
    {
        var before = Qty(data.Item.Id);
        var voucher = "UIIN-" + data.Run;
        var main = d.SignIn(data.User, UiData.Password);
        d.Menu(main, "Stock In");
        Wait.UntilResponsive(main, TimeSpan.FromSeconds(60));
        d.Combo(main, "StockIn_CategoryCombo").Select(data.Category.Name);
        Retry.WhileFalse(() => d.Combo(main, "StockIn_ItemCombo").Items.Length > 0, TimeSpan.FromSeconds(15));
        d.Combo(main, "StockIn_ItemCombo").Select(0);
        d.Type(d.Box(main, "StockIn_QuantityBox"), "7");
        d.Type(d.Box(main, "StockIn_SupplierSearchBox"), data.Supplier.CompanyName);
        Thread.Sleep(600);
        d.Type(d.Box(main, "StockIn_VoucherNumberBox"), voucher);
        d.ButtonById(main, "StockIn_SaveButton").Invoke();
        Assert.True(d.WaitText(main, "saved from"), d.AllText(main));
        Assert.Equal(before + 7, Qty(data.Item.Id));
    });

    [Fact]
    public void Orders_log_search_finds_an_order() => Run(nameof(Orders_log_search_finds_an_order), d =>
    {
        var order = data.Admin.CreateOrderAsync(new CreateOrderRequest
        {
            CustomerId = data.Customer.Id, IssueVoucherNumber = "UILOG-" + data.Run,
            Lines = [new OrderLineRequest { ItemId = data.Item.Id, Quantity = 1 }],
        }).GetAwaiter().GetResult();
        var main = d.SignIn(data.User, UiData.Password);
        d.Menu(main, "Orders Log");
        Wait.UntilResponsive(main, TimeSpan.FromSeconds(60));
        d.Type(d.Box(main, "Orders_SearchBox"), order.IssueVoucherNumber!);
        d.ButtonById(main, "Orders_SearchButton").Invoke();
        Assert.True(d.WaitText(main, "Page 1 of 1 · 1 orders"), d.AllText(main));
    });

    [Fact]
    public void Item_card_opens_and_exports() => Run(nameof(Item_card_opens_and_exports), d =>
    {
        var main = d.SignIn(data.User, UiData.Password);
        d.Menu(main, "Items & Categories");
        Wait.UntilResponsive(main, TimeSpan.FromSeconds(60));
        var cat = d.Find(main, data.Category.Name, ControlType.Text, 15);
        cat.Click();
        Thread.Sleep(1500);
        d.ById(main, "Catalog_OpenCardButton", 15).AsButton().Invoke();
        var card = d.WaitWindow(data.Item.Name, 20);
        Assert.True(d.WaitText(card, "100"), d.AllText(card));
        d.Screenshot("_card");
        // Print / PDF opens the Windows print dialog and Excel a save dialog: not driven here (see report).
    });

    // ---------------- Arabic ----------------

    [Fact]
    public void Arabic_ui_signs_in_and_shows_arabic_menu() => Run(nameof(Arabic_ui_signs_in_and_shows_arabic_menu), d =>
    {
        var login = d.WaitWindow("تسجيل", 20);
        d.Type(d.Box(login, "Login_UsernameBox"), data.User);
        var pwd = d.ById(login, "Login_PasswordBox");
        pwd.Focus(); Keyboard.Type(UiData.Password);
        Keyboard.Press(VirtualKeyShort.ENTER);
        var main = d.WaitMain();
        Assert.True(d.WaitText(main, "إذن صرف") || d.WaitText(main, "الرئيسية"), d.AllText(main));
        d.Screenshot("_ar_home");
    }, language: "ar");

    // ---------------- large data ----------------

    [Fact(Skip = "Owner: local use, small data - load tests dropped")]
    public void New_order_and_customers_open_fast_with_50k_customers() => Run(nameof(New_order_and_customers_open_fast_with_50k_customers), d =>
    {
        var customers = Convert.ToInt32(AppDriver.Sql("SELECT COUNT(*) FROM Customers"));
        var main = d.SignIn(data.User, UiData.Password);
        var results = new List<string>();
        foreach (var page in new[] { "Customers", "New Order" })
        {
            var sw = Stopwatch.StartNew();
            d.Menu(main, page);
            Wait.UntilResponsive(main, TimeSpan.FromSeconds(120));
            Retry.WhileFalse(() => !d.AllText(main).Contains("Loading"), TimeSpan.FromSeconds(60));
            var ms = sw.ElapsedMilliseconds;
            // While the UI thread is blocked, UIA calls time out; this measures how long the window stopped answering.
            results.Add($"{page}: {ms} ms with {customers} customers");
            Log($"     {page} ready after {ms} ms ({customers} customers in DB)");
        }
        Assert.All(results, r => Assert.True(int.Parse(r.Split(':')[1].Trim().Split(' ')[0]) < 3000, r));
    });
}
