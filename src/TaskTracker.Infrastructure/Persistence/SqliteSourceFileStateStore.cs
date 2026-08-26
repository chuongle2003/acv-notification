using System;
using System.Text.Json;
using Dapper;
using TaskTracker.Application;

namespace TaskTracker.Infrastructure.Persistence;

public sealed class SqliteSourceFileStateStore : ISourceFileStateStore
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteSourceFileStateStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public SourceFileState? Get(string sourceFileId)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = connection.QuerySingleOrDefault<SourceFileStateRow>(@"
            SELECT
                id AS Id,
                path AS Path,
                last_successful_hash AS LastSuccessfulHash,
                last_successful_read_utc AS LastSuccessfulReadUtc,
                last_error AS LastError,
                last_error_utc AS LastErrorUtc
            FROM source_files
            WHERE id = @SourceFileId
        ", new { SourceFileId = sourceFileId });
        return row == null
            ? null
            : new SourceFileState(
                row.Id,
                row.Path,
                row.LastSuccessfulHash,
                ParseDate(row.LastSuccessfulReadUtc),
                row.LastError,
                ParseDate(row.LastErrorUtc));
    }

    public void EnsureSource(string sourceFileId, string path)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        // The schema keeps paths unique. If a previously-used path is selected
        // again under a new source id, archive only its source metadata; task
        // history remains keyed by its old id.
        connection.Execute(
            "DELETE FROM source_files WHERE path = @Path AND id <> @SourceFileId",
            new { Path = path, SourceFileId = sourceFileId }, transaction);

        connection.Execute(@"
            INSERT INTO source_files (id, path, enabled)
            VALUES (@SourceFileId, @Path, 1)
            ON CONFLICT(id) DO UPDATE SET path = excluded.path, enabled = 1
        ", new { SourceFileId = sourceFileId, Path = path }, transaction);

        transaction.Commit();
    }

    public void RecordSuccess(
        string sourceFileId,
        string path,
        string hash,
        string snapshotId,
        ImportDiagnostics diagnostics,
        DateTimeOffset completedAtUtc)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        connection.Execute(@"
            UPDATE source_files
            SET path = @Path,
                last_successful_hash = @Hash,
                last_successful_read_utc = @CompletedAtUtc,
                last_error = NULL,
                last_error_utc = NULL
            WHERE id = @SourceFileId
        ", new
        {
            SourceFileId = sourceFileId,
            Path = path,
            Hash = hash,
            CompletedAtUtc = completedAtUtc.ToString("o")
        }, transaction);

        connection.Execute(@"
            INSERT INTO import_snapshots (
                id, source_file_id, file_hash, imported_at_utc, status, diagnostics_json)
            VALUES (
                @SnapshotId, @SourceFileId, @Hash, @CompletedAtUtc, 'Success', @DiagnosticsJson)
        ", new
        {
            SnapshotId = snapshotId,
            SourceFileId = sourceFileId,
            Hash = hash,
            CompletedAtUtc = completedAtUtc.ToString("o"),
            DiagnosticsJson = JsonSerializer.Serialize(diagnostics)
        }, transaction);

        transaction.Commit();
    }

    public void RecordFailure(string sourceFileId, string path, string error, DateTimeOffset failedAtUtc)
    {
        EnsureSource(sourceFileId, path);
        using var connection = _connectionFactory.CreateConnection();
        connection.Execute(@"
            UPDATE source_files
            SET last_error = @Error,
                last_error_utc = @FailedAtUtc
            WHERE id = @SourceFileId
        ", new
        {
            SourceFileId = sourceFileId,
            Error = error,
            FailedAtUtc = failedAtUtc.ToString("o")
        });
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : DateTimeOffset.Parse(value);

    private sealed class SourceFileStateRow
    {
        public string Id { get; set; } = "";
        public string Path { get; set; } = "";
        public string? LastSuccessfulHash { get; set; }
        public string? LastSuccessfulReadUtc { get; set; }
        public string? LastError { get; set; }
        public string? LastErrorUtc { get; set; }
    }
}
