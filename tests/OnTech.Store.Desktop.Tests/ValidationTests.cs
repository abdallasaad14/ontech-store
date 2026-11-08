using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OnTech.Store.Client;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;
using OnTech.Store.Desktop.ViewModels;
using Xunit;

namespace OnTech.Store.Desktop.Tests;

/// <summary>M-3: every text box has the server's length limit, and server validation errors read as friendly, translated text.</summary>
public class ValidationTests
{
    public static readonly string Root = FindRoot();
    public static readonly string DesktopDir = Path.Combine(Root, "src", "OnTech.Store.Desktop");
    public static readonly string ViewsDir = Path.Combine(DesktopDir, "Views");

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "OnTech.Store.sln"))) d = d.Parent;
        return d!.FullName;
    }

    public static IEnumerable<string> XamlFiles() =>
        Directory.GetFiles(ViewsDir, "*.xaml").Append(Path.Combine(DesktopDir, "MainWindow.xaml"));

    public static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    public static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Every_text_and_password_box_has_a_MaxLength()
    {
        var missing = XamlFiles().SelectMany(f => XDocument.Load(f).Descendants()
                .Where(e => e.Name == Wpf + "TextBox" || e.Name == Wpf + "PasswordBox")
                .Where(e => !int.TryParse((string?)e.Attribute("MaxLength"), out var n) || n <= 0)
                .Select(e => $"{Path.GetFileName(f)}: {e.Attribute("Text")?.Value ?? e.Attribute(X + "Name")?.Value}"))
            .ToList();
        Assert.True(missing.Count == 0, "No MaxLength: " + string.Join("; ", missing));
    }

    /// <summary>For each form, the request DTO whose [MaxLength] the text boxes must match (binding name, minus the prefix, = property name).</summary>
    public static IEnumerable<object[]> Forms() =>
    [
        ["CustomersView.xaml", typeof(SaveCustomerRequest), ""],
        ["SuppliersView.xaml", typeof(SaveSupplierRequest), ""],
        ["UsersView.xaml", typeof(CreateUserRequest), ""],
        ["NewOrderView.xaml", typeof(CreateOrderRequest), ""],
        ["StockInView.xaml", typeof(CreateStockInRequest), ""],
        ["CatalogView.xaml", typeof(SaveCategoryRequest), "Category"],
        ["CatalogView.xaml", typeof(UpdateItemRequest), "Item"],
        ["OrdersView.xaml", typeof(OrderQuery), ""],
        ["StockInsView.xaml", typeof(StockInQuery), ""],
        ["LoginWindow.xaml", typeof(LoginRequest), ""],
    ];

    [Theory]
    [MemberData(nameof(Forms))]
    public void Text_box_limits_match_the_request_DTOs(string view, Type dto, string prefix)
    {
        var checkedCount = 0;
        foreach (var box in XDocument.Load(Path.Combine(ViewsDir, view)).Descendants(Wpf + "TextBox"))
        {
            var m = Regex.Match((string?)box.Attribute("Text") ?? "", @"\{Binding (\w+)");
            if (!m.Success || !m.Groups[1].Value.StartsWith(prefix)) continue;
            var prop = dto.GetProperty(m.Groups[1].Value[prefix.Length..]);
            if (prop?.GetCustomAttribute<MaxLengthAttribute>() is not { } max) continue;
            Assert.Equal(max.Length, int.Parse((string)box.Attribute("MaxLength")!));
            checkedCount++;
        }
        Assert.True(checkedCount > 0, $"{view}: no text box matched {dto.Name}");
    }

    [Theory]
    [InlineData("CompanyName", "The CompanyName field is required.", FieldErrorKind.Required, "Company name is required.")]
    [InlineData("CompanyName", "The field CompanyName must be a string or array type with a maximum length of '200'.", FieldErrorKind.MaxLength, "Company name can be at most 200 characters.")]
    [InlineData("Email", "The Email field is not a valid e-mail address.", FieldErrorKind.Email, "Email is not a valid email address.")]
    [InlineData("NewPassword", "The field NewPassword must be a string or array type with a minimum length of '8'.", FieldErrorKind.MinLength, "New password must be at least 8 characters.")]
    [InlineData("$.customerNumber", "The JSON value could not be converted to System.Nullable`1[System.Int32]. Path: $.customerNumber | LineNumber: 0 | BytePositionInLine: 22.", FieldErrorKind.Invalid, "Customer number has a value that is not valid.")]
    [InlineData("PageSize", "The field PageSize must be between 1 and 200.", FieldErrorKind.Range, "Page size must be between 1 and 200.")]
    [InlineData("Lines[0].Quantity", "The field Quantity must be between 1 and 2147483647.", FieldErrorKind.Range, "Quantity must be between 1 and 2147483647.")]
    [InlineData("Username", "Use 3-64 letters, digits, dots, dashes or underscores.", FieldErrorKind.Other, "Use 3-64 letters, digits, dots, dashes or underscores.")]
    public void Server_validation_messages_are_read_per_field(string key, string message, FieldErrorKind kind, string english)
    {
        var e = FieldError.Parse(key, message);
        Assert.Equal(kind, e.Kind);
        Assert.Equal(english, e.English);
    }

    private static HttpResponseMessage ValidationProblem(params (string Field, string Message)[] errors) =>
        FakeServer.Json(HttpStatusCode.BadRequest, new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            title = "One or more validation errors occurred.",
            status = 400,
            errors = errors.GroupBy(e => e.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Message).ToArray()),
        });

    private static async Task<CustomersViewModel> SaveCustomerAgainst(HttpResponseMessage answer)
    {
        var server = new FakeServer().WithEmptyLists().On("POST", "api/customers", _ => Task.FromResult(answer));
        var vm = new CustomersViewModel(TestSession.Create(server, Permission.ManageCustomers));
        vm.StartNew();
        vm.CompanyName = "Acme";
        await vm.SaveCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Server_validation_errors_show_the_field_label_in_English()
    {
        var vm = await SaveCustomerAgainst(ValidationProblem(
            ("CompanyName", "The field CompanyName must be a string or array type with a maximum length of '200'."),
            ("Email", "The Email field is not a valid e-mail address.")));
        Assert.Equal("Company name can be at most 200 characters." + Environment.NewLine + "Email is not a valid email address.", vm.ErrorMessage);
        Assert.True(vm.IsEditing);
    }

    [Fact]
    public async Task Server_validation_errors_are_Arabic_in_Arabic_mode()
    {
        using var _ = new Language("ar");
        var vm = await SaveCustomerAgainst(ValidationProblem(
            ("CompanyName", "The field CompanyName must be a string or array type with a maximum length of '200'."),
            ("Email", "The Email field is not a valid e-mail address."),
            ("TaxNumber", "The TaxNumber field is required.")));
        var lines = vm.ErrorMessage!.Split(Environment.NewLine);
        Assert.Equal(Arabic.Text["Company name"] + ": الحد الأقصى 200 حرفًا.", lines[0]);
        Assert.StartsWith(Arabic.Text["Email"] + ":", lines[1]);
        Assert.StartsWith(Arabic.Text["Tax number"] + ":", lines[2]);
        Assert.DoesNotMatch("[A-Za-z]", vm.ErrorMessage);
    }

    [Fact]
    public async Task Invalid_email_is_refused_before_sending()
    {
        var server = new FakeServer().WithEmptyLists();
        var vm = new CustomersViewModel(TestSession.Create(server, Permission.ManageCustomers));
        vm.StartNew();
        vm.CompanyName = "Acme";
        vm.Email = "not-an-email";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Enter a valid email address, or leave it empty.", vm.ErrorMessage);
        Assert.Equal(0, server.Count("POST", "api/customers"));
    }

    [Fact]
    public async Task Supplier_invalid_email_is_refused_before_sending()
    {
        var server = new FakeServer().WithEmptyLists();
        var vm = new SuppliersViewModel(TestSession.Create(server, Permission.ManageSuppliers));
        vm.StartNew();
        vm.CompanyName = "Acme";
        vm.Email = "a@";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Enter a valid email address, or leave it empty.", vm.ErrorMessage);
        Assert.Equal(0, server.Count("POST", "api/suppliers"));
    }

    [Fact]
    public void Every_field_label_and_message_template_has_Arabic()
    {
        var missing = FieldError.Labels.Values
            .Concat(Enum.GetValues<FieldErrorKind>().Where(k => k != FieldErrorKind.Other).Select(FieldError.Template))
            .Where(t => !Arabic.Text.ContainsKey(t)).ToList();
        Assert.True(missing.Count == 0, string.Join(", ", missing));
    }
}
