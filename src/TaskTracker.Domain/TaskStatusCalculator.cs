using System;
using System.Text;
using System.Text.RegularExpressions;

namespace TaskTracker.Domain;

public class TaskStatusCalculator
{
    private readonly IClock _clock;

    public TaskStatusCalculator(IClock clock)
    {
        _clock = clock;
    }

    public bool IsCompleted(string? resultColumnText)
    {
        if (string.IsNullOrWhiteSpace(resultColumnText))
            return false;

        var normalized = Normalize(resultColumnText);
        return string.Equals(normalized, "Đã hoàn thành", StringComparison.Ordinal);
    }

    public int? CalculateDaysRemaining(DateOnly? alertDate)
    {
        if (alertDate == null) return null;

        var today = _clock.TodayLocal;
        return alertDate.Value.DayNumber - today.DayNumber;
    }

    public TaskStatus CalculateStatus(bool isCompleted, bool requiresReview, DateOnly? alertDate)
    {
        if (isCompleted)
        {
            return TaskStatus.Completed;
        }

        if (requiresReview || alertDate == null)
        {
            return TaskStatus.NeedsReview;
        }

        var daysRemaining = CalculateDaysRemaining(alertDate);
        if (daysRemaining == null) // Fallback just in case
        {
            return TaskStatus.NeedsReview;
        }

        if (daysRemaining.Value < 0)
        {
            return TaskStatus.Overdue;
        }

        if (daysRemaining.Value == 0)
        {
            return TaskStatus.DueToday;
        }

        if (daysRemaining.Value == 1)
        {
            return TaskStatus.DueSoon;
        }

        return TaskStatus.Normal;
    }

    private string Normalize(string input)
    {
        var nfc = input.Normalize(NormalizationForm.FormC);
        return nfc.Trim();
    }
}
