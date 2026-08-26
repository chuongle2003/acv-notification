using System;
using System.Collections.Generic;

namespace TaskTracker.Application;

/// <summary>
/// User-adjustable application settings. Persisted via ISettingsStore.
/// </summary>
public class AppSettings
{
    /// <summary>Stable identifier for the selected source across app restarts.</summary>
    public string SourceFileId { get; set; } = "";

    /// <summary>Path of the monitored Excel workbook. Empty when unset.</summary>
    public string SourceFilePath { get; set; } = "";

    /// <summary>When true, all toast notifications are suppressed.</summary>
    public bool NotificationsPaused { get; set; }

    /// <summary>Minutes between re-notifications for still-active alerts. MVP: fixed 12h, read-only UI.</summary>
    public int RepeatIntervalMinutes { get; set; } = 720;

    /// <summary>When true, the app registers itself in HKCU\Run (start with Windows).</summary>
    public bool StartWithWindows { get; set; }
}

/// <summary>
/// Port for durable settings storage, implemented by Infrastructure (SQLite settings table).
/// </summary>
public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}

/// <summary>
/// Port for the per-user OS auto-start registration (HKCU\Run on Windows).
/// </summary>
public interface IAutoStartRegistrar
{
    void Enable(string executablePath, string[] args);
    void Disable();
    bool IsEnabled();
}

public class SettingsService
{
    public const int FixedRepeatIntervalMinutes = 720; // 12h — MVP read-only value

    private readonly ISettingsStore _store;

    public SettingsService(ISettingsStore store)
    {
        _store = store;
    }

    public AppSettings Load()
    {
        var settings = _store.Load();

        // Preserve rows imported by older builds, which derived the source id
        // from the normalized path rather than persisting it in settings.
        if (string.IsNullOrWhiteSpace(settings.SourceFileId) &&
            !string.IsNullOrWhiteSpace(settings.SourceFilePath))
        {
            settings.SourceFileId = CreateLegacySourceFileId(settings.SourceFilePath);
            _store.Save(settings);
        }

        // Normalize/repair values coming from storage.
        if (settings.RepeatIntervalMinutes <= 0)
        {
            settings.RepeatIntervalMinutes = FixedRepeatIntervalMinutes;
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        settings.RepeatIntervalMinutes = FixedRepeatIntervalMinutes; // not user-editable in MVP
        _store.Save(settings);
    }

    public AppSettings SelectSourceFile(string sourceFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        var fullPath = System.IO.Path.GetFullPath(sourceFilePath);
        var settings = Load();
        var isSameSource = !string.IsNullOrWhiteSpace(settings.SourceFilePath) &&
            string.Equals(System.IO.Path.GetFullPath(settings.SourceFilePath), fullPath,
                StringComparison.OrdinalIgnoreCase);

        if (!isSameSource || string.IsNullOrWhiteSpace(settings.SourceFileId))
        {
            settings.SourceFileId = Guid.NewGuid().ToString("N");
        }

        settings.SourceFilePath = fullPath;
        Save(settings);
        return settings;
    }

    private static string CreateLegacySourceFileId(string path)
    {
        var normalized = System.IO.Path.GetFullPath(path).ToLowerInvariant();
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
