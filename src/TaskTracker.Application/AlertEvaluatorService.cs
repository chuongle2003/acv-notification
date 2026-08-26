using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaskTracker.Domain;
using TaskStatus = TaskTracker.Domain.TaskStatus;

namespace TaskTracker.Application;

public class NotificationState
{
    public string LogicalRowKey { get; set; } = "";
    public string DeadlineVersion { get; set; } = "";
    public AlertGroup AlertGroup { get; set; }
    public DateTimeOffset? FirstNotifiedAtUtc { get; set; }
    public DateTimeOffset? LastNotifiedAtUtc { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
    public int NotificationCount { get; set; }
}

public class NotificationDecision
{
    public bool ShouldNotify { get; set; }
    public TaskRow? Task { get; set; }
    public AlertGroup Group { get; set; }
}

public interface INotificationStateRepository
{
    IReadOnlyList<NotificationState> GetStates(IEnumerable<string> logicalRowKeys);
    void UpdateStates(IEnumerable<NotificationState> states);
    void Acknowledge(
        string logicalRowKey,
        string deadlineVersion,
        AlertGroup alertGroup,
        DateTimeOffset acknowledgedAtUtc);
}

public class AlertEvaluatorService
{
    private readonly INotificationStateRepository _repository;
    private readonly IClock _clock;
    private readonly TimeSpan _repeatInterval;

    public AlertEvaluatorService(
        INotificationStateRepository repository,
        IClock clock,
        TimeSpan? repeatInterval = null)
    {
        _repository = repository;
        _clock = clock;
        _repeatInterval = repeatInterval ?? TimeSpan.FromHours(12);
    }

    public IReadOnlyList<NotificationDecision> Evaluate(IReadOnlyList<TaskRow> currentTasks)
    {
        var decisions = new List<NotificationDecision>();

        var alertableTasks = currentTasks.Where(t =>
            !t.IsCompleted &&
            t.DeadlineVersion != null &&
            (t.CurrentStatus == TaskStatus.DueSoon ||
             t.CurrentStatus == TaskStatus.DueToday ||
             t.CurrentStatus == TaskStatus.Overdue)).ToList();

        if (!alertableTasks.Any()) return decisions;

        var existingStates = _repository.GetStates(alertableTasks.Select(t => t.LogicalRowKey))
            .ToDictionary(s => StateKey(s.LogicalRowKey, s.DeadlineVersion, s.AlertGroup));

        foreach (var task in alertableTasks)
        {
            var group = task.CurrentStatus == TaskStatus.Overdue ? AlertGroup.Overdue : AlertGroup.Upcoming;
            var stateKey = StateKey(task.LogicalRowKey, task.DeadlineVersion!, group);

            existingStates.TryGetValue(stateKey, out var state);

            if (state == null)
            {
                state = new NotificationState
                {
                    LogicalRowKey = task.LogicalRowKey,
                    DeadlineVersion = task.DeadlineVersion!,
                    AlertGroup = group
                };
            }

            bool shouldNotify = false;

            if (state.AcknowledgedAtUtc == null)
            {
                if (state.LastNotifiedAtUtc == null)
                {
                    shouldNotify = true; // First time
                }
                else
                {
                    var timeSinceLast = _clock.UtcNow - state.LastNotifiedAtUtc.Value;
                    if (timeSinceLast >= _repeatInterval)
                    {
                        shouldNotify = true; // Repeat reminder
                    }
                }
            }

            if (shouldNotify)
            {
                decisions.Add(new NotificationDecision
                {
                    ShouldNotify = true,
                    Task = task,
                    Group = group
                });
            }
        }

        return decisions;
    }

