using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;

namespace MsgDots;

public partial class App : Application
{
    private const int MaxOverlayMessages = 26;
    private static readonly TimeSpan CancelEscGuardDuration = TimeSpan.FromMilliseconds(1000);

    private AppTray?      _tray;
    private HotkeyManager? _hotkey;
    private KeyboardHook? _cancelEscGuardHook;
    private DispatcherTimer? _cancelEscGuardTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        QMLog.Info("MsgDots starting");

        _tray   = new AppTray(OnChangeHotkey);
        _hotkey = new HotkeyManager(OnHotkeyFired);

        if (!_hotkey.Register(HotkeyConfig.Current))
            QMLog.Info("warning: hotkey registration failed");

        HotkeyConfig.Changed += cfg =>
        {
            _hotkey.Unregister();
            _hotkey.Register(cfg);
            _tray.UpdateHotkeyLabel(cfg);
            QMLog.Info($"hotkey changed to {cfg.Display}");
        };

        QMLog.Info($"started — hotkey: {HotkeyConfig.Current.Display}");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopCancelEscGuard();
        _hotkey?.Unregister();
        _tray?.Dispose();
        QMLog.Info("MsgDots exiting");
        base.OnExit(e);
    }

    // ── Quote pipeline ────────────────────────────────────────────────────────

    private LabelOverlay? _overlay;

    private void OnHotkeyFired()
    {
        if (_overlay != null)
        {
            QMLog.Info("hotkey fired — overlay already visible, ignoring");
            return;
        }

        QMLog.Info("hotkey fired");

        List<Message> messages;
        try
        {
            messages = BubbleDetector.DetectRecentMessages(limit: MaxOverlayMessages);
        }
        catch (Exception ex)
        {
            QMLog.Info($"bubble detect failed: {ex.Message}");
            return;
        }

        QMLog.Info($"detected {messages.Count} messages");

        _overlay = new LabelOverlay(messages, outcome =>
        {
            _overlay = null;
            switch (outcome)
            {
                case CancelledOutcome:
                    StartCancelEscGuard();
                    QMLog.Info("quote flow: cancelled");
                    break;
                case PickedOutcome picked:
                    QMLog.Info($"quote flow: picked {picked.Letter} at {picked.Message.Center}");
                    try
                    {
                        QuoteAction.QuoteAt(picked.Message.Center);
                        QMLog.Info("quote flow: quote triggered");
                    }
                    catch (Exception ex)
                    {
                        QMLog.Info($"quote flow: action failed: {ex.Message}");
                    }
                    break;
            }
        });
        _overlay.Show();
    }

    // ── Menu actions ──────────────────────────────────────────────────────────

    private HotkeyRecorderWindow? _hotkeyWindow;

    private void OnChangeHotkey()
    {
        if (_hotkeyWindow == null)
        {
            _hotkeyWindow = new HotkeyRecorderWindow();
            _hotkeyWindow.Closed += (_, _) => _hotkeyWindow = null;
        }
        _hotkeyWindow.Show();
        _hotkeyWindow.Activate();
    }

    private void StartCancelEscGuard()
    {
        _cancelEscGuardTimer?.Stop();

        if (_cancelEscGuardHook == null)
        {
            _cancelEscGuardHook = new KeyboardHook
            {
                ShouldSwallow = key => key == Keys.Escape
            };
            _cancelEscGuardHook.Install();
            QMLog.Info("cancel esc guard installed");
        }

        _cancelEscGuardTimer ??= new DispatcherTimer
        {
            Interval = CancelEscGuardDuration
        };
        _cancelEscGuardTimer.Tick -= OnCancelEscGuardTimerTick;
        _cancelEscGuardTimer.Tick += OnCancelEscGuardTimerTick;
        _cancelEscGuardTimer.Start();
    }

    private void OnCancelEscGuardTimerTick(object? sender, EventArgs e)
    {
        StopCancelEscGuard();
    }

    private void StopCancelEscGuard()
    {
        _cancelEscGuardTimer?.Stop();

        if (_cancelEscGuardHook != null)
        {
            _cancelEscGuardHook.Uninstall();
            _cancelEscGuardHook = null;
            QMLog.Info("cancel esc guard uninstalled");
        }
    }
}
