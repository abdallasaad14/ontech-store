using System.Globalization;
using System.IO;
using System.Text;

namespace OnTech.Store.Desktop.Services;

/// <summary>
/// The app's log: one file per day in %LocalAppData%\OnTechStore\logs (OnTechStore-yyyyMMdd.log), the newest 14 kept (M-5).
/// Never write passwords or tokens here. Logging never throws: a full disk or a locked file must not stop the app.
/// </summary>
public static class Log
{
    public static readonly string DefaultFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OnTechStore", "logs");

    /// <summary>The log used by the app. Tests replace it with one in a temp folder.</summary>
    public static FileLog Current { get; set; } = new(DefaultFolder);

    public static void Info(string message) => Current.Write("INF", message, null);
    public static void Warn(string message, Exception? ex = null) => Current.Write("WRN", message, ex);
    public static void Error(string message, Exception? ex = null) => Current.Write("ERR", message, ex);
}

public sealed class FileLog(string folder, Func<DateTime>? clock = null, int keepFiles = 14)
{
    public const string Prefix = "OnTechStore-";
    public const string Extension = ".log";

    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.Now);
    private readonly object _lock = new();
    private DateTime? _day;

    public string Folder { get; } = folder;
    public int KeepFiles { get; } = keepFiles;

    public string FileFor(DateTime day) => Path.Combine(Folder, Prefix + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + Extension);

    public void Write(string level, string message, Exception? ex)
    {
        try
        {
            var now = _clock();
            var line = new StringBuilder()
                .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(" [").Append(level).Append("] ")
                .Append(message);
            if (ex is not null) line.AppendLine().Append(ex); // type, message, stack trace and inner exceptions
            lock (_lock)
            {
                if (_day != now.Date)
                {
                    Directory.CreateDirectory(Folder);
                    _day = now.Date;
                    DeleteOldFiles();
                }
                File.AppendAllText(FileFor(now), line.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch (Exception) { /* logging must never break the app */ }
    }

    /// <summary>Keeps the newest <see cref="KeepFiles"/> daily files (today's included) and deletes the rest.</summary>
    public void DeleteOldFiles()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            var today = Path.GetFileName(FileFor(_clock()));
            var files = Directory.GetFiles(Folder, Prefix + "*" + Extension)
                .Select(Path.GetFileName)
                .Append(today) // today's file counts even before its first line is written
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var old in files.Skip(KeepFiles))
                try { File.Delete(Path.Combine(Folder, old!)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception) { /* best effort */ }
    }
}

/// <summary>The app-wide handlers for errors nobody caught. Each one logs the full exception first.</summary>
public static class CrashHandlers
{
    public static void LogDispatcherException(Exception ex) => Log.Error("Unhandled error on the UI thread", ex);

    public static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("Unobserved task exception", e.Exception);
        e.SetObserved(); // logged; do not let it end the process
    }

    public static void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e) =>
        Log.Error(e.IsTerminating ? "Unhandled error, the app is closing" : "Unhandled error", e.ExceptionObject as Exception
            ?? new Exception(e.ExceptionObject?.ToString()));
}
