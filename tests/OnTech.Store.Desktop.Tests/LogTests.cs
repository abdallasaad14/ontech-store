using System.Net;
using OnTech.Store.Contracts;
using OnTech.Store.Desktop.Services;
using OnTech.Store.Desktop.ViewModels;
using Xunit;

namespace OnTech.Store.Desktop.Tests;

/// <summary>M-5: the app writes a daily log, keeps 14 files, and logs unhandled errors with the full exception.</summary>
public sealed class LogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ontech-logtest-" + Guid.NewGuid().ToString("N"));
    private readonly FileLog _previous = Log.Current;

    public void Dispose()
    {
        Log.Current = _previous;
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private string AllText() => string.Concat(Directory.GetFiles(_folder).Select(File.ReadAllText));

    [Fact]
    public void Default_folder_is_under_LocalAppData() =>
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OnTechStore", "logs"), Log.DefaultFolder);

    [Fact]
    public void Writes_one_file_per_day()
    {
        var now = new DateTime(2026, 11, 8, 23, 59, 0);
        var log = new FileLog(_folder, () => now);
        log.Write("INF", "before midnight", null);
        now = now.AddMinutes(2);
        log.Write("INF", "after midnight", null);
        Assert.Equal(["OnTechStore-20261108.log", "OnTechStore-20261109.log"], Directory.GetFiles(_folder).Select(f => Path.GetFileName(f)!).Order().ToArray());
        Assert.Contains("[INF] after midnight", File.ReadAllText(log.FileFor(now)));
    }

    [Fact]
    public void Keeps_the_newest_14_files()
    {
        Directory.CreateDirectory(_folder);
        var start = new DateTime(2026, 10, 1);
        for (var i = 0; i < 20; i++) File.WriteAllText(Path.Combine(_folder, $"OnTechStore-{start.AddDays(i):yyyyMMdd}.log"), "x");
        File.WriteAllText(Path.Combine(_folder, "other.txt"), "not a log");
        var today = start.AddDays(20);
        new FileLog(_folder, () => today).Write("INF", "today", null);

        var left = Directory.GetFiles(_folder, "OnTechStore-*.log").Select(Path.GetFileName).Order().ToList();
        Assert.Equal(14, left.Count);
        Assert.Equal($"OnTechStore-{today:yyyyMMdd}.log", left[^1]);
        Assert.Equal($"OnTechStore-{start.AddDays(7):yyyyMMdd}.log", left[0]);
        Assert.True(File.Exists(Path.Combine(_folder, "other.txt")));
    }

    [Fact]
    public void Unobserved_task_exception_is_logged_with_the_stack_and_observed()
    {
        Log.Current = new FileLog(_folder);
        Exception thrown;
        try { throw new InvalidOperationException("boom in a task"); } catch (Exception ex) { thrown = ex; }
        var args = new UnobservedTaskExceptionEventArgs(new AggregateException(thrown));
        CrashHandlers.OnUnobservedTaskException(null, args);
        Assert.True(args.Observed);
        var text = AllText();
        Assert.Contains("[ERR] Unobserved task exception", text);
        Assert.Contains("System.InvalidOperationException: boom in a task", text);
        Assert.Contains(nameof(Unobserved_task_exception_is_logged_with_the_stack_and_observed), text); // stack trace
    }

    [Fact]
    public void Domain_and_dispatcher_handlers_log()
    {
        Log.Current = new FileLog(_folder);
        CrashHandlers.OnDomainUnhandledException(null, new UnhandledExceptionEventArgs(new ApplicationException("fatal"), isTerminating: true));
        CrashHandlers.LogDispatcherException(new NullReferenceException("ui"));
        var text = AllText();
        Assert.Contains("the app is closing", text);
        Assert.Contains("System.ApplicationException: fatal", text);
        Assert.Contains("System.NullReferenceException: ui", text);
    }

    [Fact]
    public async Task Api_errors_are_logged()
    {
        Log.Current = new FileLog(_folder);
        var server = new FakeServer().On("GET", "api/customers", _ => Task.FromResult(FakeServer.Problem(HttpStatusCode.Conflict, "Customer number 12 is already used.", ErrorCodes.Duplicate)));
        await new CustomersViewModel(TestSession.Create(server, Permission.ManageCustomers)).LoadAsync();
        Assert.Contains("CustomersViewModel: API error 409 Duplicate: Customer number 12 is already used.", AllText());
    }

    [Fact]
    public async Task Failed_sign_in_is_logged_without_the_password()
    {
        Log.Current = new FileLog(_folder);
        var vm = new LoginViewModel(new AppSettings { ServerUrl = "127.0.0.1:1" }) { Username = "sara", Password = "S3cret-Pass!" };
        await vm.SignInCommand.ExecuteAsync(null);
        var text = AllText();
        Assert.Contains("Sign-in failed for 'sara'", text);
        Assert.DoesNotContain("S3cret-Pass!", text);
    }

    [Fact]
    public void Logging_to_a_folder_that_cannot_be_created_does_not_throw()
    {
        var file = Path.Combine(Path.GetTempPath(), "ontech-notafolder-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "x");
        try { new FileLog(Path.Combine(file, "logs")).Write("INF", "x", null); }
        finally { File.Delete(file); }
    }
}
