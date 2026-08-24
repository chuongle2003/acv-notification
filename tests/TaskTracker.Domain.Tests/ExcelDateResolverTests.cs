using System;
using TaskTracker.Domain;
using Xunit;

namespace TaskTracker.Domain.Tests;

public class ExcelDateResolverTests
{
    private readonly ExcelDateResolver _resolver = new ExcelDateResolver();

    [Fact]
    public void Resolve_ConfirmedDate_NoAmbiguity()
    {
        // 29/07/2026 -> 29 > 12, so no ambiguity
        // Date: 2026-07-29. In Windows 1900 system, the serial is 46232
        var dt = new DateTime(2026, 7, 29);
        var serial = dt.ToOADate();

        var result = _resolver.Resolve(serial, ExcelDateSystem.Windows1900, serial.ToString());

        Assert.Equal(DeadlineParserKind.ExcelDateConfirmed, result.Kind);
        Assert.Equal(new DateOnly(2026, 7, 29), result.StartDate);
        Assert.False(result.RequiresReview);
        Assert.Null(result.DiagnosticCode);
        Assert.NotNull(result.AmbiguousCandidates);
        Assert.Single(result.AmbiguousCandidates);
        Assert.Equal(new DateOnly(2026, 7, 29), result.AmbiguousCandidates[0]);
    }

    [Fact]
    public void Resolve_AmbiguousDate_BothDayAndMonthUnder13()
    {
        // 04/08/2026 -> Day 4 <= 12, Month 8 <= 12, they are different -> Ambiguous!
        // Date: 2026-08-04. In Windows 1900 system, serial is 46238
        var dt = new DateTime(2026, 8, 4);
        var serial = dt.ToOADate();

        var result = _resolver.Resolve(serial, ExcelDateSystem.Windows1900, serial.ToString());

        Assert.Equal(DeadlineParserKind.ExcelDateAmbiguous, result.Kind);
        Assert.Null(result.StartDate);
        Assert.True(result.RequiresReview);
        Assert.Equal("AmbiguousDayMonth", result.DiagnosticCode);

        Assert.NotNull(result.AmbiguousCandidates);
        Assert.Equal(2, result.AmbiguousCandidates.Count);
        Assert.Equal(new DateOnly(2026, 8, 4), result.AmbiguousCandidates[0]);
        Assert.Equal(new DateOnly(2026, 4, 8), result.AmbiguousCandidates[1]); // Swapped
    }

    [Fact]
    public void Resolve_SameDayAndMonth_NoAmbiguity()
    {
        // 12/12/2026 -> Day 12 <= 12, Month 12 <= 12, but they are the same -> Not Ambiguous!
        // Date: 2026-12-12. In Windows 1900 system, serial is 46368
        var dt = new DateTime(2026, 12, 12);
        var serial = dt.ToOADate();

        var result = _resolver.Resolve(serial, ExcelDateSystem.Windows1900, serial.ToString());

        Assert.Equal(DeadlineParserKind.ExcelDateConfirmed, result.Kind);
        Assert.Equal(new DateOnly(2026, 12, 12), result.StartDate);
        Assert.False(result.RequiresReview);
        Assert.NotNull(result.AmbiguousCandidates);
        Assert.Single(result.AmbiguousCandidates);
    }

    [Fact]
    public void Resolve_Mac1904System()
    {
        // 04/08/2026 in Mac 1904 system
        // The serial difference is exactly 1462 days
        var dt = new DateTime(2026, 8, 4);
        var serial1900 = dt.ToOADate();
        var serial1904 = serial1900 - 1462;

        var result = _resolver.Resolve(serial1904, ExcelDateSystem.Mac1904, serial1904.ToString());

        Assert.Equal(DeadlineParserKind.ExcelDateAmbiguous, result.Kind);
        Assert.Equal(new DateOnly(2026, 8, 4), result.AmbiguousCandidates![0]);
        Assert.Equal(new DateOnly(2026, 4, 8), result.AmbiguousCandidates[1]);
    }

    [Fact]
    public void Resolve_InvalidSerial()
    {
        var result = _resolver.Resolve(-1, ExcelDateSystem.Windows1900, "-1");

        Assert.Equal(DeadlineParserKind.Invalid, result.Kind);
        Assert.True(result.RequiresReview);
        Assert.Equal("InvalidSerialDate", result.DiagnosticCode);
    }
}
