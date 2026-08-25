using System;
using System.IO;
using TaskTracker.Application;
using TaskTracker.Infrastructure.Persistence;
using Xunit;

namespace TaskTracker.Infrastructure.Tests;

public class SqliteSettingsStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteSettingsStore _store;

    public SqliteSettingsStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory($"Data Source={_dbPath};Mode=ReadWriteCreate");
        new DatabaseMigrator(factory).MigrateUp();
        _store = new SqliteSettingsStore(factory);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var original = new AppSettings
        {
            SourceFilePath = @"D:\vanban\kehoach-tuan34.xlsx",
            NotificationsPaused = true,
            RepeatIntervalMinutes = 720,
            StartWithWindows = true
        };

        _store.Save(original);
        var loaded = _store.Load();

        Assert.Equal(original.SourceFilePath, loaded.SourceFilePath);
        Assert.Equal(original.NotificationsPaused, loaded.NotificationsPaused);
        Assert.Equal(original.RepeatIntervalMinutes, loaded.RepeatIntervalMinutes);
        Assert.Equal(original.StartWithWindows, loaded.StartWithWindows);
    }

    [Fact]
    public void Load_OnEmptyDatabase_ReturnsDefaults()
    {
        var settings = _store.Load();

        Assert.Equal(string.Empty, settings.SourceFilePath);
        Assert.False(settings.NotificationsPaused);
        Assert.False(settings.StartWithWindows);
        Assert.True(settings.RepeatIntervalMinutes > 0);
    }

    [Fact]
    public void Save_Twice_OverwritesPreviousValues()
    {
        _store.Save(new AppSettings { SourceFilePath = "first.xlsx", NotificationsPaused = false });
        _store.Save(new AppSettings { SourceFilePath = "second.xlsx", NotificationsPaused = true });

        var loaded = _store.Load();

        Assert.Equal("second.xlsx", loaded.SourceFilePath);
        Assert.True(loaded.NotificationsPaused);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }
}
