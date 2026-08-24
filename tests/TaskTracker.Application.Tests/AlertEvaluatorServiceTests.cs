using System;
using System.Collections.Generic;
using System.Linq;
using TaskTracker.Application;
using TaskTracker.Domain;
using TaskTracker.Domain.Tests.Fakes;
using Xunit;

namespace TaskTracker.Application.Tests;

public class FakeNotificationRepository : INotificationStateRepository
{
    private readonly Dictionary<string, NotificationState> _store = new();

    public IReadOnlyList<NotificationState> GetStates(IEnumerable<string> logicalRowKeys)
    {
        return _store.Values.Where(s => logicalRowKeys.Contains(s.LogicalRowKey)).ToList();
    }

    public void UpdateStates(IEnumerable<NotificationState> states)
    {
        foreach (var state in states)
        {
            var key = $"{state.LogicalRowKey}_{state.AlertGroup}";
            _store[key] = state;
        }
    }

    public NotificationState? GetState(string rowKey, AlertGroup group)
    {
        _store.TryGetValue($"{rowKey}_{group}", out var val);
        return val;
    }
}

public class AlertEvaluatorServiceTests
{
    private readonly FakeClock _clock;
    private readonly FakeNotificationRepository _repo;
    private readonly AlertEvaluatorService _evaluator;

    public AlertEvaluatorServiceTests()
    {
        _clock = new FakeClock(
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 8, 24));

        _repo = new FakeNotificationRepository();
        _evaluator = new AlertEvaluatorService(_repo, _clock, TimeSpan.FromHours(12));
    }

    [Fact]
    public void Evaluate_FirstTime_SendsNotification()
    {
        var tasks = new List<TaskRow>
        {
            new TaskRow
            {
                LogicalRowKey = "k1",
                DeadlineVersion = "v1",
                CurrentStatus = TaskStatus.DueSoon
            }
        };

        var decisions = _evaluator.Evaluate(tasks);

        Assert.Single(decisions);
        var state = _repo.GetState("k1", AlertGroup.Upcoming);
        Assert.NotNull(state);
        Assert.Equal(_clock.UtcNow, state!.FirstNotifiedAtUtc);
        Assert.Equal(1, state.NotificationCount);
    }

    [Fact]
    public void Evaluate_NotAcknowledged_DoesNotSendBefore12Hours()
    {
        var tasks = new List<TaskRow>
        {
            new TaskRow { LogicalRowKey = "k1", DeadlineVersion = "v1", CurrentStatus = TaskStatus.DueSoon }
        };

        _evaluator.Evaluate(tasks); // Notified at 10:00

        _clock.UtcNow = _clock.UtcNow.AddHours(5); // 15:00

        var decisions2 = _evaluator.Evaluate(tasks);

        Assert.Empty(decisions2); // Should not notify yet
    }

    [Fact]
    public void Evaluate_NotAcknowledged_SendsAgainAfter12Hours()
    {
        var tasks = new List<TaskRow>
        {
            new TaskRow { LogicalRowKey = "k1", DeadlineVersion = "v1", CurrentStatus = TaskStatus.DueSoon }
        };

        _evaluator.Evaluate(tasks); // 10:00

        _clock.UtcNow = _clock.UtcNow.AddHours(13); // 23:00

        var decisions2 = _evaluator.Evaluate(tasks);

        Assert.Single(decisions2);
        var state = _repo.GetState("k1", AlertGroup.Upcoming);
        Assert.Equal(2, state!.NotificationCount);
    }

    [Fact]
    public void Evaluate_Acknowledged_NeverSendsAgain()
    {
        var tasks = new List<TaskRow>
        {
            new TaskRow { LogicalRowKey = "k1", DeadlineVersion = "v1", CurrentStatus = TaskStatus.DueSoon }
        };

        _evaluator.Evaluate(tasks);

        // Manually acknowledge
        var state = _repo.GetState("k1", AlertGroup.Upcoming)!;
        state.AcknowledgedAtUtc = _clock.UtcNow;

        _clock.UtcNow = _clock.UtcNow.AddHours(24); // Way past 12h

        var decisions2 = _evaluator.Evaluate(tasks);

        Assert.Empty(decisions2);
    }

    [Fact]
    public void Evaluate_DeadlineVersionChanged_ResetsAcknowledgmentAndSendsNewAlert()
    {
        var tasks = new List<TaskRow>
        {
            new TaskRow { LogicalRowKey = "k1", DeadlineVersion = "v1", CurrentStatus = TaskStatus.DueSoon }
        };

        _evaluator.Evaluate(tasks);
        _repo.GetState("k1", AlertGroup.Upcoming)!.AcknowledgedAtUtc = _clock.UtcNow;

        // User changed deadline in Excel, causing new version
        tasks[0] = new TaskRow { LogicalRowKey = "k1", DeadlineVersion = "v2", CurrentStatus = TaskStatus.DueSoon };

        var decisions2 = _evaluator.Evaluate(tasks);

        Assert.Single(decisions2); // Should alert again because version changed!
    }

    [Fact]
    public void Evaluate_StatusChangesToOverdue_CreatesNewAlertGroup()
    {
        var tasks = new List<TaskRow>
        {
            new TaskRow { LogicalRowKey = "k1", DeadlineVersion = "v1", CurrentStatus = TaskStatus.DueToday }
        };

        _evaluator.Evaluate(tasks);
        _repo.GetState("k1", AlertGroup.Upcoming)!.AcknowledgedAtUtc = _clock.UtcNow;

        // Status becomes overdue
        tasks[0] = new TaskRow { LogicalRowKey = "k1", DeadlineVersion = "v1", CurrentStatus = TaskStatus.Overdue };

        var decisions2 = _evaluator.Evaluate(tasks);

        Assert.Single(decisions2); // Overdue is a different group
        Assert.NotNull(_repo.GetState("k1", AlertGroup.Overdue));
    }

    [Fact]
    public void ShouldBatch_TrueWhenMoreThan3()
    {
        var decisions = new List<NotificationDecision>
        {
            new NotificationDecision(),
            new NotificationDecision(),
            new NotificationDecision(),
            new NotificationDecision()
        };

        Assert.True(_evaluator.ShouldBatch(decisions));
    }
}
