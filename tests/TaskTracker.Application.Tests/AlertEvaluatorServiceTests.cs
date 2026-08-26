using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaskTracker.Application;
using TaskTracker.Domain;
using TaskStatus = TaskTracker.Domain.TaskStatus;
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
            var key = $"{state.LogicalRowKey}_{state.DeadlineVersion}_{state.AlertGroup}";
            _store[key] = state;
        }
    }

    public void Acknowledge(string logicalRowKey, string deadlineVersion, AlertGroup alertGroup,
        DateTimeOffset acknowledgedAtUtc)
    {
        var key = $"{logicalRowKey}_{deadlineVersion}_{alertGroup}";
        if (!_store.TryGetValue(key, out var state))
        {
            state = new NotificationState
            {
                LogicalRowKey = logicalRowKey,
                DeadlineVersion = deadlineVersion,
                AlertGroup = alertGroup
            };
            _store[key] = state;
        }
        state.AcknowledgedAtUtc = acknowledgedAtUtc;
    }

    public NotificationState? GetState(string rowKey, AlertGroup group, string version = "v1")
    {
        _store.TryGetValue($"{rowKey}_{version}_{group}", out var val);
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
        _evaluator.RecordNotified(decisions);

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

        var first = _evaluator.Evaluate(tasks); // Notified at 10:00
        _evaluator.RecordNotified(first);

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

        var first = _evaluator.Evaluate(tasks); // 10:00
        _evaluator.RecordNotified(first);

        _clock.UtcNow = _clock.UtcNow.AddHours(13); // 23:00

        var decisions2 = _evaluator.Evaluate(tasks);
        _evaluator.RecordNotified(decisions2);

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

        var first = _evaluator.Evaluate(tasks);
        _evaluator.RecordNotified(first);

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

        var first = _evaluator.Evaluate(tasks);
        _evaluator.RecordNotified(first);
        _repo.GetState("k1", AlertGroup.Upcoming)!.AcknowledgedAtUtc = _clock.UtcNow;

        // User changed deadline in Excel, causing new version
        tasks[0] = new TaskRow { LogicalRowKey = "k1", DeadlineVersion = "v2", CurrentStatus = TaskStatus.DueSoon };

        var decisions2 = _evaluator.Evaluate(tasks);

        Assert.Single(decisions2); // Should alert again because version changed!
        _evaluator.RecordNotified(decisions2);
        Assert.NotNull(_repo.GetState("k1", AlertGroup.Upcoming, "v2"));
    }

    [Fact]
    public void Evaluate_StatusChangesToOverdue_CreatesNewAlertGroup()
    {
        var tasks = new List<TaskRow>
        {
            new TaskRow { LogicalRowKey = "k1", DeadlineVersion = "v1", CurrentStatus = TaskStatus.DueToday }
        };

        var first = _evaluator.Evaluate(tasks);
        _evaluator.RecordNotified(first);
        _repo.GetState("k1", AlertGroup.Upcoming)!.AcknowledgedAtUtc = _clock.UtcNow;

        // Status becomes overdue
        tasks[0] = new TaskRow { LogicalRowKey = "k1", DeadlineVersion = "v1", CurrentStatus = TaskStatus.Overdue };

        var decisions2 = _evaluator.Evaluate(tasks);
        _evaluator.RecordNotified(decisions2);

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

    [Fact]
    public async Task Coordinator_FailedSend_DoesNotAdvanceNotificationState()
    {
        var coordinator = new NotificationCoordinator(_evaluator, new FakeNotificationSink(false));
        var tasks = new[]
        {
            new TaskRow { LogicalRowKey = "k1", DeadlineVersion = "v1", CurrentStatus = TaskStatus.DueSoon }
        };

        var sent = await coordinator.EvaluateAndNotifyAsync(tasks, notificationsPaused: false);

        Assert.Equal(0, sent);
        Assert.Null(_repo.GetState("k1", AlertGroup.Upcoming));
        Assert.Single(_evaluator.Evaluate(tasks));
    }

    [Fact]
    public async Task Coordinator_Paused_DoesNotCallSinkOrAdvanceState()
    {
        var sink = new FakeNotificationSink(true);
        var coordinator = new NotificationCoordinator(_evaluator, sink);
        var tasks = new[]
        {
            new TaskRow { LogicalRowKey = "k1", DeadlineVersion = "v1", CurrentStatus = TaskStatus.DueSoon }
        };

        var sent = await coordinator.EvaluateAndNotifyAsync(tasks, notificationsPaused: true);

        Assert.Equal(0, sent);
        Assert.Equal(0, sink.CallCount);
        Assert.Null(_repo.GetState("k1", AlertGroup.Upcoming));
    }

    [Fact]
    public async Task Coordinator_ConcurrentEvaluations_SendOnlyOnce()
    {
        var sink = new DelayedNotificationSink();
        var coordinator = new NotificationCoordinator(_evaluator, sink);
        var tasks = new[]
        {
            new TaskRow { LogicalRowKey = "k1", DeadlineVersion = "v1", CurrentStatus = TaskStatus.DueSoon }
        };

        var first = coordinator.EvaluateAndNotifyAsync(tasks, notificationsPaused: false);
        var second = coordinator.EvaluateAndNotifyAsync(tasks, notificationsPaused: false);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, results.Sum());
        Assert.Equal(1, sink.CallCount);
        Assert.Equal(1, _repo.GetState("k1", AlertGroup.Upcoming)!.NotificationCount);
    }

    private sealed class FakeNotificationSink : IAppNotificationSink
    {
        private readonly bool _succeeds;
        public int CallCount { get; private set; }

        public FakeNotificationSink(bool succeeds) => _succeeds = succeeds;

        public Task<bool> ShowIndividualAsync(
            NotificationDecision decision,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_succeeds);
        }

        public Task<bool> ShowSummaryAsync(
            IReadOnlyList<NotificationDecision> decisions,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_succeeds);
        }
    }

    private sealed class DelayedNotificationSink : IAppNotificationSink
    {
        public int CallCount { get; private set; }

        public async Task<bool> ShowIndividualAsync(
            NotificationDecision decision,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            await Task.Delay(50, cancellationToken);
            return true;
        }

        public Task<bool> ShowSummaryAsync(
            IReadOnlyList<NotificationDecision> decisions,
            CancellationToken cancellationToken = default) =>
            ShowIndividualAsync(decisions[0], cancellationToken);
    }
}
