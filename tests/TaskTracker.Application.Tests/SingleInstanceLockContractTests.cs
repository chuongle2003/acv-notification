using System;
using System.Threading.Tasks;
using TaskTracker.Application.Lifecycle;
using Xunit;

namespace TaskTracker.Application.Tests;

/// <summary>
/// Contract tests for ISingleInstanceLock implementations, exercised via a fake.
/// The real Windows named-mutex implementation is smoke-tested on Windows
/// (see spec TASK-17 completion criteria: manual Windows smoke tests).
/// </summary>
public class SingleInstanceLockContractTests
{
    private class FakeSingleInstanceLock : ISingleInstanceLock
    {
        public bool IsPrimaryInstance { get; set; } = true;
        public event EventHandler? ActivateRequested;
        public bool Disposed { get; private set; }

        public void SignalActivate() => ActivateRequested?.Invoke(this, EventArgs.Empty);
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task ActivateRequested_RaisedOnPrimary_IsObservable()
    {
        var lock1 = new FakeSingleInstanceLock();
        var tcs = new TaskCompletionSource();

        lock1.ActivateRequested += (_, _) => tcs.SetResult();

        // Simulates the platform shell forwarding a second-instance ping.
        lock1.SignalActivate();

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(lock1.Disposed);

        lock1.Dispose();
        Assert.True(lock1.Disposed);
    }
}
