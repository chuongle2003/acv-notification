namespace TaskTracker.Domain;

public enum TaskStatus
{
    Unknown = 0,
    Normal = 1,
    DueSoon = 2,
    DueToday = 3,
    Overdue = 4,
    NeedsReview = 5,
    Completed = 6
}
