using System;
using System.IO;
using System.Linq;
using TaskTracker.Application;
using TaskTracker.Domain;
using TaskStatus = TaskTracker.Domain.TaskStatus;
using TaskTracker.Domain.Tests.Fakes;
using TaskTracker.Infrastructure.Persistence;
using Xunit;

namespace TaskTracker.Infrastructure.Tests;

public class ResolveDeadlineUseCaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteTaskRepository _taskRepository;
    private readonly SqliteDeadlineResolutionRepository _resolutionRepository;
    private readonly ResolveDeadlineUseCase _useCase;
    private readonly FakeClock _clock;
    private readonly string _fileId = "file1";

    public ResolveDeadlineUseCaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"resolve_test_{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory($"Data Source={_dbPath};Mode=ReadWriteCreate");
        new DatabaseMigrator(factory).MigrateUp();

        _taskRepository = new SqliteTaskRepository(factory);
        _resolutionRepository = new SqliteDeadlineResolutionRepository(factory);

        _clock = new FakeClock(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero), new DateOnly(2026, 8, 24));

        _useCase = new ResolveDeadlineUseCase(
            _taskRepository,
            _resolutionRepository,
            new RowIdentityService(),
            new TaskStatusCalculator(_clock),
            _clock);

        // Seed one ambiguous row (04/08 -> could be Apr 8 or Aug 4) and one normal row
        _taskRepository.CommitSnapshot("snap1", _fileId, new[]
        {
            new TaskRow
            {
                SourceFileId = _fileId,
                LogicalRowKey = "row-ambiguous",
                SnapshotId = "snap1",
                IsCurrent = true,
                SheetName = "TUAN 33",
                SourceRowNumber = 1,
                DocumentNumber = "111/CV",
                DeadlineRaw = "04/08",
                DeadlineCellKind = "DateTime",
                IsCompleted = false,
                DeadlineVersion = "v-ambiguous",
                DeadlineKind = DeadlineParserKind.ExcelDateAmbiguous,
                ExcelCandidate = new DateOnly(2026, 8, 4),
                SwappedCandidate = new DateOnly(2026, 4, 8),
                RequiresReview = true,
                CurrentStatus = TaskStatus.NeedsReview
            },
            new TaskRow
            {
                SourceFileId = _fileId,
                LogicalRowKey = "row-normal",
                SnapshotId = "snap1",
                IsCurrent = true,
                SheetName = "TUAN 33",
                SourceRowNumber = 2,
                DocumentNumber = "222/CV",
                DeadlineRaw = "29/08/2026",
                IsCompleted = false,
                DeadlineVersion = "v-normal",
                CurrentStatus = TaskStatus.Normal
            }
        });
    }

    [Fact]
    public void ManualDate_UpdatesRowStatusAndPersistsResolution()
    {
        var result = _useCase.Execute(new ResolveDeadlineRequest
        {
            SourceFileId = _fileId,
            LogicalRowKey = "row-ambiguous",
            Action = DeadlineReviewAction.ManualDate,
            ManualDate = new DateOnly(2026, 9, 1)
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(TaskStatus.Normal, result.UpdatedRow!.CurrentStatus); // +8 days from Aug 24

        var rows = _taskRepository.GetCurrentRows(_fileId);
        var updated = rows.First(r => r.LogicalRowKey == "row-ambiguous");
        Assert.Equal(TaskStatus.Normal, updated.CurrentStatus);
        Assert.Equal(8, updated.DaysRemaining);

        // Resolution persisted and keyed to fingerprint of raw text
        var all = _resolutionRepository.GetAll();
        var resolution = Assert.Single(all);
        Assert.Equal("row-ambiguous", resolution.LogicalRowKey);
        Assert.Equal(ResolutionSource.ManualDate, resolution.ResolutionSource);
        Assert.Equal(new DateOnly(2026, 9, 1), resolution.SelectedStartDate);
    }

    [Fact]
    public void KeepExcelDate_OnRowWithoutValidDate_Fails()
    {
        var result = _useCase.Execute(new ResolveDeadlineRequest
        {
            SourceFileId = _fileId,
            LogicalRowKey = "row-normal",
            Action = DeadlineReviewAction.KeepExcelDate
        });

        Assert.False(result.Success);
        Assert.Contains("không có ngày gốc", result.ErrorMessage);
    }

    [Fact]
    public void UseSwappedDate_PersistsSelectedCandidate()
    {
        var result = _useCase.Execute(new ResolveDeadlineRequest
        {
            SourceFileId = _fileId,
            LogicalRowKey = "row-ambiguous",
            Action = DeadlineReviewAction.UseSwappedDate
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(new DateOnly(2026, 4, 8), result.UpdatedRow!.ResolvedStartDate);
        var resolution = Assert.Single(_resolutionRepository.GetAll());
        Assert.Equal(ResolutionSource.UseSwappedDate, resolution.ResolutionSource);
        Assert.Equal(new DateOnly(2026, 4, 8), resolution.SelectedStartDate);
    }

    [Fact]
    public void UseSwappedDate_OnNonAmbiguousRow_Fails()
    {
        var result = _useCase.Execute(new ResolveDeadlineRequest
        {
            SourceFileId = _fileId,
            LogicalRowKey = "row-normal", // 29/08/2026 parses unambiguously
            Action = DeadlineReviewAction.UseSwappedDate
        });

        Assert.False(result.Success);
        Assert.Contains("không có ứng viên ngày đảo", result.ErrorMessage);
    }

    [Fact]
    public void MarkUnresolved_SetsNeedsReview()
    {
        var result = _useCase.Execute(new ResolveDeadlineRequest
        {
            SourceFileId = _fileId,
            LogicalRowKey = "row-normal",
            Action = DeadlineReviewAction.MarkUnresolved
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(TaskStatus.NeedsReview, result.UpdatedRow!.CurrentStatus);
    }

    [Fact]
    public void UnknownRowKey_Fails()
    {
        var result = _useCase.Execute(new ResolveDeadlineRequest
        {
            SourceFileId = _fileId,
            LogicalRowKey = "does-not-exist",
            Action = DeadlineReviewAction.ManualDate,
            ManualDate = new DateOnly(2026, 9, 1)
        });

        Assert.False(result.Success);
        Assert.Contains("Không tìm thấy dòng", result.ErrorMessage);
    }

    [Fact]
    public void Resolution_IsReappliedWhenRawFingerprintUnchanged()
    {
        // User picks manual date
        _useCase.Execute(new ResolveDeadlineRequest
        {
            SourceFileId = _fileId,
            LogicalRowKey = "row-ambiguous",
            Action = DeadlineReviewAction.ManualDate,
            ManualDate = new DateOnly(2026, 9, 1)
        });

        // Later import produces the same raw text — the stored resolution applies
        var applicable = _useCase.FindApplicableResolution("row-ambiguous", "04/08");
        Assert.NotNull(applicable);
        Assert.Equal(ResolutionSource.ManualDate, applicable!.ResolutionSource);
        Assert.Equal(new DateOnly(2026, 9, 1), applicable.SelectedStartDate);

        // But a different raw text (user edited Excel) does NOT reuse it
        var notApplicable = _useCase.FindApplicableResolution("row-ambiguous", "05/08");
        Assert.Null(notApplicable);
    }

    [Fact]
    public void Reset_RemovesCorrectionAndRestoresOriginalReviewState()
    {
        _useCase.Execute(new ResolveDeadlineRequest
        {
            SourceFileId = _fileId,
            LogicalRowKey = "row-ambiguous",
            Action = DeadlineReviewAction.ManualDate,
            ManualDate = new DateOnly(2026, 9, 1)
        });

        var result = _useCase.Reset(_fileId, "row-ambiguous");

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(TaskStatus.NeedsReview, result.UpdatedRow!.CurrentStatus);
        Assert.True(result.UpdatedRow.RequiresReview);
        Assert.Empty(_resolutionRepository.GetAll());
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }
}
