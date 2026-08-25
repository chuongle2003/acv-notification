using System;
using TaskTracker.Application.Lifecycle;

namespace TaskTracker.Application.Tests;

public class FakeMainWindowHandle : IMainWindowHandle
{
    public bool IsVisible { get; private set; }
    public int ShowCount { get; private set; }
    public int HideCount { get; private set; }

    public void ShowFromTray()
    {
        ShowCount++;
        IsVisible = true;
    }

    public void HideToTray()
    {
        HideCount++;
        IsVisible = false;
    }
}

public class FakeTrayIcon : ITrayIcon
{
    public bool Shown { get; private set; }
    public string? LastTooltip { get; private set; }
    public bool Disposed { get; private set; }

    public void Show() => Shown = true;
    public void UpdateTooltip(string tooltip) => LastTooltip = tooltip;
    public void Dispose() => Disposed = true;
}
