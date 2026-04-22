using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MsgDots;

/// <summary>
/// System-tray icon with context menu (Chinese UI).
/// </summary>
sealed class AppTray : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _hotkeyItem;

    public AppTray(Action onChangeHotkey, Action onShowPermissions)
    {
        _hotkeyItem = new ToolStripMenuItem(HotkeyLabel()) { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("消息快捷操作") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_hotkeyItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("修改快捷键\u2026", null, (_, _) => onChangeHotkey());
        menu.Items.Add("权限设置\u2026",   null, (_, _) => onShowPermissions());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出",             null, (_, _) => System.Windows.Application.Current.Shutdown());

        _icon = new NotifyIcon
        {
            Icon             = LoadIcon(),
            Text             = "MsgDots — 消息快捷操作",
            Visible          = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => onChangeHotkey();
    }

    public void UpdateHotkeyLabel(HotkeyDef cfg)
    {
        _hotkeyItem.Text = HotkeyLabel(cfg);
    }

    private static string HotkeyLabel(HotkeyDef? cfg = null) =>
        $"快捷键：{(cfg ?? HotkeyConfig.Current).Display}";

    private static Icon LoadIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon.ico");
            if (File.Exists(path)) return new Icon(path);
        }
        catch { /* ignore */ }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
