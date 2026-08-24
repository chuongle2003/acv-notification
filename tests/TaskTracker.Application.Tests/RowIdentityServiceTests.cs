using System;
using System.Collections.Generic;
using System.Linq;
using TaskTracker.Application;
using TaskTracker.Domain;
using Xunit;

namespace TaskTracker.Application.Tests;

public class RowIdentityServiceTests
{
    private readonly RowIdentityService _service = new RowIdentityService();

    [Fact]
    public void GenerateLogicalRowKey_ProducesStableHash()
    {
        var key1 = _service.GenerateLogicalRowKey("file1", "TUAN 33", "123/CV-ABC", "Fix bug", "Team A", 0);
        var key2 = _service.GenerateLogicalRowKey("file1", "TUAN 33", "123/CV-ABC", "Fix bug", "Team A", 0);

        Assert.Equal(key1, key2);
        Assert.NotEmpty(key1);
    }

    [Fact]
    public void GenerateLogicalRowKey_StripsDateFromDocumentNumber()
    {
        var key1 = _service.GenerateLogicalRowKey("file1", "TUAN 33", "123/CV-ABC", "Fix bug", "Team A", 0);
        var key2 = _service.GenerateLogicalRowKey("file1", "TUAN 33", "123/CV-ABC ngay 29/07/2026", "Fix bug", "Team A", 0);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void GenerateLogicalRowKey_DifferentiatesOccurrence()
    {
        var key1 = _service.GenerateLogicalRowKey("file1", "TUAN 33", "123/CV", "Bug", "A", 0);
        var key2 = _service.GenerateLogicalRowKey("file1", "TUAN 33", "123/CV", "Bug", "A", 1);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GenerateDeadlineVersion_ProducesStableHash()
    {
        var v1 = _service.GenerateDeadlineVersion(
            DeadlineParserKind.ExactDate,
            new DateOnly(2026, 8, 24),
            new DateOnly(2026, 8, 24),
            null,
            ResolutionSource.Parser);

        var v2 = _service.GenerateDeadlineVersion(
            DeadlineParserKind.ExactDate,
            new DateOnly(2026, 8, 24),
            new DateOnly(2026, 8, 24),
            null,
            ResolutionSource.Parser);

        var v3 = _service.GenerateDeadlineVersion( // Different date
            DeadlineParserKind.ExactDate,
            new DateOnly(2026, 8, 25),
            new DateOnly(2026, 8, 25),
            null,
            ResolutionSource.Parser);

        Assert.Equal(v1, v2);
        Assert.NotEqual(v1, v3);
    }

    [Fact]
    public void AssignIdentities_AssignsOrdinalToDuplicatesWithinSameSheet()
    {
        var rows = new List<TaskRowDto>
        {
            new TaskRowDto { SheetName = "S1", DocumentNumber = "Doc1", TaskContent = "Task1" },
            new TaskRowDto { SheetName = "S1", DocumentNumber = "Doc1", TaskContent = "Task1" }, // Duplicate in S1
            new TaskRowDto { SheetName = "S2", DocumentNumber = "Doc1", TaskContent = "Task1" }  // Same data but different sheet
        };

        _service.AssignIdentities("f1", rows);

        // First two should have different keys because of occurrence ordinal
        Assert.NotEqual(rows[0].LogicalRowKey, rows[1].LogicalRowKey);

        // First and third should have different keys because of different sheet
        Assert.NotEqual(rows[0].LogicalRowKey, rows[2].LogicalRowKey);
    }
}
