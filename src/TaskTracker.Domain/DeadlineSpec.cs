using System;
using System.Collections.Generic;

namespace TaskTracker.Domain;

public record DeadlineSpec(
    DeadlineParserKind Kind,
    string? RawValue,
    DateOnly? StartDate,
    DateOnly? EndDate,
    TimeSpan? TimeOfDay,
    DateOnly? AlertDate,
    bool RequiresReview,
    string? DiagnosticCode,
    IReadOnlyList<DateOnly>? AmbiguousCandidates = null
);
