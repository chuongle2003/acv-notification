using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using TaskTracker.Application;
using TaskTracker.Domain;

namespace TaskTracker.Infrastructure.Persistence;

public sealed class SqliteNotificationStateRepository : INotificationStateRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteNotificationStateRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public IReadOnlyList<NotificationState> GetStates(IEnumerable<string> logicalRowKeys)
    {
        var keys = logicalRowKeys.Distinct().ToArray();
        if (keys.Length == 0) return Array.Empty<NotificationState>();

        using var connection = _connectionFactory.CreateConnection();
        var rows = connection.Query<NotificationStateRow>(@"
            SELECT
                logical_row_key AS LogicalRowKey,
                deadline_version AS DeadlineVersion,
                alert_group AS AlertGroup,
                first_notified_at_utc AS FirstNotifiedAtUtc,
                last_notified_at_utc AS LastNotifiedAtUtc,
                acknowledged_at_utc AS AcknowledgedAtUtc,
                notification_count AS NotificationCount
            FROM notification_states
            WHERE logical_row_key IN @Keys
        ", new { Keys = keys });

        return rows.Select(row => new NotificationState
        {
            LogicalRowKey = row.LogicalRowKey,
            DeadlineVersion = row.DeadlineVersion,
            AlertGroup = Enum.TryParse<AlertGroup>(row.AlertGroup, out var group)
                ? group : AlertGroup.Upcoming,
            FirstNotifiedAtUtc = ParseDate(row.FirstNotifiedAtUtc),
            LastNotifiedAtUtc = ParseDate(row.LastNotifiedAtUtc),
            AcknowledgedAtUtc = ParseDate(row.AcknowledgedAtUtc),
            NotificationCount = row.NotificationCount
        }).ToList();
    }

    public void UpdateStates(IEnumerable<NotificationState> states)
    {
        var values = states.ToArray();
        if (values.Length == 0) return;

        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var state in values)
        {
            connection.Execute(@"
                INSERT INTO notification_states (
                    id, logical_row_key, deadline_version, alert_group,
                    first_notified_at_utc, last_notified_at_utc,
                    acknowledged_at_utc, notification_count)
                VALUES (
                    @Id, @LogicalRowKey, @DeadlineVersion, @AlertGroup,
                    @FirstNotifiedAtUtc, @LastNotifiedAtUtc,
                    @AcknowledgedAtUtc, @NotificationCount)
                ON CONFLICT(logical_row_key, deadline_version, alert_group) DO UPDATE SET
                    first_notified_at_utc = excluded.first_notified_at_utc,
                    last_notified_at_utc = excluded.last_notified_at_utc,
                    acknowledged_at_utc = excluded.acknowledged_at_utc,
                    notification_count = excluded.notification_count
            ", ToParameters(state), transaction);
        }
        transaction.Commit();
    }

    public void Acknowledge(
        string logicalRowKey,
        string deadlineVersion,
        AlertGroup alertGroup,
        DateTimeOffset acknowledgedAtUtc)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Execute(@"
            INSERT INTO notification_states (
                id, logical_row_key, deadline_version, alert_group,
                acknowledged_at_utc, notification_count)
            VALUES (
                @Id, @LogicalRowKey, @DeadlineVersion, @AlertGroup,
                @AcknowledgedAtUtc, 0)
            ON CONFLICT(logical_row_key, deadline_version, alert_group) DO UPDATE SET
                acknowledged_at_utc = excluded.acknowledged_at_utc
        ", new
        {
            Id = Guid.NewGuid().ToString("N"),
            LogicalRowKey = logicalRowKey,
            DeadlineVersion = deadlineVersion,
            AlertGroup = alertGroup.ToString(),
            AcknowledgedAtUtc = acknowledgedAtUtc.ToString("o")
        });
    }

    private static object ToParameters(NotificationState state) => new
    {
        Id = Guid.NewGuid().ToString("N"),
        state.LogicalRowKey,
        state.DeadlineVersion,
        AlertGroup = state.AlertGroup.ToString(),
        FirstNotifiedAtUtc = state.FirstNotifiedAtUtc?.ToString("o"),
        LastNotifiedAtUtc = state.LastNotifiedAtUtc?.ToString("o"),
        AcknowledgedAtUtc = state.AcknowledgedAtUtc?.ToString("o"),
        state.NotificationCount
    };

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : DateTimeOffset.Parse(value);

    private sealed class NotificationStateRow
    {
        public string LogicalRowKey { get; init; } = "";
        public string DeadlineVersion { get; init; } = "";
        public string AlertGroup { get; init; } = "";
        public string? FirstNotifiedAtUtc { get; init; }
        public string? LastNotifiedAtUtc { get; init; }
        public string? AcknowledgedAtUtc { get; init; }
        public int NotificationCount { get; init; }
    }
}
