using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using TaskTracker.Domain;

namespace TaskTracker.Infrastructure.Persistence;

public class SqliteTaskRepository
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
                        primary_handler, deadline_raw, progress, result, note,
                        is_completed, deadline_version, current_status, days_remaining,
                        snapshot_id, is_current
                    ) VALUES (
                        @Id, @SourceFileId, @LogicalRowKey, @SheetName, @SheetWeekNumber,
                        @SourceRowNumber, @Stt, @DocumentNumber, @TaskContent, @ExecutingUnit,
                        @PrimaryHandler, @DeadlineRaw, @Progress, @Result, @Note,
                        @IsCompleted, @DeadlineVersion, @CurrentStatusStr, @DaysRemaining,
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
                    r.Progress,
                    r.Result,
                    r.Note,
                    IsCompleted = r.IsCompleted ? 1 : 0,
                    r.DeadlineVersion,
                    CurrentStatusStr = r.CurrentStatus.ToString(),
                    r.DaysRemaining,
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
                progress AS Progress,
                result AS Result,
                note AS Note,
                is_completed AS IsCompleted,
                deadline_version AS DeadlineVersion,
                current_status AS CurrentStatusStr,
                days_remaining AS DaysRemaining
            FROM task_rows
            WHERE source_file_id = @SourceFileId AND is_current = 1
        ";

        var rawData = connection.Query(sql, new { SourceFileId = sourceFileId });

        return rawData.Select(row => new TaskRow
        {
            SourceFileId = row.SourceFileId,
            LogicalRowKey = row.LogicalRowKey,
            SnapshotId = row.SnapshotId,
            IsCurrent = row.IsCurrent != 0, // boolean mapped from integer
            SheetName = row.SheetName,
            SheetWeekNumber = (int?)row.SheetWeekNumber,
            SourceRowNumber = (int)row.SourceRowNumber,
            Stt = row.Stt,
            DocumentNumber = row.DocumentNumber,
            TaskContent = row.TaskContent,
            ExecutingUnit = row.ExecutingUnit,
            PrimaryHandler = row.PrimaryHandler,
            DeadlineRaw = row.DeadlineRaw,
            Progress = row.Progress,
            Result = row.Result,
            Note = row.Note,
            IsCompleted = row.IsCompleted != 0,
            DeadlineVersion = row.DeadlineVersion,
            CurrentStatus = Enum.TryParse<TaskStatus>((string)row.CurrentStatusStr, out var status) ? status : TaskStatus.Unknown,
            DaysRemaining = (int?)row.DaysRemaining
        }).ToList();
    }
}
