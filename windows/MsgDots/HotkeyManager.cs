using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace MsgDots;

/// <summary>
/// Registers / unregisters a system-wide hotkey via RegisterHotKey.
/// Uses a hidden HwndSource to receive WM_HOTKEY messages.
/// Mirrors macOS NSEvent.addGlobalMonitorForEvents usage.
/// </summary>
sealed class HotkeyManager : IDisposable
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    const int WM_HOTKEY = 0x0312;
    const int HOTKEY_ID = 1;

    // Win32 modifier flags
    const uint MOD_ALT     = 0x0001;
    const uint MOD_CONTROL = 0x0002;
    const uint MOD_SHIFT   = 0x0004;
    const uint MOD_WIN     = 0x0008;
    const uint MOD_NOREPEAT = 0x4000;

    private readonly Action _onFired;
    private HwndSource? _source;
    private bool _registered;

    public HotkeyManager(Action onFired)
    {
        _onFired = onFired;

        // Create a minimal hidden window to receive WM_HOTKEY
        var p = new HwndSourceParameters("MsgDots_HotkeyHost")
        {
            Width = 0, Height = 0,
            WindowStyle = 0x800000,   // WS_POPUP (invisible)
            ExtendedWindowStyle = 0,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public bool Register(HotkeyDef hk)
    {
        Unregister();
        uint mods = BuildMods(hk.Modifiers) | MOD_NOREPEAT;
        _registered = RegisterHotKey(_source!.Handle, HOTKEY_ID, mods, (uint)hk.Key);
        QMLog.Info(_registered
            ? $"hotkey registered: {hk.Display}"
            : $"hotkey registration failed: {hk.Display}");
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _source != null)
            UnregisterHotKey(_source.Handle, HOTKEY_ID);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            handled = true;
            Application.Current.Dispatcher.Invoke(_onFired);
        }
        return IntPtr.Zero;
    }

    private static uint BuildMods(Keys mods)
    {
        uint r = 0;
        if (mods.HasFlag(Keys.Control)) r |= MOD_CONTROL;
        if (mods.HasFlag(Keys.Alt))     r |= MOD_ALT;
        if (mods.HasFlag(Keys.Shift))   r |= MOD_SHIFT;
        return r;
    }

    public void Dispose()
    {
        Unregister();
        _source?.Dispose();
        _source = null;
    }
}
