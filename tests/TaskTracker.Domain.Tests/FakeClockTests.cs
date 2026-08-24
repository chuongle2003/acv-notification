using System;
using TaskTracker.Domain.Tests.Fakes;
using Xunit;

namespace TaskTracker.Domain.Tests;

public class FakeClockTests
{
    [Fact]
    public void FakeClock_ShouldReturnConfiguredValues()
    {
        var utcNow = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);
        var today = new DateOnly(2026, 8, 24);

        var clock = new FakeClock(utcNow, today);

        Assert.Equal(utcNow, clock.UtcNow);
        Assert.Equal(today, clock.TodayLocal);

        var tomorrow = today.AddDays(1);
        clock.TodayLocal = tomorrow;
        
        Assert.Equal(tomorrow, clock.TodayLocal);
    }
}
