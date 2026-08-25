using System;
using Dapper;
using TaskTracker.Application;

namespace TaskTracker.Infrastructure.Persistence;

/// <summary>
/// Key/value settings persistence over the settings table (migration 1).
/// Unknown keys are preserved so older builds can read newer databases.
/// </summary>
public class SqliteSettingsStore : ISettingsStore
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteSettingsStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public AppSettings Load()
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = connection.Query<(string Key, string Value)>(
            "SELECT key, value FROM settings").AsList();

        var settings = new AppSettings();
        foreach (var (key, value) in rows)
        {
            switch (key)
            {
                case "source_file_path":
                    settings.SourceFilePath = value;
                    break;
                case "notifications_paused":
                    settings.NotificationsPaused = value == "1";
                    break;
                case "repeat_interval_minutes":
                    if (int.TryParse(value, out var minutes))
                    {
                        settings.RepeatIntervalMinutes = minutes;
                    }
                    break;
                case "start_with_windows":
                    settings.StartWithWindows = value == "1";
                    break;
            }
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        var now = DateTime.UtcNow.ToString("o");
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        Upsert(connection, transaction, "source_file_path", settings.SourceFilePath, now);
        Upsert(connection, transaction, "notifications_paused", settings.NotificationsPaused ? "1" : "0", now);
        Upsert(connection, transaction, "repeat_interval_minutes", settings.RepeatIntervalMinutes.ToString(), now);
        Upsert(connection, transaction, "start_with_windows", settings.StartWithWindows ? "1" : "0", now);

        transaction.Commit();
    }

    private static void Upsert(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction,
        string key, string value, string updatedAtUtc)
    {
        connection.Execute(@"
            INSERT INTO settings (key, value, updated_at_utc) VALUES (@Key, @Value, @UpdatedAt)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at_utc = excluded.updated_at_utc
        ", new { Key = key, Value = value, UpdatedAt = updatedAtUtc }, transaction);
    }
}
