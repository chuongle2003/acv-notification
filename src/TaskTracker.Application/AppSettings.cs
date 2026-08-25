using System;
using System.Collections.Generic;

namespace TaskTracker.Application;

/// <summary>
/// User-adjustable application settings. Persisted via ISettingsStore.
/// </summary>
public class AppSettings
{
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
}
