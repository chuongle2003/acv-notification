using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using TaskTracker.Domain;
using TaskStatus = TaskTracker.Domain.TaskStatus;
using TaskTracker.Infrastructure.Excel;
using Xunit;

namespace TaskTracker.Infrastructure.Tests;

public class ExcelReaderTests
{
    private Stream CreateSampleExcel()
    {
        var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.Worksheets.Add("TUAN 33");

            // Row 1, 2: Noise
            ws.Cell("A1").Value = "BÁO CÁO CÔNG VIỆC";
            ws.Cell("A2").Value = "Tuần 33";

            // Row 3: Headers
            ws.Cell("A3").Value = "STT";
            ws.Cell("B3").Value = "Số công văn";
            ws.Cell("C3").Value = "Nội dung nhiệm vụ";
            ws.Cell("D3").Value = "Đơn vị thực hiện";
            ws.Cell("E3").Value = "Xử lý chính";
            ws.Cell("F3").Value = "Thời hạn";
            ws.Cell("G3").Value = "Tiến độ";
            ws.Cell("H3").Value = "Kết quả";
            ws.Cell("I3").Value = "Ghi chú";

            // Row 4: Group row (Should be skipped as it has no doc num or content)
            ws.Cell("A4").Value = "ĐỘI KỸ THUẬT";

            // Row 5: Valid task
            ws.Cell("A5").Value = "1";
            ws.Cell("B5").Value = "123/CV-ABC";
            ws.Cell("C5").Value = "Fix bug login";
            ws.Cell("F5").Value = "29/07/2026"; // text date
            ws.Cell("H5").Value = "Đã hoàn thành";

            // Row 6: Valid task with numeric date
            ws.Cell("A6").Value = "2";
            ws.Cell("B6").Value = "124/CV-ABC";
            ws.Cell("C6").Value = "Deploy server";
            ws.Cell("F6").Value = new DateTime(2026, 8, 4); // Will be stored as numeric by ClosedXML
            ws.Cell("F6").Style.NumberFormat.Format = "dd/MM/yyyy";

            // Row 7: Empty row

            // Row 8: Valid task with empty STT
            ws.Cell("B8").Value = "125/CV-ABC";
            ws.Cell("C8").Value = "Test system";

            // Row 9: Duplicate STT
            ws.Cell("A9").Value = "1"; // Same STT as row 5
            ws.Cell("C9").Value = "Just another task";

            wb.SaveAs(ms);
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void ReadWorkbook_ReadsValidRowsAndSkipsNoise()
    {
        var reader = new ExcelReader();
        using var stream = CreateSampleExcel();

        var rows = reader.ReadWorkbook(stream);

        // Expecting rows 5, 6, 8, 9
        Assert.Equal(4, rows.Count);

        var row5 = rows.First(r => r.SourceRowNumber == 5);
        Assert.Equal("TUAN 33", row5.SheetName);
        Assert.Equal(33, row5.SheetWeekNumber);
        Assert.Equal("123/CV-ABC", row5.DocumentNumber);
        Assert.Equal("Fix bug login", row5.TaskContent);
        Assert.Equal("29/07/2026", row5.DeadlineCell?.TextValue);
        Assert.Equal("Text", row5.DeadlineCell?.CellKind);
        Assert.Equal("Đã hoàn thành", row5.Result);

        var row6 = rows.First(r => r.SourceRowNumber == 6);
        Assert.Equal("DateTime", row6.DeadlineCell?.CellKind);
        Assert.True(row6.DeadlineCell?.NumericValue.HasValue);

        var row8 = rows.First(r => r.SourceRowNumber == 8);
        Assert.Null(row8.Stt);

        var row9 = rows.First(r => r.SourceRowNumber == 9);
        Assert.Equal("1", row9.Stt);
    }

    [Fact]
    public void ReadWorkbook_PreservesMac1904DateSystem()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            workbook.Use1904DateSystem = true;
            var sheet = workbook.Worksheets.Add("TUAN 34");
            sheet.Cell("A1").Value = "Số công văn";
            sheet.Cell("B1").Value = "Nội dung nhiệm vụ";
            sheet.Cell("C1").Value = "Thời hạn";
            sheet.Cell("D1").Value = "Kết quả";
            sheet.Cell("A2").Value = "1904/CV";
            sheet.Cell("B2").Value = "Kiểm tra hệ ngày";
            sheet.Cell("C2").Value = new DateTime(2026, 8, 29);
            workbook.SaveAs(stream);
        }
        stream.Position = 0;

        var row = Assert.Single(new ExcelReader().ReadWorkbook(stream));
        Assert.Equal(ExcelDateSystem.Mac1904, row.DateSystem);
        var resolved = new ExcelDateResolver().Resolve(
            row.DeadlineCell!.NumericValue!.Value, row.DateSystem, row.DeadlineCell.TextValue);
        Assert.Equal(new DateOnly(2026, 8, 29), resolved.StartDate);
    }
}
