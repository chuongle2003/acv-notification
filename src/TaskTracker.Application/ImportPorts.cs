using System;
using System.Collections.Generic;
using TaskTracker.Domain;
using TaskStatus = TaskTracker.Domain.TaskStatus;

namespace TaskTracker.Application;

/// <summary>
/// Storage-neutral view of a raw Excel deadline cell (no ClosedXML types).
/// </summary>
public record RawDeadlineCellData(
    string CellKind,        // "Text" | "Number" | "DateTime" | "Blank" ...
    string? TextValue,
    double? NumericValue,
    int FormatId,
    string FormatCode,
    string CellAddress
);

/// <summary>
/// Storage-neutral view of one Excel data row.
/// </summary>
public record ExcelRowData
{
    public string SheetName { get; init; } = "";
    public int? SheetWeekNumber { get; init; }
    public int SourceRowNumber { get; init; }
    public string? Stt { get; init; }
    public string? DocumentNumber { get; init; }
    public string? TaskContent { get; init; }
    public string? ExecutingUnit { get; init; }
    public string? PrimaryHandler { get; init; }
    public RawDeadlineCellData? DeadlineCell { get; init; }
    public string? Progress { get; init; }
    public string? Result { get; init; }
    public string? Note { get; init; }
    public ExcelDateSystem DateSystem { get; init; }
}

/// <summary>
/// Port for reading an Excel workbook, implemented by Infrastructure (ClosedXML).
/// </summary>
public interface IExcelWorkbookReader
{
    IReadOnlyList<ExcelRowData> ReadWorkbook(System.IO.Stream stream);
}

/// <summary>
/// Port for task-row persistence, implemented by Infrastructure (SQLite).
/// </summary>
public interface ITaskRowStore
{
    void CommitSnapshot(string snapshotId, string sourceFileId, IReadOnlyList<TaskRow> currentRows);
    IReadOnlyList<TaskRow> GetCurrentRows(string sourceFileId);
    void UpdateDeadlineForCorrection(string sourceFileId, string logicalRowKey, DeadlineCorrectionUpdate update);
}

public record DeadlineCorrectionUpdate(
    string DeadlineVersion,
    DeadlineParserKind DeadlineKind,
    DateOnly? ExcelCandidate,
    DateOnly? SwappedCandidate,
    DateOnly? ResolvedStartDate,
    DateOnly? ResolvedEndDate,
    TimeSpan? ResolvedTime,
    ResolutionSource ResolutionSource,
    bool RequiresReview,
    TaskStatus Status,
    int? DaysRemaining,
    string SnapshotId);
