using System;
using System.Threading;
using TaskTracker.Application.Lifecycle;

namespace TaskTracker.Windows.Lifecycle;

/// <summary>
/// Windows named-mutex/event implementation of the single-instance lock.
/// The second instance signals the first (to show its window) and then exits.
/// </summary>
public sealed class SingleInstanceGuard : ISingleInstanceLock
{
    private const string MutexNameSuffix = "TaskTracker-WPF-SingleInstance";
    private const string ActivateEventSuffix = "TaskTracker-WPF-Activate";

    private static string MutexName => $@"Local\{MutexNameSuffix}";
    private static string ActivateEventName => $@"Local\{ActivateEventSuffix}";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _registeredWait;

    /// <summary>True when this process is the primary (first) instance.</summary>
    public bool IsPrimaryInstance { get; }

    /// <summary>
    /// Raised on the primary instance when a secondary instance requests activation
    /// (i.e. the user tried to launch the app again).
    /// </summary>
    public event EventHandler? ActivateRequested;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, out var createdNew);

        if (createdNew)
        {
            IsPrimaryInstance = true;

            // Primary instance listens for activation pings from second instances.
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                _activateEvent,
                callBack: (_, _) => ActivateRequested?.Invoke(this, EventArgs.Empty),
                state: null,
                millisecondsTimeOutInterval: -1,
                executeOnlyOnce: false);
        }
        else
        {
            IsPrimaryInstance = false;

            // Signal the primary instance to show itself.
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }
        }
    }

    public void Dispose()
    {
        _registeredWait?.Unregister(null);

        if (IsPrimaryInstance)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* not owned */ }
        }

        _mutex.Dispose();
        _activateEvent?.Dispose();
    }
}
