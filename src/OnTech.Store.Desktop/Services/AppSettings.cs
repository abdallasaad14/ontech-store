using System.IO;
using System.Text.Json;

namespace OnTech.Store.Desktop.Services;

/// <summary>Per-PC settings, saved in %LocalAppData%\OnTechStore\settings.json.</summary>
public sealed class AppSettings
{
    public string ServerUrl { get; set; } = "http://localhost:5080";
    public string? LastEmail { get; set; }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OnTechStore", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception) { /* a broken file falls back to defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { /* settings are a convenience; never block the app */ }
    }
}
