using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MsgDots.Models;

namespace MsgDots;

/// <summary>
/// Captures the WeChat window and locates chat bubbles by pixel-color analysis.
/// Returned Message coordinates are in logical pixels (DIPs).
/// </summary>
static class BubbleDetector
{
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string? cls, string caption);
    [DllImport("user32.dll")] static extern bool   GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool   PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] static extern uint   GetDpiForWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    const uint DEFAULT_DPI = 96;
    static readonly string[] WeChatTitles = ["微信", "WeChat"];

    // ── Public entry point ────────────────────────────────────────────────

    public static List<Message> DetectRecentMessages(int limit = 8)
    {
        var hWnd = FindWeChatWindow();
        if (hWnd == IntPtr.Zero)
            throw new InvalidOperationException("WeChat window not found");

        GetWindowRect(hWnd, out var rect);
        int pw = rect.Right  - rect.Left;
        int ph = rect.Bottom - rect.Top;

        uint dpi = GetDpiForWindow(hWnd);
        if (dpi == 0) dpi = DEFAULT_DPI;
        double scale = dpi / (double)DEFAULT_DPI;

        QMLog.Info($"WeChat: phys {rect.Left},{rect.Top} {pw}x{ph}  " +
                   $"dpi={dpi} scale={scale:F2}  " +
                   $"logical {rect.Left/scale:F0},{rect.Top/scale:F0} " +
                   $"{pw/scale:F0}x{ph/scale:F0}");

        using var bmp = CaptureWindow(hWnd, pw, ph);
        var messages = AnalyzeBitmap(bmp, rect.Left, rect.Top, scale, limit);
        QMLog.Info($"pixel scan found {messages.Count} bubble(s)");

        if (messages.Count == 0)
        {
            QMLog.Info("falling back to stub data");
            messages = StubMessages(rect.Left / scale, rect.Top / scale,
                                    pw / scale, ph / scale, limit);
        }
        return messages;
    }

    // ── Window capture ────────────────────────────────────────────────────

    static IntPtr FindWeChatWindow()
    {
        foreach (var t in WeChatTitles)
        {
            var h = FindWindow(null, t);
            if (h != IntPtr.Zero) return h;
        }
        return IntPtr.Zero;
    }

    static Bitmap CaptureWindow(IntPtr hWnd, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        var hdc = g.GetHdc();
        PrintWindow(hWnd, hdc, 2);   // PW_RENDERFULLCONTENT
        g.ReleaseHdc(hdc);
        return bmp;
    }

    // ── Pixel analysis ────────────────────────────────────────────────────
    //
    // WeChat light theme:
    //   Background : #EDEDED rgb(237,237,237)
    //   Received   : #FFFFFF rgb(255,255,255)  white
    //   Sent       : #95EC69 rgb(149,236,105)  WeChat green
    //
    // Layout exclusion zones (skip WeChat chrome):
    //   Top    12 % — title bar + chat header
    //   Bottom 14 % — message input box
    //   Left   22 % — sidebar + contact list
    //
    // Row classification:
    //   Count white+green pixels in the chat-area x-range.
    //   If ≥ BUBBLE_FILL_RATIO of that range → bubble row.
    //   (Total-count approach tolerates black text inside bubbles.)
    //
    // Band grouping:
    //   Small vertical gaps (≤ GAP_ROWS physical rows) are bridged so that
    //   multi-line bubbles aren't split at the text lines.

    const double BUBBLE_FILL_RATIO = 0.35;   // ≥35 % of row must be bubble color
    const int    GAP_ROWS          = 6;       // bridge gaps shorter than this

    static List<Message> AnalyzeBitmap(
        Bitmap bmp, int physWX, int physWY, double scale, int limit)
    {
        int W = bmp.Width, H = bmp.Height;

        var bmpData = bmp.LockBits(
            new Rectangle(0, 0, W, H),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = bmpData.Stride;
        var px = new byte[stride * H];
        Marshal.Copy(bmpData.Scan0, px, 0, px.Length);
        bmp.UnlockBits(bmpData);

        // Exclusion zones in physical pixels
        int yTop    = (int)(H * 0.12);   // skip title bar + chat header
        int yBot    = (int)(H * 0.86);   // skip input box
        int x0      = (int)(W * 0.22);   // skip sidebar + contact list
        int x1      = W - 4;
        int chatW   = x1 - x0;

        int minBubbleH = (int)(8 * scale);   // min band height in physical px

        // ── Step 1: classify each row ─────────────────────────────────────
        // isBubble[y], and track leftmost / rightmost bubble pixel per row
        var isBubble = new bool[H];
        var rowLeft  = new int[H];
        var rowRight = new int[H];
        var rowGreen = new bool[H];

        for (int y = yTop; y < yBot; y++)
        {
            int baseOff = y * stride;
            int white = 0, green = 0;
            int firstX = x1, lastX = x0;
            bool hasGreen = false;

            for (int x = x0; x < x1; x++)
            {
                int i = baseOff + x * 4;
                byte b = px[i], g = px[i + 1], r = px[i + 2];

                bool isWhite = r >= 248 && g >= 248 && b >= 248;
                // WeChat green: R 128-178, G 215-255, B 78-135
                bool isGreen = r >= 128 && r <= 178 &&
                               g >= 215 &&
                               b >= 78  && b <= 135;

                if (isWhite || isGreen)
                {
                    white += isWhite ? 1 : 0;
                    green += isGreen ? 1 : 0;
                    if (x < firstX) firstX = x;
                    if (x > lastX)  lastX  = x;
                    if (isGreen)    hasGreen = true;
                }
            }

            double fill = (double)(white + green) / chatW;
            if (fill >= BUBBLE_FILL_RATIO)
            {
                isBubble[y] = true;
                rowLeft[y]  = firstX;
                rowRight[y] = lastX;
                rowGreen[y] = green > white / 2;   // majority green → sent
            }
        }

        // ── Step 2: bridge small gaps ─────────────────────────────────────
        for (int y = yTop + 1; y < yBot - 1; y++)
        {
            if (isBubble[y]) continue;
            // Count consecutive non-bubble rows
            int gapEnd = y;
            while (gapEnd < yBot && !isBubble[gapEnd]) gapEnd++;
            int gapLen = gapEnd - y;
            // Bridge if the gap is small and neighbours are both bubble rows
            if (gapLen <= GAP_ROWS && y > yTop && gapEnd < yBot &&
                isBubble[y - 1] && isBubble[gapEnd])
            {
                for (int gy = y; gy < gapEnd; gy++)
                {
                    isBubble[gy] = true;
                    rowLeft[gy]  = rowLeft[y - 1];
                    rowRight[gy] = rowRight[y - 1];
                    rowGreen[gy] = rowGreen[y - 1];
                }
            }
            y = gapEnd - 1;
        }

        // ── Step 3: group rows into bubble bands ──────────────────────────
        var allBands = new List<(int top, int bot, int left, int right, bool isGreen)>();
        int bandTop = -1, bandL = int.MaxValue, bandR = 0;
        bool bandGreen = false;

        for (int y = yTop; y <= yBot; y++)
        {
            bool inBubble = y < yBot && isBubble[y];
            if (inBubble)
            {
                if (bandTop < 0) { bandTop = y; bandGreen = rowGreen[y]; }
                bandL = Math.Min(bandL, rowLeft[y]);
                bandR = Math.Max(bandR, rowRight[y]);
            }
            else if (bandTop >= 0)
            {
                int bh = y - bandTop;
                if (bh >= minBubbleH)
                    allBands.Add((bandTop, y - 1, bandL, bandR, bandGreen));
                bandTop = -1; bandL = int.MaxValue; bandR = 0;
            }
        }

        // ── Step 4: take bottom `limit` bands → logical Message records ───
        int skip = Math.Max(0, allBands.Count - limit);
        var result = new List<Message>();
        for (int i = skip; i < allBands.Count; i++)
        {
            var (top, bot, left, right, isGreen) = allBands[i];
            int lx = (int)((physWX + left)  / scale);
            int ly = (int)((physWY + top)   / scale);
            int lw = (int)((right - left)   / scale);
            int lh = (int)((bot - top + 1)  / scale);
            result.Add(new Message(lx, ly, lw, lh, FromSelf: isGreen));
        }
        result.Reverse();   // newest (bottom of chat) → index 0 = A
        return result;
    }

    // ── Stub fallback ─────────────────────────────────────────────────────

    static List<Message> StubMessages(
        double wx, double wy, double ww, double wh, int limit)
    {
        var result = new List<Message>();
        int chatX  = (int)(wx + ww * 0.30);
        int chatW  = (int)(ww * 0.65);
        int startY = (int)(wy + wh * 0.15);
        int gap    = (int)(wh * 0.65 / limit);
        for (int i = 0; i < limit; i++)
        {
            bool fromSelf = i % 3 == 1;
            int bw = (int)(chatW * (0.35 + (i % 3) * 0.12));
            int bh = 36;
            int bx = fromSelf ? chatX + chatW - bw - 40 : chatX + 40;
            int by = startY + i * gap;
            result.Add(new Message(bx, by, bw, bh, FromSelf: fromSelf));
        }
        result.Reverse();
        return result;
    }
}
