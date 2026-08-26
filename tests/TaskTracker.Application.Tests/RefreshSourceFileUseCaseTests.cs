using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TaskTracker.Application;
using TaskTracker.Domain.Tests.Fakes;
using Xunit;

namespace TaskTracker.Application.Tests;

public sealed class RefreshSourceFileUseCaseTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"refresh-use-case-{Guid.NewGuid():N}");

    public RefreshSourceFileUseCaseTests() => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public async Task Success_ImportsRecordsHashAndDeletesTemporarySnapshot()
    {
        var tempSnapshot = Path.Combine(_tempDirectory, "snapshot.xlsx");
        File.WriteAllBytes(tempSnapshot, new byte[] { 1, 2, 3 });
        var stable = new FakeStableReader(new StableReadResult(
            StableReadStatus.Success, tempSnapshot, "hash-1"));
        var importer = new FakeImporter(new ImportDiagnostics
        {
            SnapshotId = "snapshot-1",
            ValidRowsImported = 3
        });
        var store = new FakeSourceStore();
        var useCase = Create(stable, importer, store);

        var result = await useCase.ExecuteAsync("source-1", "workbook.xlsx");

        Assert.Equal(RefreshStatus.Imported, result.Status);
        Assert.Equal("hash-1", store.State?.LastSuccessfulHash);
        Assert.Equal(1, importer.CallCount);
        Assert.False(File.Exists(tempSnapshot));
    }

    [Fact]
    public async Task Unchanged_DoesNotImportOrCreateSnapshot()
    {
        var stable = new FakeStableReader(new StableReadResult(StableReadStatus.Unchanged, Hash: "same"));
        var importer = new FakeImporter(new ImportDiagnostics());
        var store = new FakeSourceStore();

        var result = await Create(stable, importer, store)
            .ExecuteAsync("source-1", "workbook.xlsx");

        Assert.Equal(RefreshStatus.Unchanged, result.Status);
        Assert.Equal(0, importer.CallCount);
        Assert.Null(store.LastError);
    }

    [Theory]
    [InlineData(StableReadStatus.FileNotFound, RefreshStatus.Missing)]
    [InlineData(StableReadStatus.InvalidFormat, RefreshStatus.Invalid)]
    [InlineData(StableReadStatus.Timeout, RefreshStatus.TimedOut)]
    public async Task ReadFailure_PreservesLastSuccessfulState(
        StableReadStatus stableStatus,
        RefreshStatus expectedStatus)
    {
        var store = new FakeSourceStore
        {
            State = new SourceFileState("source-1", "workbook.xlsx", "last-good", null, null, null)
        };
        var useCase = Create(
            new FakeStableReader(new StableReadResult(stableStatus)),
            new FakeImporter(new ImportDiagnostics()),
            store);

        var result = await useCase.ExecuteAsync("source-1", "workbook.xlsx");

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal("last-good", store.State?.LastSuccessfulHash);
        Assert.NotNull(store.LastError);
    }

    [Fact]
    public async Task ImportFailure_DoesNotAdvanceSuccessfulHash()
    {
        var tempSnapshot = Path.Combine(_tempDirectory, "broken.xlsx");
        File.WriteAllBytes(tempSnapshot, new byte[] { 1 });
        var store = new FakeSourceStore
        {
            State = new SourceFileState("source-1", "workbook.xlsx", "last-good", null, null, null)
        };
        var useCase = Create(
            new FakeStableReader(new StableReadResult(StableReadStatus.Success, tempSnapshot, "new-hash")),
            new FakeImporter(new ImportDiagnostics { SnapshotId = "bad", ErrorMessage = "parse failed" }),
            store);

        var result = await useCase.ExecuteAsync("source-1", "workbook.xlsx");

        Assert.Equal(RefreshStatus.Failed, result.Status);
        Assert.Equal("last-good", store.State?.LastSuccessfulHash);
        Assert.Equal("parse failed", store.LastError);
        Assert.False(File.Exists(tempSnapshot));
    }

    private static RefreshSourceFileUseCase Create(
        IStableFileReader stable,
        IWorkbookImporter importer,
        ISourceFileStateStore store) => new(
            stable,
            importer,
            store,
            new FakeClock(DateTimeOffset.UtcNow, DateOnly.FromDateTime(DateTime.Today)));

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, true);
    }

    private sealed class FakeStableReader : IStableFileReader
    {
        private readonly StableReadResult _result;
        public FakeStableReader(StableReadResult result) => _result = result;
        public Task<StableReadResult> ReadStableFileAsync(
            string sourcePath, string? lastKnownHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class FakeImporter : IWorkbookImporter
    {
        private readonly ImportDiagnostics _result;
        public int CallCount { get; private set; }
        public FakeImporter(ImportDiagnostics result) => _result = result;
        public ImportDiagnostics Execute(string sourceFileId, Stream excelStream)
        {
            CallCount++;
            return _result;
        }
    }

    private sealed class FakeSourceStore : ISourceFileStateStore
    {
        public SourceFileState? State { get; set; }
        public string? LastError { get; private set; }
        public SourceFileState? Get(string sourceFileId) => State;
        public void EnsureSource(string sourceFileId, string path) =>
            State ??= new SourceFileState(sourceFileId, path, null, null, null, null);
        public void RecordSuccess(string sourceFileId, string path, string hash, string snapshotId,
            ImportDiagnostics diagnostics, DateTimeOffset completedAtUtc) =>
            State = new SourceFileState(sourceFileId, path, hash, completedAtUtc, null, null);
        public void RecordFailure(string sourceFileId, string path, string error, DateTimeOffset failedAtUtc)
        {
            LastError = error;
            State ??= new SourceFileState(sourceFileId, path, null, null, error, failedAtUtc);
        }
    }
}
