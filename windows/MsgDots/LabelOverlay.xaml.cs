using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Forms;

namespace MsgDots;

// ── Outcome types ──────────────────────────────────────────────────────────
abstract record OverlayOutcome;
record CancelledOutcome : OverlayOutcome;
record PickedOutcome(char Letter, Message Message) : OverlayOutcome;

/// <summary>
/// Full-screen transparent overlay that paints red circles + letters
/// next to each detected bubble, and swallows overlay letter / Escape key presses.
/// Mirrors macOS LabelOverlay.swift.
/// </summary>
partial class LabelOverlay : Window
{
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int nIndex);

    const int GWL_EXSTYLE       = -20;
    const int WS_EX_TRANSPARENT = 0x00000020;

    static readonly Brush LabelFill = new SolidColorBrush(Color.FromRgb(229, 57, 53));
    static readonly Brush LabelText = Brushes.White;
    static readonly char[] LabelLetters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
    const double CircleR = 14;

    private readonly List<Message> _messages;
    private readonly Action<OverlayOutcome> _completion;
    private readonly KeyboardHook _hook;
    private readonly double _originX;
    private readonly double _originY;

    internal LabelOverlay(List<Message> messages, Action<OverlayOutcome> completion)
    {
        _messages   = messages;
        _completion = completion;
        _hook       = new KeyboardHook { ShouldSwallow = OnKey };

        InitializeComponent();
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left   = SystemParameters.VirtualScreenLeft;
        Top    = SystemParameters.VirtualScreenTop;
        Width  = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        _originX = Left;
        _originY = Top;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Make the window click-through so clicks pass to WeChat beneath
        var hwnd = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT);

        DrawLabels();
        _hook.Install();
    }

    private void DrawLabels()
    {
        Canvas.Children.Clear();
        for (int i = 0; i < _messages.Count && i < LabelLetters.Length; i++)
        {
            var msg  = _messages[i];
            char lbl = LabelLetters[i];

            // Place circle opposite avatar: right of received, left of sent
            double cx = msg.FromSelf
                ? msg.X - CircleR - 6 - _originX
                : msg.X + msg.Width + CircleR + 6 - _originX;
            double cy = msg.Y + msg.Height / 2.0 - _originY;

            // Circle
            var ellipse = new Ellipse
            {
                Width  = CircleR * 2,
                Height = CircleR * 2,
                Fill   = LabelFill,
            };
            Canvas.SetLeft(ellipse, cx - CircleR);
            Canvas.SetTop(ellipse,  cy - CircleR);
            Canvas.Children.Add(ellipse);

            // Letter
            var text = new TextBlock
            {
                Text       = lbl.ToString(),
                Foreground = LabelText,
                FontSize   = 13,
                FontWeight = FontWeights.Bold,
            };
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(text, cx - text.DesiredSize.Width  / 2);
            Canvas.SetTop(text,  cy - text.DesiredSize.Height / 2);
            Canvas.Children.Add(text);
        }
    }

    private bool OnKey(Keys key)
    {
        if (key == Keys.Escape)
        {
            Dispatcher.Invoke(Dismiss);
            _completion(new CancelledOutcome());
            return true;   // swallow
        }

        int idx = key - Keys.A;
        if (idx >= 0 && idx < _messages.Count && idx < LabelLetters.Length)
        {
            var msg  = _messages[idx];
            char lbl = LabelLetters[idx];
            Dispatcher.Invoke(Dismiss);
            _completion(new PickedOutcome(lbl, msg));
            return true;   // swallow
        }
        return false;
    }

    private void Dismiss()
    {
        _hook.Uninstall();
        Close();
    }
}
