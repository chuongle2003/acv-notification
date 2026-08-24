using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TaskTracker.Domain;

namespace TaskTracker.Application;

public class RowIdentityService
{
    public string GenerateLogicalRowKey(
        string sourceFileId,
        string sheetName,
        string? documentNumber,
        string? taskContent,
        string? primaryHandler,
        int occurrenceOrdinal)
    {
        var normalizedSheet = NormalizeStrict(sheetName);
        var normalizedDocNum = NormalizeDocumentNumber(documentNumber);
        var normalizedContent = NormalizeStrict(taskContent);
        var normalizedHandler = NormalizeStrict(primaryHandler);

        // Combine parts with a distinct separator
        var rawKey = $"{sourceFileId}|{normalizedSheet}|{normalizedDocNum}|{normalizedContent}|{normalizedHandler}|{occurrenceOrdinal}";

        return ComputeHash(rawKey);
    }

    public string GenerateDeadlineVersion(
        DeadlineParserKind resolvedKind,
        DateOnly? startDate,
        DateOnly? endDate,
        TimeSpan? timeOfDay,
        ResolutionSource currentResolutionSource)
    {
        var rawString = $"{resolvedKind}|{startDate:yyyy-MM-dd}|{endDate:yyyy-MM-dd}|{timeOfDay}|{currentResolutionSource}";
        return ComputeHash(rawString);
    }

    public void AssignIdentities(string sourceFileId, IEnumerable<TaskRowDto> rows)
    {
        // Tracks occurrences of the exact same data within the same sheet
        var occurrenceTracker = new Dictionary<string, int>();

        foreach (var row in rows)
        {
            var normalizedSheet = NormalizeStrict(row.SheetName);
            var normalizedDocNum = NormalizeDocumentNumber(row.DocumentNumber);
            var normalizedContent = NormalizeStrict(row.TaskContent);
            var normalizedHandler = NormalizeStrict(row.PrimaryHandler);

            var baseIdentityStr = $"{normalizedSheet}|{normalizedDocNum}|{normalizedContent}|{normalizedHandler}";

            if (!occurrenceTracker.TryGetValue(baseIdentityStr, out int count))
            {
                count = 0;
            }

            occurrenceTracker[baseIdentityStr] = count + 1;

            row.LogicalRowKey = GenerateLogicalRowKey(
                sourceFileId,
                row.SheetName,
                row.DocumentNumber,
                row.TaskContent,
                row.PrimaryHandler,
                count);
        }
    }

    private string NormalizeStrict(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var nfc = input.Normalize(NormalizationForm.FormC);
        var trimmed = Regex.Replace(nfc.Trim(), @"\s+", " ");
        return trimmed; // Note: Case is preserved in hash, but we could lower it if we want case-insensitivity. The spec says "chỉ chuẩn hóa Unicode/khoảng trắng".
    }

    private string NormalizeDocumentNumber(string? docNum)
    {
        if (string.IsNullOrWhiteSpace(docNum)) return string.Empty;

        var normalized = NormalizeStrict(docNum);

        // Strip trailing dates like "123/CV-ABC ngày 29/07/2026" -> "123/CV-ABC"
        var match = Regex.Match(normalized, @"^(.*?)(?:\s+ngay\s+\d{1,2}[/-]\d{1,2}[/-]\d{4})?$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return normalized;
    }

    private string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

// Temporary DTO just for identity assignment before saving to DB
public class TaskRowDto
{
    public string SheetName { get; set; } = "";
    public string? DocumentNumber { get; set; }
    public string? TaskContent { get; set; }
    public string? PrimaryHandler { get; set; }
    public string LogicalRowKey { get; set; } = "";
}
