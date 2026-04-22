using System.Windows;

namespace MsgDots;

public partial class App : Application
{
    private AppTray?      _tray;
    private HotkeyManager? _hotkey;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        QMLog.Info("MsgDots starting");

        _tray   = new AppTray(OnChangeHotkey, OnShowPermissions);
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
            messages = BubbleDetector.DetectRecentMessages(limit: 8);
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

    private void OnShowPermissions()
    {
        // TODO: permissions window
        QMLog.Info("permissions window not yet implemented");
    }
}
