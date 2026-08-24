using System;
using TaskTracker.Domain;
using Xunit;

namespace TaskTracker.Domain.Tests;

public class DeadlineParserTests
{
    private readonly DeadlineParser _parser = new DeadlineParser();

    [Theory]
    [InlineData("29/7/2026", 2026, 7, 29)]
    [InlineData("29/07/2026", 2026, 7, 29)]
    [InlineData("29-7-2026", 2026, 7, 29)]
    public void Parse_ExactDate(string input, int year, int month, int day)
    {
        var result = _parser.ParseText(input);

        Assert.Equal(DeadlineParserKind.ExactDate, result.Kind);
        Assert.Equal(new DateOnly(year, month, day), result.StartDate);
        Assert.Equal(new DateOnly(year, month, day), result.AlertDate);
        Assert.False(result.RequiresReview);
    }

    [Theory]
    [InlineData("16h00 ngày 29/7/2026", 2026, 7, 29, 16, 0)]
    [InlineData("16H00 NGÀY 05/8/2026", 2026, 8, 5, 16, 0)]
    [InlineData("14:00 ngày 7/8/2026", 2026, 8, 7, 14, 0)]
    public void Parse_ExactDateTime(string input, int year, int month, int day, int hour, int min)
    {
        var result = _parser.ParseText(input);

        Assert.Equal(DeadlineParserKind.ExactDateTime, result.Kind);
        Assert.Equal(new DateOnly(year, month, day), result.StartDate);
        Assert.Equal(new TimeSpan(hour, min, 0), result.TimeOfDay);
        Assert.False(result.RequiresReview);
    }

    [Theory]
    [InlineData("6/8-21/8/2026", 8, 6, 8, 21, 2026)]
    [InlineData("06/08 - 21/08/2026", 8, 6, 8, 21, 2026)]
    public void Parse_DateRange(string input, int m1, int d1, int m2, int d2, int year)
    {
        var result = _parser.ParseText(input);

        Assert.Equal(DeadlineParserKind.DateRange, result.Kind);
        Assert.Equal(new DateOnly(year, m1, d1), result.StartDate);
        Assert.Equal(new DateOnly(year, m2, d2), result.EndDate);
        Assert.Equal(new DateOnly(year, m1, d1), result.AlertDate);
        Assert.False(result.RequiresReview);
    }

    [Theory]
    [InlineData("Trong tháng 7/2026", DeadlineParserKind.MonthOnly)]
    [InlineData("Trong tuần 29", DeadlineParserKind.WeekOnly)]
    [InlineData("Hằng tuần", DeadlineParserKind.RecurringUnconfigured)]
    [InlineData("14h00 ngày 7/8", DeadlineParserKind.MissingYear)]
    [InlineData("7/8", DeadlineParserKind.MissingYear)]
    public void Parse_RequiresReview(string input, DeadlineParserKind expectedKind)
    {
        var result = _parser.ParseText(input);

        Assert.Equal(expectedKind, result.Kind);
        Assert.True(result.RequiresReview);
        Assert.Null(result.AlertDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Blank(string? input)
    {
        var result = _parser.ParseText(input);

        Assert.Equal(DeadlineParserKind.Blank, result.Kind);
        Assert.False(result.RequiresReview);
    }

    [Fact]
    public void Parse_UnrecognizedPattern()
    {
        var result = _parser.ParseText("Một ngày đẹp trời trong năm 2026");

        Assert.Equal(DeadlineParserKind.Unrecognized, result.Kind);
        Assert.True(result.RequiresReview);
    }
}
