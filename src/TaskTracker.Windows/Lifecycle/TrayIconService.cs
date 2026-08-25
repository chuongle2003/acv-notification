using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskTracker.Application.Lifecycle;

namespace TaskTracker.Windows.Lifecycle;

/// <summary>
/// WinForms NotifyIcon adapter for WPF, with a context menu (Mở / Thoát).
/// </summary>
public class TrayIconService : ITrayIcon
{
    private readonly AppLifecycleController _controller;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;

    public TrayIconService(AppLifecycleController controller)
    {
        _controller = controller;
    }

    public void Show()
    {
        if (_notifyIcon != null) return; // already shown

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = "Task Tracker"
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();

        var openItem = new System.Windows.Forms.ToolStripMenuItem("Mở");
        openItem.Click += (_, _) => _controller.Activate();
        menu.Items.Add(openItem);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Thoát");
        exitItem.Click += (_, _) => _controller.RequestExit();
        menu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => _controller.Activate();
    }

    public void UpdateTooltip(string tooltip)
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
        }
    }

    private static Icon LoadIcon()
    {
        // Use the app icon if available, otherwise a generic system icon.
        try
        {
            var uri = new Uri("pack://application:,,,/TaskTracker.Windows;component/app.ico");
            var sri = System.Windows.Application.GetResourceStream(uri);
            if (sri != null)
            {
                return new Icon(sri.Stream);
            }
        }
        catch (System.IO.IOException)
        {
            // icon resource missing — fall through
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}

/// <summary>
/// Bridges the WPF Window to IMainWindowHandle, intercepting the close button
/// for close-to-tray behavior.
/// </summary>
public class MainWindowHandle : IMainWindowHandle
{
    private readonly Window _window;

    public MainWindowHandle(Window window)
    {
        _window = window;
    }

    public bool IsVisible => _window.IsVisible;

    public void ShowFromTray()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void HideToTray()
    {
        _window.Hide();
    }
}
