using BetterPrompt.Models;
using System.IO;
using System.Text.Json;

namespace BetterPrompt.Services;

public class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BetterPrompt", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                MergeDefaults(loaded);
                return loaded;
            }
        }
        catch { }
        return new AppSettings();
    }

    // Ensures new defaults added in later versions are present in existing installs.
    private static void MergeDefaults(AppSettings settings)
    {
        var defaults = new AppSettings();
        foreach (var dir in defaults.ExcludedDirectories)
            if (!settings.ExcludedDirectories.Contains(dir, StringComparer.OrdinalIgnoreCase))
                settings.ExcludedDirectories.Add(dir);
        foreach (var ext in defaults.CodeExtensions)
            if (!settings.CodeExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                settings.CodeExtensions.Add(ext);
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
