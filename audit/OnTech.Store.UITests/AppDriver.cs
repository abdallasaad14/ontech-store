using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Microsoft.Data.SqlClient;

namespace OnTech.Store.UITests;

/// <summary>
/// Starts the real OnTechStore.exe (Debug build) against the audit server on port 5081 and drives it like a user.
/// Controls are found by their AutomationId (<Screen>_<Control>, M-9); Name / ControlType only for texts.
/// </summary>
public sealed class AppDriver : IDisposable
{
    public const string ServerUrl = "http://127.0.0.1:5081";
    public const string SqlServer = "Server=.\\SQLEXPRESS;Database=OnTechStore;Trusted_Connection=True;TrustServerCertificate=True";
    public static readonly string Root = FindRoot();
    public static readonly string Exe = Path.Combine(Root, "src", "OnTech.Store.Desktop", "bin", "Debug", "net8.0-windows", "OnTechStore.exe");
    public static readonly string Audit = Path.Combine(Root, "audit");
    private static readonly string SettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OnTechStore", "settings.json");
    private static readonly string SettingsBackup = Path.Combine(Audit, "evidence", "settings.json.user-backup");

    public Application App { get; }
    public UIA3Automation Automation { get; } = new();
    public ConditionFactory Cf => Automation.ConditionFactory;
    private readonly string _test;

