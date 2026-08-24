using System;
using System.Text.RegularExpressions;
using System.Globalization;

namespace TaskTracker.Domain;

public class DeadlineParser
{
    private static readonly CultureInfo VietCulture = new CultureInfo("vi-VN");

    public DeadlineSpec ParseText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new DeadlineSpec(DeadlineParserKind.Blank, input, null, null, null, null, false, null);
        }

        var normalized = Normalize(input);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new DeadlineSpec(DeadlineParserKind.Blank, input, null, null, null, null, false, null);
        }

        // 3. Khoảng ngày đầy đủ (DateRange)
        var rangeMatch = Regex.Match(normalized, @"^(\d{1,2}/\d{1,2})(?:\/\d{4})?\s*-\s*(\d{1,2}/\d{1,2}/\d{4})$");
        if (rangeMatch.Success)
        {
            var endStr = rangeMatch.Groups[2].Value;
            if (DateOnly.TryParseExact(endStr, new[] { "d/M/yyyy", "dd/MM/yyyy" }, VietCulture, DateTimeStyles.None, out var endDate))
            {
                var startStr = rangeMatch.Groups[1].Value;
                if (!startStr.Contains(endDate.Year.ToString()))
                {
                    startStr += "/" + endDate.Year;
                }

                if (DateOnly.TryParseExact(startStr, new[] { "d/M/yyyy", "dd/MM/yyyy" }, VietCulture, DateTimeStyles.None, out var startDate))
                {
                    bool requiresReview = startDate > endDate || startDate.Year != endDate.Year;

                    return new DeadlineSpec(
                        DeadlineParserKind.DateRange,
                        input,
                        startDate,
                        endDate,
                        null,
                        startDate,
                        requiresReview,
                        requiresReview ? "CrossYearRange" : null);
                }
            }
        }

        // 4. Ngày kèm giờ đầy đủ (ExactDateTime)
        var dateTimeMatch = Regex.Match(normalized, @"^(?:(?<time>\d{1,2}h\d{0,2}|\d{1,2}:\d{2})\s+)?(?:ngay\s+)?(?<date>\d{1,2}/\d{1,2}/\d{4})$", RegexOptions.IgnoreCase);
        if (dateTimeMatch.Success && !string.IsNullOrEmpty(dateTimeMatch.Groups["time"].Value))
        {
            var timeStr = dateTimeMatch.Groups["time"].Value.Replace("h", ":");
            if (timeStr.EndsWith(":")) timeStr += "00";

            var dateStr = dateTimeMatch.Groups["date"].Value;

            if (DateOnly.TryParseExact(dateStr, new[] { "d/M/yyyy", "dd/MM/yyyy" }, VietCulture, DateTimeStyles.None, out var date) &&
                TimeSpan.TryParse(timeStr, out var time))
            {
                return new DeadlineSpec(
                    DeadlineParserKind.ExactDateTime,
                    input,
                    date,
                    date,
                    time,
                    date,
                    false,
                    null);
            }
        }

        // 5. Ngày đầy đủ (ExactDate)
        var dateMatch = Regex.Match(normalized, @"^(?:ngay\s+)?(?<date>\d{1,2}/\d{1,2}/\d{4})$", RegexOptions.IgnoreCase);
        if (dateMatch.Success)
        {
            var dateStr = dateMatch.Groups["date"].Value;
            if (DateOnly.TryParseExact(dateStr, new[] { "d/M/yyyy", "dd/MM/yyyy" }, VietCulture, DateTimeStyles.None, out var date))
            {
                return new DeadlineSpec(
                    DeadlineParserKind.ExactDate,
                    input,
                    date,
                    date,
                    null,
                    date,
                    false,
                    null);
            }
        }

        // 6. Ngày có giờ nhưng thiếu năm (MissingYear)
        var missingYearTimeMatch = Regex.Match(normalized, @"^(?:(?<time>\d{1,2}h\d{0,2}|\d{1,2}:\d{2})\s+)?(?:ngay\s+)?(?<date>\d{1,2}/\d{1,2})$", RegexOptions.IgnoreCase);
        if (missingYearTimeMatch.Success)
        {
            return new DeadlineSpec(
                DeadlineParserKind.MissingYear,
                input,
                null,
                null,
                null,
                null,
                true,
                "MissingYear");
        }

        // 7. Trong tháng
        if (Regex.IsMatch(normalized, @"^trong\s+thang\s+\d{1,2}/\d{4}$", RegexOptions.IgnoreCase))
        {
            return new DeadlineSpec(DeadlineParserKind.MonthOnly, input, null, null, null, null, true, "MonthOnly");
        }

        // 8. Trong tuần
        if (Regex.IsMatch(normalized, @"^trong\s+tuan\s+\d+$", RegexOptions.IgnoreCase))
        {
            return new DeadlineSpec(DeadlineParserKind.WeekOnly, input, null, null, null, null, true, "WeekOnly");
        }

        // 9. Hằng tuần
        if (Regex.IsMatch(normalized, @"^hang\s+tuan$", RegexOptions.IgnoreCase))
        {
            return new DeadlineSpec(DeadlineParserKind.RecurringUnconfigured, input, null, null, null, null, true, "RecurringUnconfigured");
        }

        // 10. Unrecognized
        return new DeadlineSpec(DeadlineParserKind.Unrecognized, input, null, null, null, null, true, "UnrecognizedPattern");
    }

    private string Normalize(string input)
    {
        var nfc = input.Normalize(System.Text.NormalizationForm.FormC);
        var trimmed = Regex.Replace(nfc.Trim(), @"\s+", " ");
        trimmed = Regex.Replace(trimmed, @"(\d{1,2})[Hh](\d{0,2})", "$1h$2");
        trimmed = trimmed.Replace("–", "-").Replace("—", "-");
        trimmed = Regex.Replace(trimmed, @"(\d{1,2})-(\d{1,2})-(\d{4})", "$1/$2/$3");
        return RemoveDiacritics(trimmed);
    }

    private string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
        var stringBuilder = new System.Text.StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }
        return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC).Replace("đ", "d").Replace("Đ", "D");
    }
}
