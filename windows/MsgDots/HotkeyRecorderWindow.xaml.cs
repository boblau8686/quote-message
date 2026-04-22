using System.Windows;
using System.Windows.Forms;

namespace MsgDots;

/// <summary>
/// Small modal window that captures the next key combination typed by
/// the user and saves it as the new global hotkey.
/// </summary>
partial class HotkeyRecorderWindow : Window
{
    private HotkeyDef? _pending;
    private readonly KeyboardHook _hook;

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

        // Snapshot modifier state at the moment the key fires
        Keys mods = Keys.None;
        if ((System.Windows.Forms.Control.ModifierKeys & Keys.Control) != 0)
            mods |= Keys.Control;
        if ((System.Windows.Forms.Control.ModifierKeys & Keys.Alt) != 0)
            mods |= Keys.Alt;
        if ((System.Windows.Forms.Control.ModifierKeys & Keys.Shift) != 0)
            mods |= Keys.Shift;

        _pending = new HotkeyDef(key, mods);

        Dispatcher.Invoke(() =>
        {
            PreviewText.Text   = _pending.Display;
            ApplyBtn.IsEnabled = true;
        });

        return true;   // swallow — don't let it reach WeChat
    }

    // ── Buttons ──────────────────────────────────────────────────────────────

    private void ApplyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_pending != null)
            HotkeyConfig.Save(_pending);
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => Close();
}
