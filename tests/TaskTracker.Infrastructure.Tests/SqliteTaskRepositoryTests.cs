using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaskTracker.Domain;
using TaskStatus = TaskTracker.Domain.TaskStatus;
using TaskTracker.Infrastructure.Persistence;
using Xunit;

namespace TaskTracker.Infrastructure.Tests;

public class SqliteTaskRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseMigrator _migrator;
    private readonly SqliteTaskRepository _repository;

    public SqliteTaskRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tasktracker_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath};Mode=ReadWriteCreate";

        _factory = new SqliteConnectionFactory(connectionString);
        _migrator = new DatabaseMigrator(_factory);
        _repository = new SqliteTaskRepository(_factory);

        _migrator.MigrateUp();
    }

    [Fact]
    public void CommitSnapshot_ReplacesCurrentRowsTransactionally()
    {
        var fileId = "file1";
        var snap1 = "snap1";

        var rows1 = new List<TaskRow>
        {
            new TaskRow
            {
                SourceFileId = fileId,
                LogicalRowKey = "key1",
                SnapshotId = snap1,
                IsCurrent = true,
                SheetName = "TUAN 33",
                SourceRowNumber = 5,
                DocumentNumber = "123/CV",
                IsCompleted = false,
                CurrentStatus = TaskStatus.DueSoon
            }
        };

        // Act 1: Initial commit
        _repository.CommitSnapshot(snap1, fileId, rows1);

        var currentAfterSnap1 = _repository.GetCurrentRows(fileId);
        Assert.Single(currentAfterSnap1);
        Assert.Equal(snap1, currentAfterSnap1[0].SnapshotId);
        Assert.Equal("123/CV", currentAfterSnap1[0].DocumentNumber);
        Assert.Equal(TaskStatus.DueSoon, currentAfterSnap1[0].CurrentStatus);

        // Act 2: Second commit (simulating file refresh)
        var snap2 = "snap2";
        var rows2 = new List<TaskRow>
        {
            new TaskRow
            {
                SourceFileId = fileId,
                LogicalRowKey = "key1", // Same logic row
                SnapshotId = snap2,
                IsCurrent = true,
                SheetName = "TUAN 33",
                SourceRowNumber = 5,
                DocumentNumber = "123/CV", // Same
                IsCompleted = true, // Status changed
                CurrentStatus = TaskStatus.Completed
            },
            new TaskRow
            {
                SourceFileId = fileId,
                LogicalRowKey = "key2", // New logic row added in Excel
                SnapshotId = snap2,
                IsCurrent = true,
                SheetName = "TUAN 33",
                SourceRowNumber = 6,
                DocumentNumber = "124/CV",
                IsCompleted = false,
                CurrentStatus = TaskStatus.Normal
            }
        };

        _repository.CommitSnapshot(snap2, fileId, rows2);

        var currentAfterSnap2 = _repository.GetCurrentRows(fileId);
        Assert.Equal(2, currentAfterSnap2.Count);

        // Assert old snap1 is no longer current
        Assert.All(currentAfterSnap2, r => Assert.Equal(snap2, r.SnapshotId));

        var updatedKey1 = currentAfterSnap2.First(r => r.LogicalRowKey == "key1");
        Assert.True(updatedKey1.IsCompleted);
        Assert.Equal(TaskStatus.Completed, updatedKey1.CurrentStatus);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }
}
