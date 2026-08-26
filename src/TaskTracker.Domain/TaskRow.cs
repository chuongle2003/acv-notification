namespace TaskTracker.Domain;

public record TaskRow
{
    public string SourceFileId { get; init; } = "";
    public string LogicalRowKey { get; init; } = "";
    public string SnapshotId { get; init; } = "";
    public bool IsCurrent { get; init; }
    
    public string SheetName { get; init; } = "";
    public int? SheetWeekNumber { get; init; }
    public int SourceRowNumber { get; init; }
    
    public string? Stt { get; init; }
    public string? DocumentNumber { get; init; }
    public string? TaskContent { get; init; }
    public string? ExecutingUnit { get; init; }
    public string? PrimaryHandler { get; init; }
    public string? DeadlineRaw { get; init; }
    public string? DeadlineCellKind { get; init; }
    public int? DeadlineFormatId { get; init; }
    public string? DeadlineFormatCode { get; init; }
    public string? DeadlineCellAddress { get; init; }
    public string? Progress { get; init; }
    public string? Result { get; init; }
    public string? Note { get; init; }

    public bool IsCompleted { get; init; }
    public string? DeadlineVersion { get; init; }
    public DeadlineParserKind DeadlineKind { get; init; } = DeadlineParserKind.Unrecognized;
    public DateOnly? ExcelCandidate { get; init; }
    public DateOnly? SwappedCandidate { get; init; }
    public DateOnly? ResolvedStartDate { get; init; }
    public DateOnly? ResolvedEndDate { get; init; }
    public TimeSpan? ResolvedTime { get; init; }
    public ResolutionSource ResolutionSource { get; init; } = ResolutionSource.Parser;
    public bool RequiresReview { get; init; }
    public TaskStatus CurrentStatus { get; init; }
    public int? DaysRemaining { get; init; }
}
