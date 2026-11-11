using OnTech.Store.Client;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.ViewModels;
using Xunit;

namespace OnTech.Store.Desktop.Tests;

/// <summary>H-3: numbers typed on an Arabic keyboard (٠-٩ or ۰-۹) are read like 0-9 everywhere.</summary>
public class NumbersTests
{
    [Theory]
    [InlineData("٥", 5)]
    [InlineData("۱۲", 12)]
    [InlineData("1٢", 12)]
    [InlineData("  ٣٤  ", 34)]
    [InlineData("‏٧", 7)]
    [InlineData("0", 0)]
    [InlineData("٠٩", 9)]
    public void Arabic_digits_parse_as_whole_numbers(string text, int expected)
    {
        Assert.True(Numbers.TryParseInt(text, out var n));
        Assert.Equal(expected, n);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("٥a")]
    [InlineData("1.5")]
    [InlineData("1 2")]
    [InlineData("99999999999")]
    public void Invalid_whole_numbers_are_refused(string? text) => Assert.False(Numbers.TryParseInt(text, out _));

    [Theory]
    [InlineData("١٢٫٥", "12.5")]
    [InlineData("١٬٢٣٤٫٥", "1234.5")]
    [InlineData("1,234.5", "1234.5")]
    [InlineData(" ۳ ", "3")]
    public void Arabic_decimals_and_separators_parse(string text, string expected)
    {
        Assert.True(Numbers.TryParseDecimal(text, out var d));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), d);
    }

    [Fact]
    public void Normalize_keeps_other_text() => Assert.Equal("INV-12 أ", Numbers.NormalizeDigits("INV-١٢ أ"));

    [Fact]
    public void Server_address_with_Arabic_digits_is_understood() =>
        Assert.Equal(new Uri("http://192.168.1.10:5080/"), StoreApiClient.NormalizeServerUrl("١٩٢.١٦٨.١.١٠"));

    [Fact]
    public async Task New_order_accepts_an_Arabic_quantity()
    {
        using var _ = new Language("ar");
        var server = new FakeServer().WithEmptyLists();
        var item = new ItemDto { Id = 1, CategoryId = 1, Name = "Mouse", Quantity = 10 };
        var session = TestSession.Create(server, Permission.CreateOrders);
        var vm = new NewOrderViewModel(session, new ShellViewModel(session));
        await vm.LoadAsync();
        vm.Items.Add(item);
        vm.SelectedItem = item;
        vm.QuantityText = "٥";
        vm.AddLineCommand.Execute(null);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(5, Assert.Single(vm.Lines).Quantity);
    }

    [Fact]
    public async Task Stock_in_accepts_an_Arabic_quantity()
    {
        var server = new FakeServer().WithEmptyLists();
        var item = new ItemDto { Id = 1, CategoryId = 1, Name = "Mouse", Quantity = 10 };
        var session = TestSession.Create(server, Permission.AddStock);
        var vm = new StockInViewModel(session, new ShellViewModel(session));
        await vm.LoadAsync();
        vm.Items.Add(item);
        vm.SelectedItem = item;
        vm.QuantityText = "۱۲";
        vm.AddLineCommand.Execute(null);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(12, Assert.Single(vm.Lines).Quantity);
    }

    [Fact]
    public async Task Customer_number_in_Arabic_digits_is_sent_as_a_number()
    {
        var server = new FakeServer().WithEmptyLists()
            .OnJson("POST", "api/customers", new CustomerDto { Id = 3, CustomerNumber = 15, CompanyName = "Acme" });
        var vm = new CustomersViewModel(TestSession.Create(server, Permission.ManageCustomers));
        vm.StartNew();
        vm.CustomerNumber = "١٥";
        vm.CompanyName = "Acme";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.Contains(server.Bodies, b => b.Contains("\"customerNumber\":15"));
    }

    [Fact]
    public async Task Supplier_number_in_Arabic_digits_is_sent_as_a_number()
    {
        var server = new FakeServer().WithEmptyLists()
            .OnJson("POST", "api/suppliers", new SupplierDto { Id = 3, SupplierNumber = 22, CompanyName = "Acme" });
        var vm = new SuppliersViewModel(TestSession.Create(server, Permission.ManageSuppliers));
        vm.StartNew();
        vm.SupplierNumber = "۲۲";
        vm.CompanyName = "Acme";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.Contains(server.Bodies, b => b.Contains("\"supplierNumber\":22"));
    }

    [Fact]
    public async Task Low_stock_warning_in_Arabic_digits_is_accepted()
    {
        var cat = new CategoryDto { Id = 1, Name = "Cables", IsActive = true };
        var server = new FakeServer().WithEmptyLists()
            .OnJson("GET", "api/categories", new[] { cat })
            .OnJson("POST", "api/items", new ItemDto { Id = 9, CategoryId = 1, Name = "HDMI", LowStockThreshold = 4 });
        var vm = new CatalogViewModel(TestSession.Create(server, Permission.ManageCatalog));
        await vm.LoadAsync();
        vm.NewItemCommand.Execute(null);
        vm.ItemName = "HDMI";
        vm.ItemLowStock = "٤";
        await vm.SaveFormCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.Contains(server.Bodies, b => b.Contains("\"lowStockThreshold\":4"));
    }

    [Fact]
    public void Customer_sheet_reads_Arabic_digit_numbers()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ontech-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "رقم,اسم الشركة\n١٢,Acme\n", System.Text.Encoding.UTF8);
        try { Assert.Equal(12, Assert.Single(CustomerSheet.Read(path)).CustomerNumber); }
        finally { File.Delete(path); }
    }
}
