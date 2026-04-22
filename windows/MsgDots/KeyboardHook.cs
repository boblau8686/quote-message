using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MsgDots;

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL) that swallows specific keys
/// while the overlay is visible.
/// Mirrors macOS KeyCaptureTap / CGEventTap logic.
/// </summary>
sealed class KeyboardHook : IDisposable
{
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool   UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? lpModuleName);

    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN     = 0x0100;

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    /// <summary>
    /// Called for every key press while the hook is installed.
    /// Return true to swallow the key (prevent it reaching any other app).
    /// </summary>
    public Func<Keys, bool>? ShouldSwallow;

    private IntPtr _hook = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;   // keep reference alive

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        using var process = Process.GetCurrentProcess();
        using var module  = process.MainModule!;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc,
                    GetModuleHandle(module.ModuleName), 0);
        QMLog.Info(_hook != IntPtr.Zero ? "keyboard hook installed" : "keyboard hook FAILED");
    }

    public void Uninstall()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            QMLog.Info("keyboard hook uninstalled");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WM_KEYDOWN)
        {
            var kb  = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var key = (Keys)kb.vkCode;
            if (ShouldSwallow?.Invoke(key) == true)
                return (IntPtr)1;   // swallow — do not pass to other apps
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
