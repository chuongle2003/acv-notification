using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using TaskTracker.Application;
using TaskTracker.Domain;

namespace TaskTracker.Infrastructure.Excel;

/// <summary>
/// Adapter mapping ClosedXML-specific cell data onto the Application-layer port.
/// </summary>
public class ExcelReader : IExcelWorkbookReader
{
    public IReadOnlyList<ExcelRowData> ReadWorkbook(Stream stream)
    {
        var rows = new List<ExcelRowData>();
        using var workbook = new XLWorkbook(stream);

        var dateSystem = workbook.Use1904DateSystem
            ? ExcelDateSystem.Mac1904
            : ExcelDateSystem.Windows1900;

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
                RawDeadlineCellData? deadlineCell = null;
                if (colMap.TryGetValue("Thời hạn", out var dlCol))
                {
                    var cell = row.Cell(dlCol);
                    double? numeric = cell.DataType switch
                    {
                        XLDataType.Number => cell.GetDouble(),
                        XLDataType.DateTime => ToExcelSerial(cell.GetDateTime(), dateSystem),
                        _ => null
                    };
                    deadlineCell = new RawDeadlineCellData(
                        cell.DataType.ToString(),
                        cell.GetString(),
                        numeric,
                        cell.Style.NumberFormat.NumberFormatId,
                        cell.Style.NumberFormat.Format ?? "",
                        cell.Address.ToStringRelative()
                    );
                }

                rows.Add(new ExcelRowData
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
                    DateSystem = dateSystem
                });
            }
        }

        return rows;
    }

    /// <summary>Converts a DateTime to the Excel 1900-system serial number.</summary>
    private static double ToExcelSerial(DateTime value, ExcelDateSystem dateSystem)
    {
        var serial = value.ToOADate();
        return dateSystem == ExcelDateSystem.Mac1904 ? serial - 1462 : serial;
    }

    private int? ExtractWeekNumber(string sheetName)    {
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
