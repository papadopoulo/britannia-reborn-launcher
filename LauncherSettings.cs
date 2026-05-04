using System;
using System.IO;
using System.Text.Json;

namespace BritanniaReborn;

// Persistencia simple de settings (UO path, último username, save password).
// Guardado en %APPDATA%\BritanniaReborn\settings.json — limpio, no hay que
// tocar registro Windows.

internal sealed class LauncherSettingsData
{
    public string UoPath { get; set; } = "";
    public string LastUsername { get; set; } = "";
    public string SavedPassword { get; set; } = "";
    public bool SavePassword { get; set; }
}

internal static class LauncherSettings
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BritanniaReborn");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    public static LauncherSettingsData Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new LauncherSettingsData();
            var json = File.ReadAllText(SettingsFile);
            return JsonSerializer.Deserialize<LauncherSettingsData>(json) ?? new LauncherSettingsData();
        }
        catch
        {
            return new LauncherSettingsData();
        }
    }

    public static void Save(LauncherSettingsData data)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }

    public static string LoadUoPath() => Load().UoPath;
    public static void SaveUoPath(string path)
    {
        var d = Load();
        d.UoPath = path;
        Save(d);
    }
}
