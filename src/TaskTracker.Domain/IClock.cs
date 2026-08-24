using System;

namespace TaskTracker.Domain;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateOnly TodayLocal { get; }
}
