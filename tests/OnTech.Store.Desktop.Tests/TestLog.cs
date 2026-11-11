using System.Runtime.CompilerServices;
using OnTech.Store.Desktop.Services;

namespace OnTech.Store.Desktop.Tests;

/// <summary>Points the app's log at a temp folder before any test runs, so tests never write to the user's real log folder.</summary>
internal static class TestLog
{
    public static readonly string Folder = Path.Combine(Path.GetTempPath(), "OnTechStore.Desktop.Tests", "logs-" + Environment.ProcessId);

    [ModuleInitializer]
    internal static void Init() => Log.Current = new FileLog(Folder);
}
