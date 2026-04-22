using System.Drawing;
using System.Runtime.InteropServices;

namespace MsgDots;

/// <summary>
/// Simulates a right-click at the given screen coordinate so the
/// WeChat "Quote / 引用" context menu item appears.
/// Mirrors macOS QuoteAction.swift (CGEvent mouse simulation).
/// </summary>
static class QuoteAction
{
    [DllImport("user32.dll")] static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] static extern int  GetSystemMetrics(int nIndex);

    const int SM_CXSCREEN = 0;
    const int SM_CYSCREEN = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
        // Pad to full INPUT union size (keyboard / hardware variants are larger on 64-bit).
        // MOUSEINPUT is already the largest member on Win32 so no extra padding needed here.
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int  dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    const uint INPUT_MOUSE           = 0;
    const uint MOUSEEVENTF_MOVE      = 0x0001;
    const uint MOUSEEVENTF_ABSOLUTE  = 0x8000;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    const uint MOUSEEVENTF_RIGHTUP   = 0x0010;

    /// <summary>
    /// Move the cursor to <paramref name="pt"/> and send a right-click.
    /// The WeChat context menu will appear; the user then clicks "引用".
    /// </summary>
    public static void QuoteAt(Point pt)
    {
        int screenW = GetSystemMetrics(SM_CXSCREEN);
        int screenH = GetSystemMetrics(SM_CYSCREEN);

        // Normalise to 0–65535 as required by MOUSEEVENTF_ABSOLUTE
        int absX = (int)((pt.X + 0.5) * 65536 / screenW);
        int absY = (int)((pt.Y + 0.5) * 65536 / screenH);

        var inputs = new INPUT[]
        {
            // 1. Move cursor to bubble
            new INPUT
            {
                type = INPUT_MOUSE,
                mi   = new MOUSEINPUT
                {
                    dx = absX, dy = absY,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE
                }
            },
            // 2. Right-button down
            new INPUT
            {
                type = INPUT_MOUSE,
                mi   = new MOUSEINPUT
                {
                    dx = absX, dy = absY,
                    dwFlags = MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_ABSOLUTE
                }
            },
            // 3. Right-button up  →  context menu opens
            new INPUT
            {
                type = INPUT_MOUSE,
                mi   = new MOUSEINPUT
                {
                    dx = absX, dy = absY,
                    dwFlags = MOUSEEVENTF_RIGHTUP | MOUSEEVENTF_ABSOLUTE
                }
            },
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        QMLog.Info($"QuoteAt: right-click sent to ({pt.X},{pt.Y})");
    }
}
