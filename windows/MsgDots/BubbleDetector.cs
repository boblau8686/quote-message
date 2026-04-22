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

    public static List<Message> DetectRecentMessages(int limit = 26)
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
        for (int i = 0; i < messages.Count; i++)
            QMLog.Info($"bubble[{i}] x={messages[i].X} y={messages[i].Y} " +
                       $"w={messages[i].Width} h={messages[i].Height} self={messages[i].FromSelf}");
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
    // Mirrors the macOS detector:
    //   1. Crop away WeChat chrome (sidebar/header/input).
    //   2. Estimate the chat background colour by histogram mode.
    //   3. Mark pixels that differ from the background.
    //   4. Group rows with non-background pixels into vertical bands.
    //   5. Resolve each band to the widest horizontal run, which is normally
    //      the message bubble rather than the avatar/timestamp.

    const double SidebarWidthDip = 360;
    const double HeaderHeightDip = 58;
    const double InputHeightDip  = 130;
    const double RightMarginDip  = 18;
    const double EdgeMarginDip   = 4;

    const int BubbleBGThreshold      = 24;
    const int NeutralBubbleMinDelta  = 9;
    const int NeutralBubbleMaxDelta  = 42;
    const int NeutralBubbleMaxSpread = 6;
    const int BubbleGapClosePx       = 8;
    const int BubbleMinHpx           = 24;
    const int BubbleMinWpx           = 36;
    const double MaxBubbleWidthRatio = 0.72;
    const double CenterRatioThreshold = 0.08;
    const double MaxCenterWidthRatio  = 0.40;
    const int SentGreenDelta         = 6;

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

        int cropLeft   = (int)(SidebarWidthDip * scale);
        int cropRight  = (int)(W - RightMarginDip * scale);
        int cropTop    = (int)((HeaderHeightDip + EdgeMarginDip) * scale);
        int cropBottom = (int)(H - (InputHeightDip + EdgeMarginDip) * scale);

        if (cropRight <= cropLeft || cropBottom <= cropTop)
            return new List<Message>();

        int cropW = cropRight - cropLeft;
        int cropH = cropBottom - cropTop;
        QMLog.Info($"crop: x={cropLeft} y={cropTop} {cropW}x{cropH}");

        var (bgR, bgG, bgB, sampled) = EstimateBackground(px, stride, cropLeft, cropTop, cropW, cropH);
        QMLog.Info($"bg estimate rgb=({bgR},{bgG},{bgB}) sampled={sampled}");

        var mask = BuildMask(px, stride, cropLeft, cropTop, cropW, cropH, bgR, bgG, bgB);
        ScrubEdgeColumns(mask, cropW, cropH);

        var bands = FindVerticalBands(mask, cropW, cropH);
        QMLog.Info($"found {bands.Count} vertical bands");

        var bubbles = ResolveBubbles(px, stride, mask, cropLeft, cropTop, cropW, cropH, bands);
        QMLog.Info($"after filtering: {bubbles.Count} bubbles");

        var newest = bubbles
            .OrderByDescending(b => b.Bottom)
            .Take(limit)
            .Select(b =>
            {
                int lx = (int)((physWX + cropLeft + b.Left) / scale);
                int ly = (int)((physWY + cropTop + b.Top) / scale);
                int lw = Math.Max(1, (int)((b.Right - b.Left + 1) / scale));
                int lh = Math.Max(1, (int)((b.Bottom - b.Top + 1) / scale));
                return new Message(lx, ly, lw, lh, FromSelf: b.FromSelf);
            })
            .ToList();

        return newest;
    }

    record struct Bubble(int Left, int Right, int Top, int Bottom, bool FromSelf);

    static (int R, int G, int B, int Sampled) EstimateBackground(
        byte[] px, int stride, int cropLeft, int cropTop, int cropW, int cropH)
    {
        var histR = new int[256];
        var histG = new int[256];
        var histB = new int[256];

        int total = cropW * cropH;
        int step = Math.Max(1, total / 50_000);
        int sampled = 0;

        for (int idx = 0; idx < total; idx += step)
        {
            int cy = idx / cropW;
            int cx = idx % cropW;
            int off = (cropTop + cy) * stride + (cropLeft + cx) * 4;
            byte b = px[off], g = px[off + 1], r = px[off + 2];
            histR[r]++;
            histG[g]++;
            histB[b]++;
            sampled++;
        }

        return (ArgMax(histR), ArgMax(histG), ArgMax(histB), sampled);
    }

    static bool[] BuildMask(
        byte[] px, int stride, int cropLeft, int cropTop, int cropW, int cropH,
        int bgR, int bgG, int bgB)
    {
        var mask = new bool[cropW * cropH];
        for (int cy = 0; cy < cropH; cy++)
        {
            int rowOff = (cropTop + cy) * stride + cropLeft * 4;
            for (int cx = 0; cx < cropW; cx++)
            {
                int off = rowOff + cx * 4;
                byte b = px[off], g = px[off + 1], r = px[off + 2];
                int delta = Math.Abs(r - bgR) + Math.Abs(g - bgG) + Math.Abs(b - bgB);
                mask[cy * cropW + cx] =
                    delta > BubbleBGThreshold ||
                    LooksLikeNeutralBubbleFill(r, g, b, bgR, bgG, bgB, delta);
            }
        }
        return mask;
    }

    static bool LooksLikeNeutralBubbleFill(
        byte r, byte g, byte b,
        int bgR, int bgG, int bgB,
        int delta)
    {
        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        if (max - min > NeutralBubbleMaxSpread)
            return false;

        if (max < 232)
            return false;

        // Windows WeChat can render the chat background as rgb(250,250,250)
        // and received bubbles as very light grey/white, so the total RGB
        // delta can be below the generic threshold.  Restrict this path to
        // low-saturation near-background fills to avoid swallowing text/icons.
        if (delta < NeutralBubbleMinDelta || delta > NeutralBubbleMaxDelta)
            return false;

        int bgAvg = (bgR + bgG + bgB) / 3;
        int avg = (r + g + b) / 3;
        return Math.Abs(avg - bgAvg) >= 3;
    }

    static void ScrubEdgeColumns(bool[] mask, int cropW, int cropH)
    {
        int edgeMargin = Math.Min(20, cropW / 40);
        for (int x = 0; x < edgeMargin; x++) ScrubColumn(mask, cropW, cropH, x);
        for (int x = Math.Max(0, cropW - edgeMargin); x < cropW; x++) ScrubColumn(mask, cropW, cropH, x);
    }

    static void ScrubColumn(bool[] mask, int cropW, int cropH, int x)
    {
        int hits = 0;
        for (int y = 0; y < cropH; y++)
            if (mask[y * cropW + x]) hits++;

        if ((double)hits / Math.Max(1, cropH) <= 0.08)
            return;

        for (int y = 0; y < cropH; y++)
            mask[y * cropW + x] = false;
    }

    static List<(int Top, int Bottom)> FindVerticalBands(bool[] mask, int cropW, int cropH)
    {
        var rowHas = new bool[cropH];
        for (int y = 0; y < cropH; y++)
        {
            int baseIdx = y * cropW;
            for (int x = 0; x < cropW; x++)
            {
                if (!mask[baseIdx + x]) continue;
                rowHas[y] = true;
                break;
            }
        }

        var bands = new List<(int Top, int Bottom)>();
        int i = 0;
        while (i < cropH)
        {
            if (!rowHas[i]) { i++; continue; }

            int start = i;
            int end = i;
            int gap = 0;
            while (i < cropH)
            {
                if (rowHas[i])
                {
                    end = i;
                    gap = 0;
                }
                else
                {
                    gap++;
                    if (gap > BubbleGapClosePx) break;
                }
                i++;
            }
            bands.Add((start, end));
        }
        return bands;
    }

    static List<Bubble> ResolveBubbles(
        byte[] px, int stride, bool[] mask,
        int cropLeft, int cropTop, int cropW, int cropH,
        List<(int Top, int Bottom)> bands)
    {
        var result = new List<Bubble>();
        int chatCenterX = cropW / 2;

        foreach (var (top, bottom) in bands)
        {
            int bandH = bottom - top + 1;
            if (bandH < BubbleMinHpx) continue;

            var (width, left, right) = BestBubbleRunInBand(mask, cropW, top, bottom);
            if (width < BubbleMinWpx) continue;

            int midX = (left + right) / 2;
            if (Math.Abs(midX - chatCenterX) < cropW * CenterRatioThreshold &&
                width < cropW * MaxCenterWidthRatio)
                continue;

            var (r, g, b) = MedianPatch(px, stride, cropLeft, cropTop, cropW, cropH, left, right, top, bottom);
            bool isGreen = g > r + SentGreenDelta && g > b + SentGreenDelta;
            bool posRight = left > cropW * 0.45 && cropW - 1 - right < left;
            bool fromSelf = isGreen || posRight;

            result.Add(new Bubble(left, right, top, bottom, fromSelf));
        }
        return result;
    }

    static bool LooksLikeLeftAvatarRun(int width, int left, int cropW)
    {
        // A received-message avatar lives at the far left of the chat crop and
        // is roughly square. If a white bubble is partially missed, the avatar
        // can otherwise become the "widest run" and put the label on the avatar.
        return left < Math.Min(150, cropW * 0.10) && width <= 150;
    }

    static int ArgMax(int[] hist)
    {
        int best = 0, bestIdx = 0;
        for (int i = 0; i < hist.Length; i++)
        {
            if (hist[i] <= best) continue;
            best = hist[i];
            bestIdx = i;
        }
        return bestIdx;
    }

    static (int Width, int Left, int Right) BestBubbleRunInBand(
        bool[] mask,
        int cropW,
        int top,
        int bottom)
    {
        var best = (Width: 0, Left: 0, Right: 0);

        for (int y = top; y <= bottom; y++)
        {
            foreach (var run in HorizontalRuns(mask, cropW, y))
            {
                if (run.Width < BubbleMinWpx)
                    continue;
                if (run.Width > cropW * MaxBubbleWidthRatio)
                    continue;
                if (LooksLikeLeftAvatarRun(run.Width, run.Left, cropW))
                    continue;
                if (run.Width > best.Width)
                    best = run;
            }
        }

        return best;
    }

    static List<(int Width, int Left, int Right)> HorizontalRuns(bool[] mask, int cropW, int y)
    {
        var runs = new List<(int Width, int Left, int Right)>();
        int baseIdx = y * cropW;
        int curStart = -1, curEnd = -1;
        for (int x = 0; x < cropW; x++)
        {
            if (mask[baseIdx + x])
            {
                if (curStart < 0) curStart = x;
                curEnd = x;
                continue;
            }

            if (curStart < 0) continue;
            int width = curEnd - curStart + 1;
            runs.Add((width, curStart, curEnd));
            curStart = -1;
        }

        if (curStart >= 0)
        {
            int width = curEnd - curStart + 1;
            runs.Add((width, curStart, curEnd));
        }

        return runs;
    }

    static (int R, int G, int B) MedianPatch(
        byte[] px, int stride,
        int cropLeft, int cropTop, int cropW, int cropH,
        int left, int right, int top, int bottom)
    {
        int cx = (left + right) / 2;
        int cy = (top + bottom) / 2;
        int x0 = Math.Max(0, cx - 2), x1 = Math.Min(cropW, cx + 3);
        int y0 = Math.Max(0, cy - 2), y1 = Math.Min(cropH, cy + 3);

        var rs = new List<int>();
        var gs = new List<int>();
        var bs = new List<int>();

        for (int y = y0; y < y1; y++)
        {
            int rowOff = (cropTop + y) * stride + cropLeft * 4;
            for (int x = x0; x < x1; x++)
            {
                int off = rowOff + x * 4;
                bs.Add(px[off]);
                gs.Add(px[off + 1]);
                rs.Add(px[off + 2]);
            }
        }

        return (Median(rs), Median(gs), Median(bs));
    }

    static int Median(List<int> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        return values[values.Count / 2];
    }
}
