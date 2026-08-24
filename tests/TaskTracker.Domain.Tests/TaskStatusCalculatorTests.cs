using System;
using TaskTracker.Domain;
using TaskTracker.Domain.Tests.Fakes;
using Xunit;

namespace TaskTracker.Domain.Tests;

public class TaskStatusCalculatorTests
{
    private readonly FakeClock _clock;
    private readonly TaskStatusCalculator _calculator;

    public TaskStatusCalculatorTests()
    {
        // Setup clock to 2026-08-24
        _clock = new FakeClock(
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 8, 24)
        );
        _calculator = new TaskStatusCalculator(_clock);
    }

    [Theory]
    [InlineData("Đã hoàn thành", true)]
    [InlineData("   Đã hoàn thành   ", true)] // Trim spaces
    [InlineData("Hoàn thành", false)]
    [InlineData("ĐÃ HOÀN THÀNH", false)] // Case-sensitive
    [InlineData("Đã xong", false)]
    [InlineData("Đã báo cáo", false)]
    [InlineData("Đã giao hàng", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsCompleted_EvaluatesCorrectly(string? input, bool expected)
    {
        var result = _calculator.IsCompleted(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsCompleted_NormalizesNfc()
    {
        // "Đã hoàn thành" using FormD (decomposed)
        string decomposed = "Đã hoàn thành".Normalize(System.Text.NormalizationForm.FormD);

        // Ensure it is still recognized after normalization
        var result = _calculator.IsCompleted(decomposed);

        Assert.True(result);
    }

    [Theory]
    [InlineData(2026, 8, 24, 0)]   // Today
    [InlineData(2026, 8, 25, 1)]   // Tomorrow
    [InlineData(2026, 8, 26, 2)]   // In 2 days
    [InlineData(2026, 8, 23, -1)]  // Yesterday
    [InlineData(2026, 8, 20, -4)]  // 4 days ago
    public void CalculateDaysRemaining_CalculatesBasedOnDateOnly(int y, int m, int d, int expectedDays)
    {
        var alertDate = new DateOnly(y, m, d);
        var remaining = _calculator.CalculateDaysRemaining(alertDate);

        Assert.Equal(expectedDays, remaining);
    }

    [Fact]
    public void CalculateStatus_Completed_TakesPrecedence()
    {
        var alertDate = new DateOnly(2026, 8, 20); // Overdue

        var status = _calculator.CalculateStatus(
            isCompleted: true,
            requiresReview: false,
            alertDate: alertDate);

        Assert.Equal(TaskStatus.Completed, status);
    }

    [Fact]
    public void CalculateStatus_NeedsReview_WhenFlagIsTrue()
    {
        var alertDate = new DateOnly(2026, 8, 24); // Today

        var status = _calculator.CalculateStatus(
            isCompleted: false,
            requiresReview: true,
            alertDate: alertDate);

        Assert.Equal(TaskStatus.NeedsReview, status);
    }

    [Fact]
    public void CalculateStatus_NeedsReview_WhenAlertDateIsNull()
    {
        var status = _calculator.CalculateStatus(
            isCompleted: false,
            requiresReview: false,
            alertDate: null);

        Assert.Equal(TaskStatus.NeedsReview, status);
    }

    [Theory]
    [InlineData(2026, 8, 23, TaskStatus.Overdue)] // D-1
    [InlineData(2026, 8, 24, TaskStatus.DueToday)] // D0
    [InlineData(2026, 8, 25, TaskStatus.DueSoon)] // D+1
    [InlineData(2026, 8, 26, TaskStatus.Normal)] // D+2
    [InlineData(2026, 9, 24, TaskStatus.Normal)] // Next month
    public void CalculateStatus_ResolvesCorrectLevel(int y, int m, int d, TaskStatus expectedStatus)
    {
        var alertDate = new DateOnly(y, m, d);

        var status = _calculator.CalculateStatus(
            isCompleted: false,
            requiresReview: false,
            alertDate: alertDate);

        Assert.Equal(expectedStatus, status);
    }
}