    public void RecordNotified(IEnumerable<NotificationDecision> sentDecisions)
    {
        var sent = sentDecisions
            .Where(d => d.Task?.DeadlineVersion != null)
            .ToList();
        if (sent.Count == 0) return;

        var states = _repository.GetStates(sent.Select(d => d.Task!.LogicalRowKey))
            .ToDictionary(s => StateKey(s.LogicalRowKey, s.DeadlineVersion, s.AlertGroup));
        var now = _clock.UtcNow;
        var updates = new List<NotificationState>();

        foreach (var decision in sent)
        {
            var task = decision.Task!;
            var version = task.DeadlineVersion!;
            var key = StateKey(task.LogicalRowKey, version, decision.Group);
            if (!states.TryGetValue(key, out var state))
            {
                state = new NotificationState
                {
                    LogicalRowKey = task.LogicalRowKey,
                    DeadlineVersion = version,
                    AlertGroup = decision.Group
                };
            }

            state.FirstNotifiedAtUtc ??= now;
            state.LastNotifiedAtUtc = now;
            state.NotificationCount++;
            updates.Add(state);
        }

        _repository.UpdateStates(updates);
    }

    private static string StateKey(string logicalRowKey, string deadlineVersion, AlertGroup group) =>
        $"{logicalRowKey}\u001f{deadlineVersion}\u001f{group}";

    public bool ShouldBatch(IReadOnlyList<NotificationDecision> decisions)
    {
        return decisions.Count > 3;
    }
}

public interface IAppNotificationSink
{
    Task<bool> ShowIndividualAsync(NotificationDecision decision, CancellationToken cancellationToken = default);
    Task<bool> ShowSummaryAsync(
        IReadOnlyList<NotificationDecision> decisions,
        CancellationToken cancellationToken = default);
}

public sealed class NotificationCoordinator
{
    private readonly AlertEvaluatorService _evaluator;
    private readonly IAppNotificationSink _sink;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NotificationCoordinator(AlertEvaluatorService evaluator, IAppNotificationSink sink)
    {
        _evaluator = evaluator;
        _sink = sink;
    }

    public async Task<int> EvaluateAndNotifyAsync(
        IReadOnlyList<TaskRow> currentTasks,
        bool notificationsPaused,
        CancellationToken cancellationToken = default)
    {
        if (notificationsPaused) return 0;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Refresh, watcher, resume and the minute scheduler can converge at
            // once. Serialize evaluation + send + persistence so the same alert
            // cannot be emitted twice before LastNotifiedAtUtc is recorded.
            var decisions = _evaluator.Evaluate(currentTasks);
            if (decisions.Count == 0) return 0;

            if (_evaluator.ShouldBatch(decisions))
            {
                if (!await _sink.ShowSummaryAsync(decisions, cancellationToken).ConfigureAwait(false)) return 0;
                _evaluator.RecordNotified(decisions);
                return decisions.Count;
            }

            var sent = new List<NotificationDecision>();
            foreach (var decision in decisions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await _sink.ShowIndividualAsync(decision, cancellationToken).ConfigureAwait(false))
                    sent.Add(decision);
            }

            _evaluator.RecordNotified(sent);
            return sent.Count;
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class AcknowledgeAlertUseCase
{
    private readonly INotificationStateRepository _repository;
    private readonly IClock _clock;

    public AcknowledgeAlertUseCase(INotificationStateRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public void Execute(TaskRow task)
    {
        if (string.IsNullOrWhiteSpace(task.DeadlineVersion)) return;
        var group = task.CurrentStatus == TaskStatus.Overdue ? AlertGroup.Overdue : AlertGroup.Upcoming;
        _repository.Acknowledge(task.LogicalRowKey, task.DeadlineVersion, group, _clock.UtcNow);
    }

    public bool IsAcknowledged(TaskRow task)
    {
        if (string.IsNullOrWhiteSpace(task.DeadlineVersion)) return false;
        var group = task.CurrentStatus == TaskStatus.Overdue ? AlertGroup.Overdue : AlertGroup.Upcoming;
        return _repository.GetStates(new[] { task.LogicalRowKey }).Any(s =>
            s.LogicalRowKey == task.LogicalRowKey &&
            s.DeadlineVersion == task.DeadlineVersion &&
            s.AlertGroup == group &&
            s.AcknowledgedAtUtc != null);
    }
}
