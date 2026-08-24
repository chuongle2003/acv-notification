using System;
using System.Collections.Generic;

namespace TaskTracker.Domain;

public enum ExcelDateSystem
{
    Windows1900,
    Mac1904
}

public class ExcelDateResolver
{
    public DeadlineSpec Resolve(double serialDate, ExcelDateSystem dateSystem, string? rawValue)
    {
        if (serialDate < 1 || serialDate > 2958465) // 2958465 is max for OADate (Dec 31, 9999)
        {
            return new DeadlineSpec(DeadlineParserKind.Invalid, rawValue, null, null, null, null, true, "InvalidSerialDate");
        }

        try
        {
            double adjustedSerial = dateSystem == ExcelDateSystem.Mac1904 ? serialDate + 1462 : serialDate;

            var dt = DateTime.FromOADate(adjustedSerial);
            var date = DateOnly.FromDateTime(dt);

            var candidates = new List<DateOnly> { date };
            bool isAmbiguous = false;

            // Check if day and month can be swapped (both <= 12 and they are different)
            if (date.Day <= 12 && date.Day != date.Month)
            {
                var swapped = new DateOnly(date.Year, date.Day, date.Month);
                candidates.Add(swapped);
                isAmbiguous = true;
            }

            if (isAmbiguous)
            {
                return new DeadlineSpec(
                    DeadlineParserKind.ExcelDateAmbiguous,
                    rawValue,
                    null,
                    null,
                    null,
                    null,
                    true,
                    "AmbiguousDayMonth",
                    candidates
                );
            }

            return new DeadlineSpec(
                DeadlineParserKind.ExcelDateConfirmed,
                rawValue,
                date,
                date,
                null,
                date,
                false,
                null,
                candidates
            );
        }
        catch (ArgumentException)
        {
            return new DeadlineSpec(DeadlineParserKind.Invalid, rawValue, null, null, null, null, true, "ExceptionParsingOADate");
        }
    }
}
