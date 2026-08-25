using System;

namespace TaskTracker.Application.Lifecycle;

/// <summary>
/// OS-level single-instance primitive, implemented by the platform shell
/// (WPF app on Windows uses a named mutex + named event).
/// </summary>
public interface ISingleInstanceLock : IDisposable
{
    /// <summary>True when this process owns the single-instance lock.</summary>
    bool IsPrimaryInstance { get; }

    /// <summary>Raised on the primary instance when a secondary launch requests activation.</summary>
    event EventHandler? ActivateRequested;
}
