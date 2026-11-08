using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using OnTech.Store.Client;
using OnTech.Store.Desktop;
using OnTech.Store.Desktop.Services;
using OnTech.Store.Desktop.ViewModels;
using OnTech.Store.Desktop.Views;

namespace OnTech.Store.Tests;

/// <summary>
/// Phase 5.1: opens every page of the real app in-process (real XAML, real ViewModels, real API on 5081),
/// with PresentationTraceSources binding tracing on, and records every binding error WPF reports.
/// </summary>
[Collection("ui-culture")]
public class BindingTraceTests
{
    private sealed class Collector : TraceListener
    {
        public readonly StringBuilder All = new();
        public override void Write(string? message) { lock (All) All.Append(message); }
        public override void WriteLine(string? message) { lock (All) All.AppendLine(message); }
    }

    [Fact]
    public void Every_page_opens_without_binding_errors_in_English_and_Arabic()
    {
        const string language = "en+ar";
        string? failure = null;
        var errors = new List<string>();
        var t = new Thread(() =>
        {
            try { errors = Run("en"); errors.AddRange(Run("ar")); }
            catch (Exception e) { failure = e.ToString(); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        if (!t.Join(TimeSpan.FromMinutes(8))) failure = "UI thread did not finish within 8 minutes";
        if (failure is not null) File.WriteAllText(Path.Combine(Paths.Audit, "evidence", $"binding_errors_{language}_failure.txt"), failure);
        Assert.Null(failure);
        var distinct = errors.Distinct().ToList();
        File.WriteAllLines(Path.Combine(Paths.Audit, "evidence", $"binding_errors_{language}.txt"), distinct);
        Assert.Empty(distinct);
    }

    private static List<string> Run(string language)
    {
        var collector = new Collector();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(collector);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        PresentationTraceSources.ResourceDictionarySource.Listeners.Add(collector);
        PresentationTraceSources.ResourceDictionarySource.Switch.Level = SourceLevels.Warning;

        // "/Assets/..." pack URIs resolve against the resource assembly, which is the test host here, not OnTechStore.
        if (Application.ResourceAssembly != typeof(App).Assembly)
            typeof(Application).GetField("_resourceAssembly", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .SetValue(null, typeof(App).Assembly);
        var app = Application.Current as App ?? new App();
        if (app.Resources.MergedDictionaries.Count == 0) app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        L.Set(language);

        var api = StoreApiClient.Create(Server.SqlServer.Url);
        var user = Task.Run(() => api.LoginAsync("admin", Server.AdminPassword)).GetAwaiter().GetResult(); // Task.Run: no deadlock on the STA dispatcher
        var session = new Session(api, user);
        var shell = new ShellViewModel(session);
        var main = new MainWindow(shell) { Left = -3000, Top = -3000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        main.Show();
        var errors = new List<string>();

        // Sanity check: a deliberately wrong binding must be reported, or "no errors" means nothing.
        collector.All.Clear();
        var probe = new System.Windows.Controls.TextBlock { DataContext = new object() };
        probe.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding("NoSuchProperty"));
        Pump(TimeSpan.FromMilliseconds(300));
        var probeSeen = collector.All.ToString().Contains("NoSuchProperty");
        File.AppendAllText(Path.Combine(Paths.Audit, "evidence", $"binding_trace_{language}.txt"), $"probe binding error captured: {probeSeen}{Environment.NewLine}");
        if (!probeSeen) errors.Add("binding trace listener is not receiving errors");

        foreach (var page in Enum.GetValues<Page>())
        {
            collector.All.Clear();
            var sw = Stopwatch.StartNew();
            shell.Navigate(page);
            Pump(TimeSpan.FromSeconds(3));
            File.AppendAllText(Path.Combine(Paths.Audit, "evidence", $"binding_trace_{language}.txt"),
                $"{page}: navigate + 3 s pump took {sw.ElapsedMilliseconds} ms (anything well above 3000 ms = UI thread blocked){Environment.NewLine}");
            errors.AddRange(Lines(collector, $"[{language}] {page}"));
        }

        // Pop-up windows
        var item = Task.Run(() => api.GetItemsAsync()).GetAwaiter().GetResult().First();
        foreach (var (name, make) in new (string, Func<Window>)[]
                 {
                     ("ItemCardWindow", () => new ItemCardWindow(new ItemCardViewModel(session, item))),
                     ("ChangePasswordWindow", () => new ChangePasswordWindow(new ChangePasswordViewModel(session, false))),
                     ("LoginWindow", () => new LoginWindow(new LoginViewModel(new AppSettings()))),
                 })
        {
            collector.All.Clear();
            Window w;
            try { w = make(); }
            catch (Exception e) { errors.Add($"[{language}] {name}: could not be created: {e.GetType().Name} {e.Message}"); continue; }
            w.Left = -3000; w.Top = -3000; w.ShowActivated = false; w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Show();
            Pump(TimeSpan.FromSeconds(3));
            errors.AddRange(Lines(collector, $"[{language}] {name}"));
            w.Close();
        }
        main.Close();
        L.Set("en");
        return errors;
    }

    private static IEnumerable<string> Lines(Collector c, string where) =>
        c.All.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains("Error", StringComparison.OrdinalIgnoreCase) || l.Contains("Warning", StringComparison.OrdinalIgnoreCase))
            .Select(l => $"{where}: {l.Trim()}");

    private static void Pump(TimeSpan time)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(time, DispatcherPriority.Background, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }
}


