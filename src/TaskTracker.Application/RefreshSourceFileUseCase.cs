using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TaskTracker.Domain;

namespace TaskTracker.Application;

public enum StableReadStatus
{
    Success,
    Unchanged,
    FileNotFound,
    InvalidFormat,
    Timeout
}

public record StableReadResult(
    StableReadStatus Status,
    string? TempFilePath = null,
    string? Hash = null);

public interface IStableFileReader
{
    Task<StableReadResult> ReadStableFileAsync(
        string sourcePath,
        string? lastKnownHash,
        CancellationToken cancellationToken = default);
}

public record SourceFileState(
    string Id,
    string Path,
    string? LastSuccessfulHash,
    DateTimeOffset? LastSuccessfulReadUtc,
    string? LastError,
    DateTimeOffset? LastErrorUtc);

public interface ISourceFileStateStore
{
    SourceFileState? Get(string sourceFileId);
    void EnsureSource(string sourceFileId, string path);
    void RecordSuccess(
        string sourceFileId,
        string path,
        string hash,
        string snapshotId,
        ImportDiagnostics diagnostics,
        DateTimeOffset completedAtUtc);
    void RecordFailure(string sourceFileId, string path, string error, DateTimeOffset failedAtUtc);
}

public enum RefreshStatus
{
    Imported,
    Unchanged,
    Missing,
    Invalid,
    TimedOut,
    Failed
}

public record RefreshResult(
    RefreshStatus Status,
    ImportDiagnostics? Diagnostics = null,
    string? ErrorMessage = null,
    string? Hash = null);

/// <summary>
/// The single refresh pipeline used by startup, file watcher, manual refresh,
/// resume-from-sleep, and source-file changes.
/// </summary>
public sealed class RefreshSourceFileUseCase
{
    private readonly IStableFileReader _stableFileReader;
    private readonly IWorkbookImporter _importUseCase;
    private readonly ISourceFileStateStore _sourceStore;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RefreshSourceFileUseCase(
        IStableFileReader stableFileReader,
        IWorkbookImporter importUseCase,
        ISourceFileStateStore sourceStore,
        IClock clock)
    {
        _stableFileReader = stableFileReader;
        _importUseCase = importUseCase;
        _sourceStore = sourceStore;
        _clock = clock;
    }

    public async Task<RefreshResult> ExecuteAsync(
        string sourceFileId,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFileId))
            return new RefreshResult(RefreshStatus.Failed, ErrorMessage: "Source file id is missing.");
        if (string.IsNullOrWhiteSpace(sourcePath))
            return new RefreshResult(RefreshStatus.Missing, ErrorMessage: "Source file path is missing.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _sourceStore.EnsureSource(sourceFileId, sourcePath);
            var previous = _sourceStore.Get(sourceFileId);
            var stable = await _stableFileReader.ReadStableFileAsync(
                sourcePath, previous?.LastSuccessfulHash, cancellationToken).ConfigureAwait(false);

            return stable.Status switch
            {
                StableReadStatus.Unchanged => new RefreshResult(RefreshStatus.Unchanged, Hash: stable.Hash),
                StableReadStatus.FileNotFound => RecordFailure(
                    sourceFileId, sourcePath, RefreshStatus.Missing, "Không tìm thấy file nguồn."),
                StableReadStatus.InvalidFormat => RecordFailure(
                    sourceFileId, sourcePath, RefreshStatus.Invalid, "File nguồn không phải XLSX hợp lệ."),
                StableReadStatus.Timeout => RecordFailure(
                    sourceFileId, sourcePath, RefreshStatus.TimedOut, "File nguồn chưa ổn định sau thời gian chờ."),
                StableReadStatus.Success => await ImportStableSnapshotAsync(
                    sourceFileId, sourcePath, stable, cancellationToken).ConfigureAwait(false),
                _ => RecordFailure(sourceFileId, sourcePath, RefreshStatus.Failed, "Trạng thái đọc file không xác định.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RecordFailure(sourceFileId, sourcePath, RefreshStatus.Failed, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RefreshResult> ImportStableSnapshotAsync(
        string sourceFileId,
        string sourcePath,
        StableReadResult stable,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stable.TempFilePath) || string.IsNullOrWhiteSpace(stable.Hash))
            return RecordFailure(sourceFileId, sourcePath, RefreshStatus.Failed, "Stable reader returned no snapshot.");

        try
        {
            var diagnostics = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = File.OpenRead(stable.TempFilePath);
                return _importUseCase.Execute(sourceFileId, stream);
            }, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(diagnostics.ErrorMessage))
            {
                return RecordFailure(
                    sourceFileId, sourcePath, RefreshStatus.Failed, diagnostics.ErrorMessage, diagnostics);
            }

            _sourceStore.RecordSuccess(
                sourceFileId,
                sourcePath,
                stable.Hash,
                diagnostics.SnapshotId ?? throw new InvalidOperationException("Import did not return a snapshot id."),
                diagnostics,
                _clock.UtcNow);

            return new RefreshResult(RefreshStatus.Imported, diagnostics, Hash: stable.Hash);
        }
        finally
        {
            try { File.Delete(stable.TempFilePath); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private RefreshResult RecordFailure(
        string sourceFileId,
        string sourcePath,
        RefreshStatus status,
        string error,
        ImportDiagnostics? diagnostics = null)
    {
        _sourceStore.RecordFailure(sourceFileId, sourcePath, error, _clock.UtcNow);
        return new RefreshResult(status, diagnostics, error);
    }
}
