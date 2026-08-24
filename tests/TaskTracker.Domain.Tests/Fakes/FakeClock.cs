using System;

namespace TaskTracker.Domain.Tests.Fakes;

public class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }
    public DateOnly TodayLocal { get; set; }

    public FakeClock(DateTimeOffset utcNow, DateOnly todayLocal)
    {
        UtcNow = utcNow;
        TodayLocal = todayLocal;
    }
}
