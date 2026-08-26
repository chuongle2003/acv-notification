using System;
using System.IO;
using System.Linq;
using Dapper;
using TaskTracker.Application;
using TaskTracker.Domain;
using TaskTracker.Infrastructure.Persistence;
using Xunit;

namespace TaskTracker.Infrastructure.Tests;

public sealed class NotificationAndSourceStateTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"state-tests-{Guid.NewGuid():N}.db");
    private readonly SqliteConnectionFactory _factory;

    public NotificationAndSourceStateTests()
    {
        _factory = new SqliteConnectionFactory(
            $"Data Source={_dbPath};Mode=ReadWriteCreate;Pooling=False");
        new DatabaseMigrator(_factory).MigrateUp();
    }

    [Fact]
    public void NotificationState_PersistsSendAndAcknowledgement()
    {
        var repository = new SqliteNotificationStateRepository(_factory);
        var notifiedAt = new DateTimeOffset(2026, 8, 26, 1, 0, 0, TimeSpan.Zero);
        repository.UpdateStates(new[]
        {
            new NotificationState
            {
                LogicalRowKey = "row-1",
                DeadlineVersion = "v1",
                AlertGroup = AlertGroup.Upcoming,
                FirstNotifiedAtUtc = notifiedAt,
                LastNotifiedAtUtc = notifiedAt,
                NotificationCount = 1
            }
        });

        var acknowledgedAt = notifiedAt.AddMinutes(5);
        repository.Acknowledge("row-1", "v1", AlertGroup.Upcoming, acknowledgedAt);

        var state = Assert.Single(repository.GetStates(new[] { "row-1" }));
        Assert.Equal(1, state.NotificationCount);
        Assert.Equal(notifiedAt, state.LastNotifiedAtUtc);
        Assert.Equal(acknowledgedAt, state.AcknowledgedAtUtc);
    }

    [Fact]
    public void SourceState_RecordsSuccessfulHashAndSnapshot()
    {
        var repository = new SqliteSourceFileStateStore(_factory);
        repository.EnsureSource("source-1", "book.xlsx");
        repository.RecordSuccess(
            "source-1",
            "book.xlsx",
            "hash-1",
            "snapshot-1",
            new ImportDiagnostics { SnapshotId = "snapshot-1", ValidRowsImported = 2 },
            DateTimeOffset.UtcNow);

        var state = repository.Get("source-1");
        Assert.NotNull(state);
        Assert.Equal("hash-1", state!.LastSuccessfulHash);

        using var connection = _factory.CreateConnection();
        Assert.Equal(1, connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM import_snapshots WHERE id = 'snapshot-1'"));
    }

    [Fact]
    public void Migration_IsIdempotentAndAddsDeadlineMetadata()
    {
        new DatabaseMigrator(_factory).MigrateUp();

        using var connection = _factory.CreateConnection();
        Assert.Equal(2, connection.QuerySingle<long>("PRAGMA user_version"));
        var columns = connection.Query<string>(
            "SELECT name FROM pragma_table_info('task_rows')").ToArray();
        Assert.Contains("deadline_cell_address", columns);
        Assert.Contains("resolution_source", columns);
        Assert.Contains("requires_review", columns);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
