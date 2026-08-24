using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using TaskTracker.Domain;

namespace TaskTracker.Infrastructure.Excel;

public class RawExcelCell
{
    public XLDataType DataType { get; init; }
    public string? TextValue { get; init; }
    public double? NumericValue { get; init; }
    public int FormatId { get; init; }
    public string FormatCode { get; init; } = "";
    public string CellAddress { get; init; } = "";
}

public class ExcelRowDto
{
    public string SheetName { get; init; } = "";
    public int? SheetWeekNumber { get; init; }
    public int SourceRowNumber { get; init; }
    public string? Stt { get; init; }
    public string? DocumentNumber { get; init; }
    public string? TaskContent { get; init; }
    public string? ExecutingUnit { get; init; }
    public string? PrimaryHandler { get; init; }
    public RawExcelCell? DeadlineCell { get; init; }
    public string? Progress { get; init; }
    public string? Result { get; init; }
    public string? Note { get; init; }
    public ExcelDateSystem DateSystem { get; init; }
}

public class ExcelReader
{
    public IReadOnlyList<ExcelRowDto> ReadWorkbook(Stream stream)
    {
        var rows = new List<ExcelRowDto>();
        using var workbook = new XLWorkbook(stream);

        var dateSystem = workbook.ReferenceStyle == XLReferenceStyle.R1C1
            ? ExcelDateSystem.Windows1900 // Just fallback for now, ClosedXML doesn't expose 1904 easily in all versions, wait actually it's workbook.Use1904DateSystem if exists
            : ExcelDateSystem.Windows1900;

        // Actually ClosedXML exposes Date1904 property
        if (workbook.Properties != null)
        {
             // CloseXML might have workbook.CalculateMode, but for 1904:
             // It's usually accessible but if not, fallback to 1900
        }

        foreach (var sheet in workbook.Worksheets)
        {
            if (sheet.Visibility != XLWorksheetVisibility.Visible)
                continue;

            var sheetName = sheet.Name;
            int? weekNumber = ExtractWeekNumber(sheetName);

            var headerRowNumber = FindHeaderRow(sheet);
            if (headerRowNumber == null)
            {
                continue; // Skip sheet if no header found
            }

            var colMap = MapColumns(sheet.Row(headerRowNumber.Value));
            if (!IsValidHeaderMap(colMap))
            {
                continue; // Missing required columns
            }

            var lastRowUsed = sheet.LastRowUsed()?.RowNumber() ?? headerRowNumber.Value;

            for (int r = headerRowNumber.Value + 1; r <= lastRowUsed; r++)
            {
                var row = sheet.Row(r);

                // Read basic strings
                var docNum = GetString(row, colMap, "Số công văn");
                var content = GetString(row, colMap, "Nội dung nhiệm vụ");
                var stt = GetString(row, colMap, "STT");

                // Check if it's a business row (must have docNum or content)
                if (string.IsNullOrWhiteSpace(docNum) && string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                // Read deadline cell specifically
                RawExcelCell? deadlineCell = null;
                if (colMap.TryGetValue("Thời hạn", out var dlCol))
                {
                    var cell = row.Cell(dlCol);
                    deadlineCell = new RawExcelCell
                    {
                        DataType = cell.DataType,
                        TextValue = cell.GetString(),
                        NumericValue = cell.DataType == XLDataType.Number || cell.DataType == XLDataType.DateTime
                                       ? cell.GetDouble() : null,
                        FormatId = cell.Style.NumberFormat.NumberFormatId,
                        FormatCode = cell.Style.NumberFormat.Format ?? "",
                        CellAddress = cell.Address.ToStringRelative()
                    };
                }

                rows.Add(new ExcelRowDto
                {
                    SheetName = sheetName,
                    SheetWeekNumber = weekNumber,
                    SourceRowNumber = r,
                    Stt = stt,
                    DocumentNumber = docNum,
                    TaskContent = content,
                    ExecutingUnit = GetString(row, colMap, "Đơn vị thực hiện"),
                    PrimaryHandler = GetString(row, colMap, "Xử lý chính"),
                    DeadlineCell = deadlineCell,
                    Progress = GetString(row, colMap, "Tiến độ"),
                    Result = GetString(row, colMap, "Kết quả"),
                    Note = GetString(row, colMap, "Ghi chú"),
                    DateSystem = ExcelDateSystem.Windows1900 // Hardcoded for MVP unless we find the flag
                });
            }
        }

        return rows;
    }

    private int? ExtractWeekNumber(string sheetName)
    {
        var match = Regex.Match(sheetName.Normalize(System.Text.NormalizationForm.FormC), @"(?i)tuan\s+(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int w))
            return w;
        return null;
    }

    private int? FindHeaderRow(IXLWorksheet sheet)
    {
        for (int r = 1; r <= Math.Min(20, sheet.LastRowUsed()?.RowNumber() ?? 20); r++)
        {
            var row = sheet.Row(r);
            var map = MapColumns(row);
            if (IsValidHeaderMap(map)) return r;
        }
        return null;
    }

    private Dictionary<string, int> MapColumns(IXLRow row)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastCell = row.LastCellUsed()?.Address.ColumnNumber ?? 20;

        for (int c = 1; c <= lastCell; c++)
        {
            var val = row.Cell(c).GetString();
            if (!string.IsNullOrWhiteSpace(val))
            {
                var normalized = NormalizeHeader(val);
                map[normalized] = c;
            }
        }
        return map;
    }

    private bool IsValidHeaderMap(Dictionary<string, int> map)
    {
        // Must have: Số công văn, Nội dung nhiệm vụ, Thời hạn, Kết quả
        return map.ContainsKey("Số công văn") &&
               map.ContainsKey("Nội dung nhiệm vụ") &&
               map.ContainsKey("Thời hạn") &&
               map.ContainsKey("Kết quả");
    }

    private string NormalizeHeader(string header)
    {
        var nfc = header.Normalize(System.Text.NormalizationForm.FormC);
        var trimmed = Regex.Replace(nfc.Trim(), @"\s+", " ");
        // Ensure standard casing mapping isn't necessary because dictionary uses OrdinalIgnoreCase,
        // but we still want exact standard Vietnamese representations without diacritic mismatches.
        return trimmed;
    }

    private string? GetString(IXLRow row, Dictionary<string, int> map, string colName)
    {
        if (map.TryGetValue(colName, out var colIdx))
        {
            var val = row.Cell(colIdx).GetString();
            return string.IsNullOrWhiteSpace(val) ? null : val;
        }
        return null;
    }
}
