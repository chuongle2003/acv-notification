using System;
using Xunit;
using TaskTracker.Application.Lifecycle;

namespace TaskTracker.Application.Tests;

public class AppLifecycleControllerTests
{
    private static (AppLifecycleController, FakeMainWindowHandle, FakeTrayIcon) Create()
    {
        var window = new FakeMainWindowHandle();
        var tray = new FakeTrayIcon();
        var controller = new AppLifecycleController(window, tray, () => "tooltip");
        return (controller, window, tray);
    }

    [Fact]
    public void OnStartup_NormalMode_ShowsWindowAndTray()
    {
        var (controller, window, tray) = Create();

        controller.OnStartup(startMinimized: false);

        Assert.True(tray.Shown);
        Assert.Equal("tooltip", tray.LastTooltip);
        Assert.True(window.IsVisible);
        Assert.Equal(1, window.ShowCount);
    }

    [Fact]
    public void OnStartup_BackgroundMode_ShowsTrayOnly()
    {
        var (controller, window, tray) = Create();

        controller.OnStartup(startMinimized: true);

        Assert.True(tray.Shown);
        Assert.False(window.IsVisible);
        Assert.Equal(1, window.HideCount);
        Assert.Equal(0, window.ShowCount);
    }

    [Fact]
    public void OnCloseRequested_WithCloseToTray_HidesInsteadOfExiting()
    {
        var (controller, window, _) = Create();
        var exitFired = false;
        controller.ExitRequested += (_, _) => exitFired = true;

        controller.OnCloseRequested();

        Assert.True(controller.CloseToTrayEnabled);
        Assert.False(exitFired);
        Assert.False(window.IsVisible);
        Assert.Equal(1, window.HideCount);
    }

    [Fact]
    public void OnCloseRequested_WithoutCloseToTray_FiresExit()
    {
        var (controller, window, _) = Create();
        controller.CloseToTrayEnabled = false;
        var exitFired = false;
        controller.ExitRequested += (_, _) => exitFired = true;

        controller.OnCloseRequested();

        Assert.True(exitFired);
        Assert.Equal(0, window.HideCount);
    }

    [Fact]
    public void Activate_FromHiddenState_ShowsWindow()
    {
        var (controller, window, tray) = Create();
        controller.OnStartup(startMinimized: true);

        controller.Activate();

        Assert.True(window.IsVisible);
        Assert.Equal(1, window.ShowCount);
        Assert.Equal("tooltip", tray.LastTooltip);
    }

    [Fact]
    public void RequestExit_FiresExitEvent()
    {
        var (controller, _, _) = Create();
        var exitFired = false;
        controller.ExitRequested += (_, _) => exitFired = true;

        controller.RequestExit();

        Assert.True(exitFired);
    }
}
