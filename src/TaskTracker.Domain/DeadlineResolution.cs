using System;

namespace TaskTracker.Domain;

public record DeadlineResolution(
    string LogicalRowKey,
    string RawDeadlineFingerprint,
    DeadlineParserKind ParserKind,
    string? RawValue,
    DateOnly? ExcelCandidate,
    DateOnly? SwappedCandidate,
    DateOnly? SelectedStartDate,
    DateOnly? SelectedEndDate,
    TimeSpan? SelectedTime,
    ResolutionSource ResolutionSource,
    bool RequiresReview,
    DateTimeOffset UpdatedAtUtc
);
