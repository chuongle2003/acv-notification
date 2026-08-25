using System;
using System.IO;
using TaskTracker.Application;
using Xunit;

namespace TaskTracker.Application.Tests;

public class SettingsServiceTests
{
    private class InMemoryStore : ISettingsStore
    {
        public AppSettings? Saved { get; private set; }
        public AppSettings Stored { get; set; } = new();

        public AppSettings Load() => Stored;
        public void Save(AppSettings settings) => Saved = settings;
    }

    [Fact]
    public void Load_NonPositiveRepeatInterval_IsReplacedWithFixedValue()
    {
        var store = new InMemoryStore { Stored = new AppSettings { RepeatIntervalMinutes = -5 } };
        var service = new SettingsService(store);

        var settings = service.Load();

        Assert.Equal(SettingsService.FixedRepeatIntervalMinutes, settings.RepeatIntervalMinutes);
    }

    [Fact]
    public void Save_AlwaysPersistsTheFixedRepeatInterval()
    {
        var store = new InMemoryStore();
        var service = new SettingsService(store);

        service.Save(new AppSettings { RepeatIntervalMinutes = 1 });

        Assert.NotNull(store.Saved);
        Assert.Equal(SettingsService.FixedRepeatIntervalMinutes, store.Saved!.RepeatIntervalMinutes);
    }

    [Fact]
    public void Load_PreservesUserEditableFields()
    {
        var store = new InMemoryStore
        {
            Stored = new AppSettings
            {
                SourceFilePath = @"C:\data\kehoach.xlsx",
                NotificationsPaused = true,
                StartWithWindows = true,
                RepeatIntervalMinutes = 720
            }
        };
        var service = new SettingsService(store);

        var settings = service.Load();

        Assert.Equal(@"C:\data\kehoach.xlsx", settings.SourceFilePath);
        Assert.True(settings.NotificationsPaused);
        Assert.True(settings.StartWithWindows);
    }
}
