using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

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
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string? cls, string caption);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    const uint DEFAULT_DPI = 96;
    static readonly string[] WeChatTitles = ["微信", "WeChat"];

    const int SM_XVIRTUALSCREEN  = 76;
    const int SM_YVIRTUALSCREEN  = 77;
    const int SM_CXVIRTUALSCREEN = 78;
    const int SM_CYVIRTUALSCREEN = 79;

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

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    const uint INPUT_MOUSE           = 0;
    const uint MOUSEEVENTF_MOVE      = 0x0001;
    const uint MOUSEEVENTF_LEFTDOWN  = 0x0002;
    const uint MOUSEEVENTF_LEFTUP    = 0x0004;
    const uint MOUSEEVENTF_ABSOLUTE  = 0x8000;
    const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    const uint MOUSEEVENTF_RIGHTUP   = 0x0010;

    /// <summary>
    /// Move the cursor to <paramref name="pt"/>, right-click, then click
    /// WeChat's "引用" menu item.
    /// </summary>
    public static void QuoteAt(Point pt)
    {
        var hWnd = FindWeChatWindow();
        if (hWnd == IntPtr.Zero)
            throw new InvalidOperationException("WeChat window not found");

        GetWindowThreadProcessId(hWnd, out var pid);
        var baseline = SnapshotWindowHandles(pid);

        SetForegroundWindow(hWnd);
        Thread.Sleep(120);

        var physPt = DipToPhysical(pt, hWnd);
        PostRightClick(physPt);
        QMLog.Info($"QuoteAt: right-click sent to dip=({pt.X},{pt.Y}) phys=({physPt.X},{physPt.Y})");

        var popup = FindPopupWindow(pid, baseline, timeoutMs: 500);
        if (popup == null)
            throw new InvalidOperationException("context menu did not appear");

        var clickPt = SecondToLastItemCenter(popup.Value);
        QMLog.Info($"QuoteAt: popup bounds={Describe(popup.Value)} click=({clickPt.X},{clickPt.Y})");
        PostLeftClick(clickPt);
    }

    static IntPtr FindWeChatWindow()
    {
        foreach (var title in WeChatTitles)
        {
            var hWnd = FindWindow(null, title);
            if (hWnd != IntPtr.Zero) return hWnd;
        }
        return IntPtr.Zero;
    }

    static Point DipToPhysical(Point pt, IntPtr referenceWindow)
    {
        uint dpi = GetDpiForWindow(referenceWindow);
        if (dpi == 0) dpi = DEFAULT_DPI;
        double scale = dpi / (double)DEFAULT_DPI;
        return new Point(
            (int)Math.Round(pt.X * scale),
            (int)Math.Round(pt.Y * scale));
    }

    static HashSet<IntPtr> SnapshotWindowHandles(uint pid)
    {
        var handles = new HashSet<IntPtr>();
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var ownerPid);
            if (ownerPid == pid && IsWindowVisible(hWnd))
                handles.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return handles;
    }

    static RECT? FindPopupWindow(uint pid, HashSet<IntPtr> baseline, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            RECT? best = null;
            int bestArea = 0;

            EnumWindows((hWnd, _) =>
            {
                if (baseline.Contains(hWnd) || !IsWindowVisible(hWnd))
                    return true;

                GetWindowThreadProcessId(hWnd, out var ownerPid);
                if (ownerPid != pid)
                    return true;

                if (!GetWindowRect(hWnd, out var rect))
                    return true;

                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;
                if (w <= 40 || w >= 800 || h <= 60 || h >= 1200)
                    return true;

                int area = w * h;
                if (area > bestArea)
                {
                    best = rect;
                    bestArea = area;
                }
                return true;
            }, IntPtr.Zero);

            if (best != null)
                return best;

            Thread.Sleep(30);
        }
        return null;
    }

    static Point SecondToLastItemCenter(RECT popup)
    {
        const double nItems = 10;
        const double pad = 3;
        const double bottomPad = 36;

        int w = popup.Right - popup.Left;
        int h = popup.Bottom - popup.Top;
        double menuH = Math.Max(40, h - bottomPad);
        double itemH = Math.Max(16, (menuH - 2 * pad) / nItems);

        int x = popup.Left + w / 2;
        int y = (int)Math.Round(popup.Top + h - bottomPad - pad - itemH * 1.5);
        y = Math.Max((int)(popup.Top + pad), Math.Min(y, (int)(popup.Bottom - pad)));
        return new Point(x, y);
    }

    static void PostRightClick(Point pt) =>
        PostMouse(pt, MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);

    static void PostLeftClick(Point pt) =>
        PostMouse(pt, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);

    static void PostMouse(Point pt, uint downFlag, uint upFlag)
    {
        var (absX, absY) = NormalizeToVirtualDesktop(pt);
        var common = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
        var inputs = new INPUT[]
        {
            new()
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT { dx = absX, dy = absY, dwFlags = MOUSEEVENTF_MOVE | common }
            },
            new()
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT { dx = absX, dy = absY, dwFlags = downFlag | common }
            },
            new()
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT { dx = absX, dy = absY, dwFlags = upFlag | common }
            },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    static (int X, int Y) NormalizeToVirtualDesktop(Point pt)
    {
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        int vh = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));

        int x = (int)Math.Round((pt.X - vx) * 65535.0 / Math.Max(1, vw - 1));
        int y = (int)Math.Round((pt.Y - vy) * 65535.0 / Math.Max(1, vh - 1));
        return (Math.Clamp(x, 0, 65535), Math.Clamp(y, 0, 65535));
    }

    static string Describe(RECT rect) =>
        $"({rect.Left},{rect.Top}) {rect.Right - rect.Left}x{rect.Bottom - rect.Top}";
}
