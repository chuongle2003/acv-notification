using System;
using TaskTracker.Domain;

namespace TaskTracker.Windows.Infrastructure;

public class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    // Windows local time
    public DateOnly TodayLocal => DateOnly.FromDateTime(DateTime.Now);
}
