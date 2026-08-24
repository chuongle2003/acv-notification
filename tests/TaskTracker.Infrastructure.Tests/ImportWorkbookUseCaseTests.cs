using System;
using System.IO;
using ClosedXML.Excel;
using TaskTracker.Application;
using TaskTracker.Domain;
using TaskTracker.Domain.Tests.Fakes;
using TaskTracker.Infrastructure.Excel;
using TaskTracker.Infrastructure.Persistence;
using Xunit;

namespace TaskTracker.Infrastructure.Tests;

public class ImportWorkbookUseCaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ImportWorkbookUseCase _useCase;
    private readonly SqliteTaskRepository _repository;

    public ImportWorkbookUseCaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"import_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath};Mode=ReadWriteCreate";

        var factory = new SqliteConnectionFactory(connectionString);
        var migrator = new DatabaseMigrator(factory);
        migrator.MigrateUp();

        _repository = new SqliteTaskRepository(factory);

        var clock = new FakeClock(DateTimeOffset.UtcNow, new DateOnly(2026, 8, 24));
        var reader = new ExcelReader();
        var identityService = new RowIdentityService();
        var deadlineParser = new DeadlineParser();
        var excelResolver = new ExcelDateResolver();
        var statusCalculator = new TaskStatusCalculator(clock);

        _useCase = new ImportWorkbookUseCase(
            reader,
            identityService,
            deadlineParser,
            excelResolver,
            statusCalculator,
            _repository,
            clock);
    }

    private Stream CreateValidExcel()
    {
        var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.Worksheets.Add("TUAN 33");
            ws.Cell("A1").Value = "STT";
            ws.Cell("B1").Value = "Số công văn";
            ws.Cell("C1").Value = "Nội dung nhiệm vụ";
            ws.Cell("D1").Value = "Thời hạn";
            ws.Cell("E1").Value = "Kết quả";
            ws.Cell("F1").Value = "Xử lý chính"; // Add mapping for missing things

            // Row 1: Valid date
            ws.Cell("A2").Value = "1";
            ws.Cell("B2").Value = "123/CV";
            ws.Cell("C2").Value = "Test Content";
            ws.Cell("D2").Value = "29/08/2026";
            ws.Cell("E2").Value = "";

            // Row 2: Ambiguous Excel Date
            ws.Cell("A3").Value = "2";
            ws.Cell("B3").Value = "124/CV";
            ws.Cell("C3").Value = "Test Ambiguous";
            ws.Cell("D3").Value = new DateTime(2026, 8, 4); // 04/08/2026 is ambiguous
            ws.Cell("E3").Value = "";

            // Row 3: Completed
            ws.Cell("A4").Value = "3";
            ws.Cell("B4").Value = "125/CV";
            ws.Cell("C4").Value = "Test Completed";
            ws.Cell("D4").Value = "24/08/2026"; // Due Today
            ws.Cell("E4").Value = "Đã hoàn thành"; // Should override to Completed

            wb.SaveAs(ms);
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Execute_SuccessfullyImportsAndCalculatesStatus()
    {
        using var stream = CreateValidExcel();
        var fileId = "test_file_id";

        var diagnostics = _useCase.Execute(fileId, stream);

        Assert.Null(diagnostics.ErrorMessage);
        Assert.Equal(3, diagnostics.TotalRowsFound);
        Assert.Equal(3, diagnostics.ValidRowsImported);
        Assert.Equal(1, diagnostics.AmbiguousDatesDetected); // Row 2
        Assert.Equal(0, diagnostics.ParseErrors);

        var rows = _repository.GetCurrentRows(fileId);
        Assert.Equal(3, rows.Count);

        // Row 1: 29/08 is +5 days -> Normal
        var row1 = rows.Find(r => r.DocumentNumber == "123/CV");
        Assert.NotNull(row1);
        Assert.Equal(TaskStatus.Normal, row1!.CurrentStatus);
        Assert.False(row1.IsCompleted);
        Assert.Equal(5, row1.DaysRemaining);

        // Row 2: Ambiguous
        var row2 = rows.Find(r => r.DocumentNumber == "124/CV");
        Assert.NotNull(row2);
        Assert.Equal(TaskStatus.NeedsReview, row2!.CurrentStatus);

        // Row 3: Completed
        var row3 = rows.Find(r => r.DocumentNumber == "125/CV");
        Assert.NotNull(row3);
        Assert.Equal(TaskStatus.Completed, row3!.CurrentStatus);
        Assert.True(row3.IsCompleted);
    }

    [Fact]
    public void Execute_OnCorruptedStream_ReturnsErrorDiagnostics_DoesNotCrash()
    {
        using var corruptedStream = new MemoryStream(new byte[] { 0, 1, 2, 3, 4 });
        var fileId = "test_file_id";

        var diagnostics = _useCase.Execute(fileId, corruptedStream);

        Assert.NotNull(diagnostics.ErrorMessage);
        Assert.Equal(0, diagnostics.TotalRowsFound);
        Assert.Equal(0, diagnostics.ValidRowsImported);

        var rows = _repository.GetCurrentRows(fileId);
        Assert.Empty(rows); // State is unaffected
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }
}
