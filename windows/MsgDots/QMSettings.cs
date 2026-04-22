using System.IO;
using System.Text.Json;

namespace MsgDots;

/// <summary>
/// Simple JSON-backed key-value store that persists to
/// %APPDATA%\MsgDots\settings.json.
/// Replaces Properties.Settings.Default for this project.
/// </summary>
static class QMSettings
{
    private static readonly string _path;
    private static Dictionary<string, string> _store = new();

    static QMSettings()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MsgDots");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Load();
    }

    public static string? Get(string key) =>
        _store.TryGetValue(key, out var v) ? v : null;

    public static void Set(string key, string value) =>
        _store[key] = value;

    public static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                _store, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch (Exception ex)
        {
            QMLog.Info($"settings save failed: {ex.Message}");
        }
    }

    private static void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                _store = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                         ?? new Dictionary<string, string>();
            }
        }
        catch (Exception ex)
        {
            QMLog.Info($"settings load failed: {ex.Message}");
        }
    }
}
