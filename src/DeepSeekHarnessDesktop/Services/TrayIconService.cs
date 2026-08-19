using DrawingIcon = System.Drawing.Icon;
using Forms = System.Windows.Forms;
using System.Reflection;

namespace DeepSeekHarnessDesktop.Services;

public sealed class TrayIconService : IDisposable
{
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ContextMenuStrip? _menu;
    private DrawingIcon? _icon;
    private Action? _openWindow;
    private Action? _exitApplication;
    private bool _hiddenNotificationShown;

    public void Initialize(Action openWindow, Action exitApplication)
    {
        if (_notifyIcon is not null)
        {
            throw new InvalidOperationException("The tray icon is already initialized.");
        }
        _openWindow = openWindow;
        _exitApplication = exitApplication;
        _icon = DrawingIcon.ExtractAssociatedIcon(
            Assembly.GetEntryAssembly()?.Location
            ?? throw new InvalidOperationException("The application executable path is unavailable."));

        var openItem = new Forms.ToolStripMenuItem("打开 DeepSeek Harness Desktop");
        openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);
        openItem.Click += OnOpenClick;
        var exitItem = new Forms.ToolStripMenuItem("退出");
        exitItem.Click += OnExitClick;

        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add(openItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "DeepSeek Harness Desktop",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += OnOpenClick;
        _notifyIcon.BalloonTipClicked += OnOpenClick;
    }

    public void ShowHiddenNotification()
    {
        if (_notifyIcon is null || _hiddenNotificationShown)
        {
            return;
        }

        _hiddenNotificationShown = true;
        _notifyIcon.BalloonTipTitle = "DeepSeek Harness Desktop";
        _notifyIcon.BalloonTipText = "应用仍在后台运行，可从系统托盘重新打开。";
        _notifyIcon.ShowBalloonTip(2500);
    }

    private void OnOpenClick(object? sender, EventArgs e) => _openWindow?.Invoke();

    private void OnExitClick(object? sender, EventArgs e) => _exitApplication?.Invoke();

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.DoubleClick -= OnOpenClick;
            _notifyIcon.BalloonTipClicked -= OnOpenClick;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _menu?.Dispose();
        _menu = null;
        _icon?.Dispose();
        _icon = null;
        _openWindow = null;
        _exitApplication = null;
    }
}
