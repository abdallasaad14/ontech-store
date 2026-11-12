using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.ViewModels;
using OnTech.Store.Desktop.Views;
using Xunit;

namespace OnTech.Store.Desktop.Tests;

/// <summary>
/// The Demo page lays out off screen (no window is shown): the grouped "With customers now" grid
/// must give its columns real widths, or every row looks empty.
/// </summary>
public class DemoGridLayoutTests
{
    private static OpenDemoLineDto Line(int lineId, int customerId, string customer, string item, int qty) => new()
    {
        OrderLineId = lineId, OrderId = lineId, IssueVoucherNumber = $"D{lineId}", CreatedAt = DateTimeOffset.Now.AddDays(-2),
        CustomerId = customerId, CustomerNumber = customerId + 30, CustomerName = customer, ItemId = lineId, ItemName = item,
        CategoryName = "Cat", Quantity = qty, OpenQuantity = qty,
    };

    private static void OnSta(Func<Task> body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            Task task;
            try { task = body(); } catch (Exception e) { task = Task.FromException(e); }
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
            error = task.Exception?.GetBaseException();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    /// <summary>
    /// The app's theme and converters, without the App class: App's startup opens the login window,
    /// which must never appear from a test.
    /// </summary>
    private static void LoadAppResources()
    {
        if (Application.Current is not null) return;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/OnTechStore;component/Themes/Theme.xaml") });
        var appXaml = File.ReadAllText(Path.Combine(ValidationTests.DesktopDir, "App.xaml"));
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(appXaml, @"<local:(\w+) x:Key=""(\w+)""\s*/>"))
            app.Resources[m.Groups[2].Value] = Activator.CreateInstance(typeof(App).Assembly.GetType("OnTech.Store.Desktop." + m.Groups[1].Value, throwOnError: true)!);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    /// <summary>Every page view loads its XAML (built, never shown), in English and Arabic.</summary>
    [Fact]
    public void Every_page_view_loads()
    {
        OnSta(() =>
        {
            LoadAppResources();
            var types = typeof(App).Assembly.GetTypes()
                .Where(t => t.Namespace == "OnTech.Store.Desktop.Views" && typeof(UserControl).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null);
            var failed = new List<string>();
            foreach (var lang in new[] { "en", "ar" })
            {
                Services.L.Set(lang);
                foreach (var t in types)
                {
                    try { Activator.CreateInstance(t); }
                    catch (Exception e) { failed.Add($"{lang} {t.Name}: {(e.InnerException ?? e).GetBaseException().Message} | {e.InnerException?.Message ?? e.Message}"); }
                }
            }
            Services.L.Set("en");
            Assert.True(failed.Count == 0, string.Join("\n", failed));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Grouped_open_demos_show_their_columns_and_cells()
    {
        OnSta(async () =>
        {
            LoadAppResources();
            var server = new FakeServer().WithEmptyLists().OnJson("GET", "api/demos/open", new[]
            {
                Line(1, 7, "Advanced Computer Technology - ACT", "Scanner", 1),
                Line(2, 3, "Beta", "Printer", 2),
                Line(3, 3, "Beta", "Ribbon", 3),
            });
            var session = TestSession.Create(server, Permission.ViewOrders | Permission.AddStock);
            var vm = new DemosViewModel(session, new ShellViewModel(session));
            // As in the app: the page is laid out empty first, then the demos arrive.
            var view = new DemosView { DataContext = vm };
            void Layout() { view.Measure(new Size(1400, 800)); view.Arrange(new Rect(0, 0, 1400, 800)); view.UpdateLayout(); }
            Layout();
            await vm.LoadAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Layout();

            var grid = (DataGrid)view.FindName("OpenGrid");
            Assert.All(grid.Columns.Where(c => c.Visibility == Visibility.Visible), c => Assert.True(c.ActualWidth > 40, $"{c.Header} is {c.ActualWidth} wide"));
            var cells = Descendants<DataGridCell>(grid).Where(c => c.Column.Visibility == Visibility.Visible).ToList();
            Assert.NotEmpty(cells);
            Assert.Contains(cells, c => c.ActualWidth > 100 && Descendants<TextBlock>(c).Any(t => t.Text == "Scanner"));

            // Group header: the name alone, then the count in its own text (no customer number).
            var headers = Descendants<GroupItem>(grid).Select(g => Descendants<TextBlock>(g).Take(2).Select(t => t.Text).ToArray()).ToList();
            Assert.Contains(headers, h => h.SequenceEqual(new[] { "Advanced Computer Technology - ACT", "1 unit(s) out" }));
            Assert.Contains(headers, h => h.SequenceEqual(new[] { "Beta", "5 unit(s) out" }));
        });
    }

    [Theory]
    [InlineData(1, "D-ISSUE-9")]
    [InlineData(2, "D-RETURN-4")]
    public void Demo_issue_and_return_tabs_show_their_rows(int tab, string voucher)
    {
        OnSta(async () =>
        {
            LoadAppResources();
            var server = new FakeServer().WithEmptyLists()
                .OnJson("GET", "api/orders", new PagedResult<OrderDto> { Items = [new OrderDto { Id = 9, OrderNumber = "D-ISSUE-9", IssueVoucherNumber = "D-ISSUE-9", Type = IssueType.Demo, CustomerName = "Beta", TotalQuantity = 2, CreatedAt = DateTimeOffset.Now }], TotalCount = 1, Page = 1, PageSize = 50 })
                .OnJson("GET", "api/stock-ins", new PagedResult<StockInDto> { Items = [new StockInDto { Id = 4, StockInNumber = "D-RETURN-4", VoucherNumber = "D-RETURN-4", Type = ReceiveType.DemoReturn, PartyName = "Beta", CustomerName = "Beta", CreatedAt = DateTimeOffset.Now }], TotalCount = 1, Page = 1, PageSize = 50 });
            var session = TestSession.Create(server, Permission.ViewOrders | Permission.AddStock);
            var vm = new DemosViewModel(session, new ShellViewModel(session));
            var view = new DemosView { DataContext = vm };
            // MainWindow maps these view models to their views; the tabs show them through it.
            foreach (var (model, page) in new[] { (typeof(OrdersViewModel), typeof(OrdersView)), (typeof(StockInsViewModel), typeof(StockInsView)) })
                view.Resources.Add(new DataTemplateKey(model), new DataTemplate(model) { VisualTree = new FrameworkElementFactory(page) });
            void Layout() { view.Measure(new Size(1400, 800)); view.Arrange(new Rect(0, 0, 1400, 800)); view.UpdateLayout(); }
            Layout();
            await vm.LoadAsync();
            vm.SelectedTab = tab;
            await Wait.For(() => (tab == 1 ? vm.Issues!.TotalCount : vm.Returns!.TotalCount) == 1);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Layout();

            var grid = Descendants<DataGrid>(view).Single(g => g.Items.Count > 0 && g.ActualWidth > 0);
            Assert.All(grid.Columns.Where(c => c.Visibility == Visibility.Visible), c => Assert.True(c.ActualWidth >= 40, $"{c.Header} is {c.ActualWidth} wide"));
            Assert.Contains(Descendants<DataGridCell>(grid), c => c.ActualWidth > 40 && Descendants<TextBlock>(c).Any(t => t.Text == voucher));
        });
    }
}
