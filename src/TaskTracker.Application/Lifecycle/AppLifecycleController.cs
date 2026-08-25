using System;

namespace TaskTracker.Application.Lifecycle;

/// <summary>
/// Abstraction over the main window show/hide behavior so lifecycle logic
/// (tray, close-to-tray, second-instance activation) is unit-testable.
/// </summary>
public interface IMainWindowHandle
{
    void ShowFromTray();
    void HideToTray();
    bool IsVisible { get; }
}

/// <summary>
/// Abstraction for the tray icon surface.
/// </summary>
public interface ITrayIcon : IDisposable
{
    void Show();
    void UpdateTooltip(string tooltip);
}

/// <summary>
/// Coordinates tray/close-to-tray/second-instance lifecycle decisions.
/// Pure logic, no platform APIs — the WPF shell implements the interfaces.
/// </summary>
public class AppLifecycleController
{
    private readonly IMainWindowHandle _mainWindow;
    private readonly ITrayIcon _trayIcon;
    private readonly Func<string> _tooltipProvider;

    public bool CloseToTrayEnabled { get; set; } = true;

    public AppLifecycleController(IMainWindowHandle mainWindow, ITrayIcon trayIcon, Func<string> tooltipProvider)
    {
        _mainWindow = mainWindow;
        _trayIcon = trayIcon;
        _tooltipProvider = tooltipProvider;
    }

    /// <summary>App startup: show window + tray icon.</summary>
    public void OnStartup(bool startMinimized)
    {
        _trayIcon.Show();
        _trayIcon.UpdateTooltip(_tooltipProvider());

        if (startMinimized)
        {
            _mainWindow.HideToTray();
        }
        else
        {
            _mainWindow.ShowFromTray();
        }
    }

    /// <summary>User clicked the window X.</summary>
    public void OnCloseRequested()
    {
        if (CloseToTrayEnabled)
        {
            _mainWindow.HideToTray();
        }
        else
        {
            RequestExit();
        }
    }

    /// <summary>Tray double-click or "Mở" menu or second-instance activation.</summary>
    public void Activate()
    {
        _mainWindow.ShowFromTray();
        _trayIcon.UpdateTooltip(_tooltipProvider());
    }

    /// <summary>Tray context menu "Exit" — real quit.</summary>
    public event EventHandler? ExitRequested;

    public void RequestExit()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
