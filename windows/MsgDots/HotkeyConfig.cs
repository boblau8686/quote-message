using System.Windows.Forms;

namespace MsgDots;

/// <summary>
/// Hotkey definition + persistence via QMSettings (JSON in %APPDATA%).
/// Mirrors macOS HotkeyConfig.swift.
/// </summary>
public record HotkeyDef(Keys Key, Keys Modifiers)
{
    public static readonly HotkeyDef Default = new(Keys.Q, Keys.Control);

    public string Display
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(Keys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(Keys.Alt))     parts.Add("Alt");
            if (Modifiers.HasFlag(Keys.Shift))   parts.Add("Shift");
            parts.Add(Key.ToString());
            return string.Join("+", parts);
        }
    }
}

static class HotkeyConfig
{
    private const string KeyCode = "qm.hotkey.keyCode";
    private const string KeyMods = "qm.hotkey.modifiers";

    public static event Action<HotkeyDef>? Changed;

    public static HotkeyDef Current
    {
        get
        {
            var k = QMSettings.Get(KeyCode);
            var m = QMSettings.Get(KeyMods);
            if (k != null && m != null &&
                Enum.TryParse<Keys>(k, out var key) &&
                Enum.TryParse<Keys>(m, out var mods))
                return new HotkeyDef(key, mods);
            return HotkeyDef.Default;
        }
    }

    public static void Save(HotkeyDef hk)
    {
        QMSettings.Set(KeyCode, hk.Key.ToString());
        QMSettings.Set(KeyMods, hk.Modifiers.ToString());
        QMSettings.Save();
        Changed?.Invoke(hk);
        QMLog.Info($"hotkey saved: {hk.Display}");
    }

    public static void ResetToDefault() => Save(HotkeyDef.Default);
}
