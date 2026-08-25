using System;
using System.Collections.Generic;
using System.Linq;
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
            .ToDictionary(s => $"{s.LogicalRowKey}_{s.AlertGroup}");

        var statesToUpdate = new List<NotificationState>();

        foreach (var task in alertableTasks)
        {
            var group = task.CurrentStatus == TaskStatus.Overdue ? AlertGroup.Overdue : AlertGroup.Upcoming;
            var stateKey = $"{task.LogicalRowKey}_{group}";

            existingStates.TryGetValue(stateKey, out var state);

            // If the deadline version changed, treat it as a brand new alert (ignore old state)
            if (state != null && state.DeadlineVersion != task.DeadlineVersion)
            {
                state = null;
            }

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
                var now = _clock.UtcNow;
                state.FirstNotifiedAtUtc ??= now;
                state.LastNotifiedAtUtc = now;
                state.NotificationCount++;

                statesToUpdate.Add(state);

                decisions.Add(new NotificationDecision
                {
                    ShouldNotify = true,
                    Task = task,
                    Group = group
                });
            }
        }

        if (statesToUpdate.Any())
        {
            _repository.UpdateStates(statesToUpdate);
        }

        return decisions;
    }

    public bool ShouldBatch(IReadOnlyList<NotificationDecision> decisions)
    {
        return decisions.Count > 3;
    }
}
