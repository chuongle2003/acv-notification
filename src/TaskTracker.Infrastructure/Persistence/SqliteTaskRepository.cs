using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using TaskTracker.Application;
using TaskTracker.Domain;
using TaskStatus = TaskTracker.Domain.TaskStatus;

namespace TaskTracker.Infrastructure.Persistence;

public class SqliteTaskRepository : ITaskRowStore
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteTaskRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public void CommitSnapshot(string snapshotId, string sourceFileId, IReadOnlyList<TaskRow> currentRows)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            // 1. Mark all current rows for this file as not current
            connection.Execute(
                "UPDATE task_rows SET is_current = 0 WHERE source_file_id = @SourceFileId AND is_current = 1",
                new { SourceFileId = sourceFileId },
                transaction);

            // 2. Insert new rows
            if (currentRows.Any())
            {
                var sql = @"
                    INSERT INTO task_rows (
                        id, source_file_id, logical_row_key, sheet_name, sheet_week_number,
                        source_row_number, stt, document_number, task_content, executing_unit,
                        primary_handler, deadline_raw, deadline_cell_kind, deadline_format_id,
                        deadline_format_code, deadline_cell_address, progress, result, note,
                        is_completed, deadline_version, current_status, days_remaining,
                        deadline_kind, excel_candidate, swapped_candidate, resolved_start_date,
                        resolved_end_date, resolved_time, resolution_source, requires_review,
                        snapshot_id, is_current
                    ) VALUES (
                        @Id, @SourceFileId, @LogicalRowKey, @SheetName, @SheetWeekNumber,
                        @SourceRowNumber, @Stt, @DocumentNumber, @TaskContent, @ExecutingUnit,
                        @PrimaryHandler, @DeadlineRaw, @DeadlineCellKind, @DeadlineFormatId,
                        @DeadlineFormatCode, @DeadlineCellAddress, @Progress, @Result, @Note,
                        @IsCompleted, @DeadlineVersion, @CurrentStatusStr, @DaysRemaining,
                        @DeadlineKindStr, @ExcelCandidate, @SwappedCandidate, @ResolvedStartDate,
                        @ResolvedEndDate, @ResolvedTime, @ResolutionSourceStr, @RequiresReview,
                        @SnapshotId, @IsCurrent
                    )";

                var parameters = currentRows.Select(r => new
                {
                    Id = Guid.NewGuid().ToString("N"),
                    r.SourceFileId,
                    r.LogicalRowKey,
                    r.SheetName,
                    r.SheetWeekNumber,
                    r.SourceRowNumber,
                    r.Stt,
                    r.DocumentNumber,
                    r.TaskContent,
                    r.ExecutingUnit,
                    r.PrimaryHandler,
                    r.DeadlineRaw,
                    r.DeadlineCellKind,
                    r.DeadlineFormatId,
                    r.DeadlineFormatCode,
                    r.DeadlineCellAddress,
                    r.Progress,
                    r.Result,
                    r.Note,
                    IsCompleted = r.IsCompleted ? 1 : 0,
                    r.DeadlineVersion,
                    CurrentStatusStr = r.CurrentStatus.ToString(),
                    r.DaysRemaining,
                    DeadlineKindStr = r.DeadlineKind.ToString(),
                    ExcelCandidate = DateOnlyToString(r.ExcelCandidate),
                    SwappedCandidate = DateOnlyToString(r.SwappedCandidate),
                    ResolvedStartDate = DateOnlyToString(r.ResolvedStartDate),
                    ResolvedEndDate = DateOnlyToString(r.ResolvedEndDate),
                    ResolvedTime = TimeSpanToString(r.ResolvedTime),
                    ResolutionSourceStr = r.ResolutionSource.ToString(),
                    RequiresReview = r.RequiresReview ? 1 : 0,
                    r.SnapshotId,
                    IsCurrent = 1
                });

                connection.Execute(sql, parameters, transaction);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void UpdateDeadlineForCorrection(string sourceFileId, string logicalRowKey, DeadlineCorrectionUpdate update)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            connection.Execute(@"
                UPDATE task_rows
                SET deadline_version = @DeadlineVersion,
                    deadline_kind = @DeadlineKind,
                    excel_candidate = @ExcelCandidate,
                    swapped_candidate = @SwappedCandidate,
                    resolved_start_date = @ResolvedStartDate,
                    resolved_end_date = @ResolvedEndDate,
                    resolved_time = @ResolvedTime,
                    resolution_source = @ResolutionSource,
                    requires_review = @RequiresReview,
                    current_status = @Status,
                    days_remaining = @DaysRemaining,
                    snapshot_id = @SnapshotId
                WHERE source_file_id = @SourceFileId
                  AND logical_row_key = @LogicalRowKey
                  AND is_current = 1
            ", new
            {
                update.DeadlineVersion,
                DeadlineKind = update.DeadlineKind.ToString(),
                ExcelCandidate = DateOnlyToString(update.ExcelCandidate),
                SwappedCandidate = DateOnlyToString(update.SwappedCandidate),
                ResolvedStartDate = DateOnlyToString(update.ResolvedStartDate),
                ResolvedEndDate = DateOnlyToString(update.ResolvedEndDate),
                ResolvedTime = TimeSpanToString(update.ResolvedTime),
                ResolutionSource = update.ResolutionSource.ToString(),
                RequiresReview = update.RequiresReview ? 1 : 0,
                Status = update.Status.ToString(),
                update.DaysRemaining,
                SnapshotId = update.SnapshotId,
                SourceFileId = sourceFileId,
                LogicalRowKey = logicalRowKey
            }, transaction);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public IReadOnlyList<TaskRow> GetCurrentRows(string sourceFileId)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            SELECT
                source_file_id AS SourceFileId,
                logical_row_key AS LogicalRowKey,
                snapshot_id AS SnapshotId,
                is_current AS IsCurrent,
                sheet_name AS SheetName,
                sheet_week_number AS SheetWeekNumber,
                source_row_number AS SourceRowNumber,
                stt AS Stt,
                document_number AS DocumentNumber,
                task_content AS TaskContent,
                executing_unit AS ExecutingUnit,
                primary_handler AS PrimaryHandler,
                deadline_raw AS DeadlineRaw,
                deadline_cell_kind AS DeadlineCellKind,
                deadline_format_id AS DeadlineFormatId,
                deadline_format_code AS DeadlineFormatCode,
                deadline_cell_address AS DeadlineCellAddress,
                progress AS Progress,
                result AS Result,
                note AS Note,
                is_completed AS IsCompleted,
                deadline_version AS DeadlineVersion,
                deadline_kind AS DeadlineKindStr,
                excel_candidate AS ExcelCandidate,
                swapped_candidate AS SwappedCandidate,
                resolved_start_date AS ResolvedStartDate,
                resolved_end_date AS ResolvedEndDate,
                resolved_time AS ResolvedTime,
                resolution_source AS ResolutionSourceStr,
                requires_review AS RequiresReview,
                current_status AS CurrentStatusStr,
                days_remaining AS DaysRemaining
            FROM task_rows
            WHERE source_file_id = @SourceFileId AND is_current = 1
        ";

        var rawData = connection.Query<TaskRowRecord>(sql, new { SourceFileId = sourceFileId });

        return rawData.Select(row => new TaskRow
        {
            SourceFileId = row.SourceFileId,
            LogicalRowKey = row.LogicalRowKey,
            SnapshotId = row.SnapshotId,
            IsCurrent = row.IsCurrent != 0,
            SheetName = row.SheetName,
            SheetWeekNumber = row.SheetWeekNumber is null ? null : (int)row.SheetWeekNumber.Value,
            SourceRowNumber = (int)row.SourceRowNumber,
            Stt = row.Stt,
            DocumentNumber = row.DocumentNumber,
            TaskContent = row.TaskContent,
            ExecutingUnit = row.ExecutingUnit,
            PrimaryHandler = row.PrimaryHandler,
            DeadlineRaw = row.DeadlineRaw,
            DeadlineCellKind = row.DeadlineCellKind,
            DeadlineFormatId = row.DeadlineFormatId is null ? null : (int)row.DeadlineFormatId.Value,
            DeadlineFormatCode = row.DeadlineFormatCode,
            DeadlineCellAddress = row.DeadlineCellAddress,
            Progress = row.Progress,
            Result = row.Result,
            Note = row.Note,
            IsCompleted = row.IsCompleted != 0,
            DeadlineVersion = row.DeadlineVersion,
            DeadlineKind = Enum.TryParse<DeadlineParserKind>(row.DeadlineKindStr, out var kind)
                ? kind : DeadlineParserKind.Unrecognized,
            ExcelCandidate = StringToDateOnly(row.ExcelCandidate),
            SwappedCandidate = StringToDateOnly(row.SwappedCandidate),
            ResolvedStartDate = StringToDateOnly(row.ResolvedStartDate),
            ResolvedEndDate = StringToDateOnly(row.ResolvedEndDate),
            ResolvedTime = StringToTimeSpan(row.ResolvedTime),
            ResolutionSource = Enum.TryParse<ResolutionSource>(row.ResolutionSourceStr, out var source)
                ? source : ResolutionSource.Parser,
            RequiresReview = row.RequiresReview != 0,
            CurrentStatus = Enum.TryParse<TaskStatus>(row.CurrentStatusStr, out var status) ? status : TaskStatus.Unknown,
            DaysRemaining = row.DaysRemaining is null ? null : (int)row.DaysRemaining.Value
        }).ToList();
    }

    private static string? DateOnlyToString(DateOnly? value) => value?.ToString("yyyy-MM-dd");
    private static string? TimeSpanToString(TimeSpan? value) => value?.ToString(@"hh\:mm");
    private static DateOnly? StringToDateOnly(object? value) =>
        value == null || string.IsNullOrWhiteSpace(value.ToString())
            ? null : DateOnly.Parse(value.ToString()!);
    private static TimeSpan? StringToTimeSpan(object? value) =>
        value == null || string.IsNullOrWhiteSpace(value.ToString())
            ? null : TimeSpan.Parse(value.ToString()!);

    private sealed class TaskRowRecord
    {
        public string SourceFileId { get; init; } = "";
        public string LogicalRowKey { get; init; } = "";
        public string SnapshotId { get; init; } = "";
        public long IsCurrent { get; init; }
        public string SheetName { get; init; } = "";
        public long? SheetWeekNumber { get; init; }
        public long SourceRowNumber { get; init; }
        public string? Stt { get; init; }
        public string? DocumentNumber { get; init; }
        public string? TaskContent { get; init; }
        public string? ExecutingUnit { get; init; }
        public string? PrimaryHandler { get; init; }
        public string? DeadlineRaw { get; init; }
        public string? DeadlineCellKind { get; init; }
        public long? DeadlineFormatId { get; init; }
        public string? DeadlineFormatCode { get; init; }
        public string? DeadlineCellAddress { get; init; }
        public string? Progress { get; init; }
        public string? Result { get; init; }
        public string? Note { get; init; }
        public long IsCompleted { get; init; }
        public string? DeadlineVersion { get; init; }
        public string DeadlineKindStr { get; init; } = "Unrecognized";
        public string? ExcelCandidate { get; init; }
        public string? SwappedCandidate { get; init; }
        public string? ResolvedStartDate { get; init; }
        public string? ResolvedEndDate { get; init; }
        public string? ResolvedTime { get; init; }
        public string ResolutionSourceStr { get; init; } = "Parser";
        public long RequiresReview { get; init; }
        public string CurrentStatusStr { get; init; } = "Unknown";
        public long? DaysRemaining { get; init; }
    }
}
