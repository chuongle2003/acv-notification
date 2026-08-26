using System;
using System.Collections.Generic;
using TaskTracker.Domain;

namespace TaskTracker.Application;

public record NotificationActivation(
    string Action,
    string? SourceFileId,
    string? LogicalRowKey,
    string? DeadlineVersion,
    AlertGroup? AlertGroup);

/// <summary>
/// Parses the argument string produced by Windows app notification buttons.
/// Kept platform-neutral so malformed activation payloads can be tested without
/// loading the Windows App SDK into a testhost process.
/// </summary>
public static class NotificationActivationParser
{
    public static NotificationActivation Parse(string argument)
    {
        var values = ParseArguments(argument);
        values.TryGetValue("action", out var action);
        values.TryGetValue("sourceFileId", out var sourceFileId);
        values.TryGetValue("logicalRowKey", out var logicalRowKey);
        values.TryGetValue("deadlineVersion", out var deadlineVersion);
        values.TryGetValue("alertGroup", out var alertGroupText);

        AlertGroup? group = Enum.TryParse<AlertGroup>(alertGroupText, out var parsedGroup)
            ? parsedGroup : null;
        return new NotificationActivation(
            action ?? "open-list", sourceFileId, logicalRowKey, deadlineVersion, group);
    }

    private static IReadOnlyDictionary<string, string> ParseArguments(string argument)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in argument.Split(new[] { '&', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = segment.Split('=', 2);
            if (pair.Length == 2)
                result[Uri.UnescapeDataString(pair[0])] = Uri.UnescapeDataString(pair[1]);
        }
        return result;
    }
}
