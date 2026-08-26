using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using TaskTracker.Application;
using TaskTracker.Domain;

namespace TaskTracker.Infrastructure.Persistence;

public class SqliteDeadlineResolutionRepository : IResolutionStore
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteDeadlineResolutionRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public void Upsert(DeadlineResolution resolution)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO deadline_resolutions (
                id, logical_row_key, raw_deadline_fingerprint, parser_kind,
                raw_value, excel_candidate, swapped_candidate,
                selected_start_date, selected_end_date, selected_time,
                resolution_source, requires_review, updated_at_utc
            ) VALUES (
                @Id, @LogicalRowKey, @RawDeadlineFingerprint, @ParserKindStr,
                @RawValue, @ExcelCandidate, @SwappedCandidate,
                @SelectedStartDate, @SelectedEndDate, @SelectedTime,
                @ResolutionSourceStr, @RequiresReview, @UpdatedAtUtc
            )
            ON CONFLICT(logical_row_key, raw_deadline_fingerprint) DO UPDATE SET
                parser_kind = excluded.parser_kind,
                raw_value = excluded.raw_value,
                excel_candidate = excluded.excel_candidate,
                swapped_candidate = excluded.swapped_candidate,
                selected_start_date = excluded.selected_start_date,
                selected_end_date = excluded.selected_end_date,
                selected_time = excluded.selected_time,
                resolution_source = excluded.resolution_source,
                requires_review = excluded.requires_review,
                updated_at_utc = excluded.updated_at_utc
        ";

        connection.Execute(sql, new
        {
            Id = Guid.NewGuid().ToString("N"),
            resolution.LogicalRowKey,
            resolution.RawDeadlineFingerprint,
            ParserKindStr = resolution.ParserKind.ToString(),
            resolution.RawValue,
            ExcelCandidate = DateOnlyToString(resolution.ExcelCandidate),
            SwappedCandidate = DateOnlyToString(resolution.SwappedCandidate),
            SelectedStartDate = DateOnlyToString(resolution.SelectedStartDate),
            SelectedEndDate = DateOnlyToString(resolution.SelectedEndDate),
            SelectedTime = TimeSpanToString(resolution.SelectedTime),
            ResolutionSourceStr = resolution.ResolutionSource.ToString(),
            RequiresReview = resolution.RequiresReview ? 1 : 0,
            UpdatedAtUtc = resolution.UpdatedAtUtc.ToString("o")
        });
    }

    public IReadOnlyList<DeadlineResolution> GetAll()
    {
        using var connection = _connectionFactory.CreateConnection();
        var sql = @"
            SELECT
                logical_row_key AS LogicalRowKey,
                raw_deadline_fingerprint AS RawDeadlineFingerprint,
                parser_kind AS ParserKindStr,
                raw_value AS RawValue,
                excel_candidate AS ExcelCandidate,
                swapped_candidate AS SwappedCandidate,
                selected_start_date AS SelectedStartDate,
                selected_end_date AS SelectedEndDate,
                selected_time AS SelectedTime,
                resolution_source AS ResolutionSourceStr,
                requires_review AS RequiresReview,
                updated_at_utc AS UpdatedAtUtc
            FROM deadline_resolutions
        ";

        var rawData = connection.Query(sql);
        return rawData.Select(row => new DeadlineResolution(
            (string)row.LogicalRowKey,
            (string)row.RawDeadlineFingerprint,
            Enum.TryParse<DeadlineParserKind>((string)row.ParserKindStr, out var kind) ? kind : DeadlineParserKind.Unrecognized,
            (string?)row.RawValue,
            StringToDateOnly(row.ExcelCandidate),
            StringToDateOnly(row.SwappedCandidate),
            StringToDateOnly(row.SelectedStartDate),
            StringToDateOnly(row.SelectedEndDate),
            StringToTimeSpan(row.SelectedTime),
            Enum.TryParse<ResolutionSource>((string)row.ResolutionSourceStr, out var src) ? src : ResolutionSource.Parser,
            row.RequiresReview != 0,
            DateTimeOffset.Parse((string)row.UpdatedAtUtc)
        )).ToList();
    }

    public DeadlineResolution? FindByKey(string logicalRowKey, string rawDeadlineFingerprint)
    {
        return GetAll().FirstOrDefault(r =>
            r.LogicalRowKey == logicalRowKey &&
            r.RawDeadlineFingerprint == rawDeadlineFingerprint);
    }

    public void Delete(string logicalRowKey, string rawDeadlineFingerprint)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Execute(@"
            DELETE FROM deadline_resolutions
            WHERE logical_row_key = @LogicalRowKey
              AND raw_deadline_fingerprint = @RawDeadlineFingerprint
        ", new { LogicalRowKey = logicalRowKey, RawDeadlineFingerprint = rawDeadlineFingerprint });
    }

    private static string? DateOnlyToString(DateOnly? date) => date?.ToString("yyyy-MM-dd");
    private static string? TimeSpanToString(TimeSpan? time) => time?.ToString(@"hh\:mm");
    private static DateOnly? StringToDateOnly(object? value) =>
        value == null || string.IsNullOrWhiteSpace(value.ToString())
            ? null : DateOnly.Parse(value.ToString()!);
    private static TimeSpan? StringToTimeSpan(object? value) =>
        value == null || string.IsNullOrWhiteSpace(value.ToString())
            ? null : TimeSpan.Parse(value.ToString()!);
}