    public AppDriver(string testName, string serverUrl = ServerUrl, string language = "en", string? lastUser = null)
    {
        _test = testName;
        // The app reads %LocalAppData%\OnTechStore\settings.json. Keep the owner's file and restore it in Dispose.
        if (File.Exists(SettingsFile) && !File.Exists(SettingsBackup)) File.Copy(SettingsFile, SettingsBackup);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new { ServerUrl = serverUrl, LastUsername = lastUser, Language = language }));
        App = Application.Launch(new ProcessStartInfo(Exe) { WorkingDirectory = Path.GetDirectoryName(Exe)! });
    }

    public static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "OnTech.Store.sln"))) d = d.Parent;
        return d!.FullName;
    }

    // ---------------- finding things ----------------

    public Window WaitWindow(string titleContains, int seconds = 20)
    {
        // Owned windows (the item card) are children of their owner in UIA, not top-level, so look one level down too.
        var w = Retry.WhileNull(() => App.GetAllTopLevelWindows(Automation)
                .SelectMany(x => x.FindAllChildren(Cf.ByControlType(ControlType.Window)).Select(c => c.AsWindow()).Prepend(x))
                .FirstOrDefault(x => x.Title.Contains(titleContains)),
            TimeSpan.FromSeconds(seconds), TimeSpan.FromMilliseconds(200)).Result;
        return w ?? throw new TimeoutException($"No window with title containing '{titleContains}'. Open: {string.Join(" | ", App.GetAllTopLevelWindows(Automation).Select(x => x.Title))}");
    }

    public Window WaitMain(int seconds = 30)
    {
        var w = Retry.WhileNull(() => App.GetAllTopLevelWindows(Automation).FirstOrDefault(x => x.Title == "OnTech Store" || x.Title == "متجر أونتك" || x.FindFirstDescendant(Cf.ByName("Sign out")) is not null),
            TimeSpan.FromSeconds(seconds), TimeSpan.FromMilliseconds(250)).Result;
        return w ?? throw new TimeoutException("Main window did not open. Open: " + string.Join(" | ", App.GetAllTopLevelWindows(Automation).Select(x => x.Title + ": " + AllText(x))));
    }

    public AutomationElement Find(AutomationElement root, string name, ControlType? type = null, int seconds = 10)
    {
        FlaUI.Core.Conditions.ConditionBase cond = type is null ? Cf.ByName(name) : Cf.ByName(name).And(Cf.ByControlType(type.Value));
        var e = Retry.WhileNull(() => root.FindFirstDescendant(cond), TimeSpan.FromSeconds(seconds), TimeSpan.FromMilliseconds(200)).Result;
        return e ?? throw new InvalidOperationException($"'{name}' ({type}) not found in '{root.Name}'");
    }

    public AutomationElement? TryFind(AutomationElement root, string name, ControlType? type = null, int seconds = 3)
    {
        FlaUI.Core.Conditions.ConditionBase cond = type is null ? Cf.ByName(name) : Cf.ByName(name).And(Cf.ByControlType(type.Value));
        return Retry.WhileNull(() => root.FindFirstDescendant(cond), TimeSpan.FromSeconds(seconds), TimeSpan.FromMilliseconds(200)).Result;
    }

    /// <summary>A control by its AutomationId, e.g. "NewOrder_SaveButton".</summary>
    public AutomationElement ById(AutomationElement root, string id, int seconds = 10)
    {
        var e = Retry.WhileNull(() => root.FindFirstDescendant(Cf.ByAutomationId(id)), TimeSpan.FromSeconds(seconds), TimeSpan.FromMilliseconds(200)).Result;
        return e ?? throw new InvalidOperationException($"'{id}' not found in '{root.Name}'");
    }

    public TextBox Box(AutomationElement root, string id) => ById(root, id).AsTextBox();
    public ComboBox Combo(AutomationElement root, string id) => ById(root, id).AsComboBox();
    public Button ButtonById(AutomationElement root, string id) => ById(root, id).AsButton();

    public Button Button(AutomationElement root, string name) => Find(root, name, ControlType.Button).AsButton();
    public TextBox[] Edits(AutomationElement root) => root.FindAllDescendants(Cf.ByControlType(ControlType.Edit)).Select(e => e.AsTextBox()).ToArray();
    public ComboBox[] Combos(AutomationElement root) => root.FindAllDescendants(Cf.ByControlType(ControlType.ComboBox)).Select(e => e.AsComboBox()).ToArray();

    /// <summary>Text of every TextBlock currently shown (banners, summaries).</summary>
    public string AllText(AutomationElement root) =>
        string.Join(" | ", root.FindAllDescendants(Cf.ByControlType(ControlType.Text)).Select(t => t.Name).Where(n => !string.IsNullOrWhiteSpace(n)));

    public void Type(TextBox box, string text)
    {
        box.Focus();
        box.Text = "";
        box.Enter(text);
    }

    public bool WaitText(AutomationElement root, string contains, int seconds = 15) =>
        Retry.WhileFalse(() => AllText(root).Contains(contains), TimeSpan.FromSeconds(seconds), TimeSpan.FromMilliseconds(250)).Result;

    // ---------------- flows ----------------

    public Window SignIn(string user, string password)
    {
        var login = WaitWindow("Sign in");
        Type(Box(login, "Login_UsernameBox"), user);
        var pwd = ById(login, "Login_PasswordBox");
        pwd.Focus(); Keyboard.Type(password);
        ButtonById(login, "Login_SignInButton").Invoke();
        return WaitMain();
    }

    /// <summary>Opens a page from the menu. <paramref name="page"/> is the English title; the item is found by its id Shell_Nav_&lt;Page&gt;.</summary>
    public void Menu(Window main, string page)
    {
        var id = "Shell_Nav_" + page switch
        {
            "New Order" => "NewOrder",
            "Stock In" => "StockIn",
            "Orders Log" => "Orders",
            "Stock In Log" => "StockIns",
            "Items & Categories" => "Catalog",
            _ => page,
        };
        ById(main, id).AsListBoxItem().Select();
        Wait.UntilInputIsProcessed();
    }

    // ---------------- evidence ----------------

    public string Screenshot(string suffix = "")
    {
        var dir = Path.Combine(Audit, "screenshots");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{_test}{suffix}.png");
        try
        {
            var w = App.GetAllTopLevelWindows(Automation).FirstOrDefault();
            (w is not null ? Capture.Element(w) : Capture.Screen()).ToFile(file);
        }
        catch { Capture.Screen().ToFile(file); }
        return file;
    }

    /// <summary>The desktop app writes no log file of its own, so the "app log" at failure time is the server log tail plus the visible texts.</summary>
    public void SaveFailureLog(Exception e)
    {
        var dir = Path.Combine(Audit, "screenshots");
        var server = Path.Combine(Audit, "evidence", "api_sqlserver_stdout.log");
        string tail;
        try
        {
            using var fs = new FileStream(server, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new StreamReader(fs);
            tail = string.Join(Environment.NewLine, r.ReadToEnd().Split('\n').TakeLast(40));
        }
        catch (Exception ex) { tail = "(server log not readable: " + ex.Message + ")"; }
        string texts;
        try { texts = string.Join(Environment.NewLine, App.GetAllTopLevelWindows(Automation).Select(w => $"[{w.Title}] {AllText(w)}")); }
        catch (Exception ex) { texts = "(no windows: " + ex.Message + ")"; }
        File.WriteAllText(Path.Combine(dir, $"{_test}.log"), $"{e}\n\n--- visible text ---\n{texts}\n\n--- server log tail ---\n{tail}");
    }

    public static object? Sql(string sql, params (string, object)[] args)
    {
        using var c = new SqlConnection(SqlServer); c.Open();
        using var cmd = new SqlCommand(sql, c);
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        var r = cmd.ExecuteScalar(); return r is DBNull ? null : r;
    }

    public void Dispose()
    {
        try { App.Close(); if (!App.HasExited) App.Kill(); } catch { }
        try { if (!App.HasExited) App.Kill(); } catch { }
        Automation.Dispose();
        if (File.Exists(SettingsBackup)) File.Copy(SettingsBackup, SettingsFile, overwrite: true);
        else if (File.Exists(SettingsFile)) File.Delete(SettingsFile);
    }
}


