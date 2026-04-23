using System.Windows;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace MsgDots;

/// <summary>
/// Small modal window that captures the next key combination typed by
/// the user and saves it as the new global hotkey.
/// </summary>
partial class HotkeyRecorderWindow : Window
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);

    private HotkeyDef? _pending;
    private readonly KeyboardHook _hook;
    private const string RequireModifierText = "必须至少包含 Ctrl / Alt / Shift 之一";

    public HotkeyRecorderWindow()
    {
        InitializeComponent();
        _hook = new KeyboardHook { ShouldSwallow = RecordKey };
        Loaded += (_, _) => _hook.Install();
        Closed += (_, _) => _hook.Uninstall();
    }

    // ── Key recording ────────────────────────────────────────────────────────

    private bool RecordKey(Keys key)
    {
        // Don't record bare modifier keys
        if (key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
                or Keys.ShiftKey   or Keys.LShiftKey   or Keys.RShiftKey
                or Keys.Menu       or Keys.LMenu        or Keys.RMenu
                or Keys.LWin       or Keys.RWin)
            return false;

        // Escape cancels without saving
        if (key == Keys.Escape)
        {
            Dispatcher.Invoke(Close);
            return true;
        }

        // Snapshot modifier state directly from Win32. In a low-level
        // keyboard hook callback this is more reliable than
        // Control.ModifierKeys, especially for Alt-combos.
        Keys mods = Keys.None;
        if (IsDown(Keys.ControlKey) || IsDown(Keys.LControlKey) || IsDown(Keys.RControlKey))
            mods |= Keys.Control;
        if (IsDown(Keys.Menu) || IsDown(Keys.LMenu) || IsDown(Keys.RMenu))
            mods |= Keys.Alt;
        if (IsDown(Keys.ShiftKey) || IsDown(Keys.LShiftKey) || IsDown(Keys.RShiftKey))
            mods |= Keys.Shift;

        if (mods == Keys.None)
        {
            Dispatcher.Invoke(() =>
            {
                _pending = null;
                PreviewText.Text   = RequireModifierText;
                ApplyBtn.IsEnabled = false;
            });
            return true;
        }

        _pending = new HotkeyDef(key, mods);

        Dispatcher.Invoke(() =>
        {
            PreviewText.Text   = _pending.Display;
            ApplyBtn.IsEnabled = true;
        });

        return true;   // swallow — don't let it reach WeChat
    }

    private static bool IsDown(Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

    // ── Buttons ──────────────────────────────────────────────────────────────

    private void ApplyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_pending != null)
            HotkeyConfig.Save(_pending);
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => Close();
}
