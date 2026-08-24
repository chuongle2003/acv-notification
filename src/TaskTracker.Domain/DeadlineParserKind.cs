namespace TaskTracker.Domain;

public enum DeadlineParserKind
{
    ExactDate,
    ExactDateTime,
    DateRange,
    ExcelDateConfirmed,
    ExcelDateAmbiguous,
    MonthOnly,
    WeekOnly,
    RecurringUnconfigured,
    MissingYear,
    Blank,
    Unrecognized,
    Invalid
}
