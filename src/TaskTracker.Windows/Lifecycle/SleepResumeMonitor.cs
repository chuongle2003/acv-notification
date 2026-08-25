using System;

namespace TaskTracker.Windows.Lifecycle;

/// <summary>
/// Raises an event when Windows resumes from sleep so alerts can be
/// re-evaluated immediately (deadlines may have passed while asleep).
/// Uses SystemEvents.PowerModeChanged under a testable wrapper.
/// </summary>
public interface ISleepResumeMonitor : IDisposable
{
    event EventHandler? ResumedFromSleep;
}

public class SleepResumeMonitor : ISleepResumeMonitor
{
    public event EventHandler? ResumedFromSleep;

    public SleepResumeMonitor()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            ResumedFromSleep?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
