using System;
using System.Collections.Generic;
using Dapper;

namespace TaskTracker.Infrastructure.Persistence;

public class DatabaseMigrator
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DatabaseMigrator(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public void MigrateUp()
    {
        using var connection = _connectionFactory.CreateConnection();
        var currentVersion = connection.QuerySingle<long>("PRAGMA user_version;");

        var migrations = GetMigrations();

        foreach (var migration in migrations)
        {
            if (migration.Key > currentVersion)
            {
                using var transaction = connection.BeginTransaction();
                try
                {
                    connection.Execute(migration.Value, transaction: transaction);
                    connection.Execute($"PRAGMA user_version = {migration.Key};", transaction: transaction);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }
    }

    private SortedDictionary<long, string> GetMigrations()
    {
        return new SortedDictionary<long, string>
        {
            { 1, @"
                CREATE TABLE IF NOT EXISTS settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS source_files (
                    id TEXT PRIMARY KEY,
                    path TEXT NOT NULL UNIQUE,
                    enabled INTEGER NOT NULL,
                    last_successful_hash TEXT NULL,
                    last_successful_read_utc TEXT NULL,
                    last_error TEXT NULL,
                    last_error_utc TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS import_snapshots (
                    id TEXT PRIMARY KEY,
                    source_file_id TEXT NOT NULL,
                    file_hash TEXT NOT NULL,
                    file_modified_utc TEXT NULL,
                    imported_at_utc TEXT NOT NULL,
                    status TEXT NOT NULL,
                    diagnostics_json TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS task_rows (
                    id TEXT PRIMARY KEY,
                    source_file_id TEXT NOT NULL,
                    logical_row_key TEXT NOT NULL,
                    sheet_name TEXT NOT NULL,
                    sheet_week_number INTEGER NULL,
                    source_row_number INTEGER NOT NULL,
                    stt TEXT NULL,
                    document_number TEXT NULL,
                    task_content TEXT NULL,
                    executing_unit TEXT NULL,
                    primary_handler TEXT NULL,
                    deadline_raw TEXT NULL,
                    progress TEXT NULL,
                    result TEXT NULL,
                    note TEXT NULL,
                    is_completed INTEGER NOT NULL,
                    deadline_version TEXT NULL,
                    current_status TEXT NOT NULL,
                    days_remaining INTEGER NULL,
                    snapshot_id TEXT NOT NULL,
                    is_current INTEGER NOT NULL,
                    UNIQUE(source_file_id, logical_row_key, snapshot_id)
                );

                CREATE TABLE IF NOT EXISTS deadline_resolutions (
                    id TEXT PRIMARY KEY,
                    logical_row_key TEXT NOT NULL,
                    raw_deadline_fingerprint TEXT NOT NULL,
                    parser_kind TEXT NOT NULL,
                    raw_value TEXT NULL,
                    excel_candidate TEXT NULL,
                    swapped_candidate TEXT NULL,
                    selected_start_date TEXT NULL,
                    selected_end_date TEXT NULL,
                    selected_time TEXT NULL,
                    resolution_source TEXT NOT NULL,
                    requires_review INTEGER NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    UNIQUE(logical_row_key, raw_deadline_fingerprint)
                );

                CREATE TABLE IF NOT EXISTS notification_states (
                    id TEXT PRIMARY KEY,
                    logical_row_key TEXT NOT NULL,
                    deadline_version TEXT NOT NULL,
                    alert_group TEXT NOT NULL,
                    first_notified_at_utc TEXT NULL,
                    last_notified_at_utc TEXT NULL,
                    acknowledged_at_utc TEXT NULL,
                    notification_count INTEGER NOT NULL DEFAULT 0,
                    UNIQUE(logical_row_key, deadline_version, alert_group)
                );
            "},
            { 2, @"
                ALTER TABLE task_rows ADD COLUMN deadline_cell_kind TEXT NULL;
                ALTER TABLE task_rows ADD COLUMN deadline_format_id INTEGER NULL;
                ALTER TABLE task_rows ADD COLUMN deadline_format_code TEXT NULL;
                ALTER TABLE task_rows ADD COLUMN deadline_cell_address TEXT NULL;
                ALTER TABLE task_rows ADD COLUMN deadline_kind TEXT NOT NULL DEFAULT 'Unrecognized';
                ALTER TABLE task_rows ADD COLUMN excel_candidate TEXT NULL;
                ALTER TABLE task_rows ADD COLUMN swapped_candidate TEXT NULL;
                ALTER TABLE task_rows ADD COLUMN resolved_start_date TEXT NULL;
                ALTER TABLE task_rows ADD COLUMN resolved_end_date TEXT NULL;
                ALTER TABLE task_rows ADD COLUMN resolved_time TEXT NULL;
                ALTER TABLE task_rows ADD COLUMN resolution_source TEXT NOT NULL DEFAULT 'Parser';
                ALTER TABLE task_rows ADD COLUMN requires_review INTEGER NOT NULL DEFAULT 0;

                CREATE INDEX IF NOT EXISTS ix_task_rows_current
                    ON task_rows(source_file_id, is_current);
                CREATE INDEX IF NOT EXISTS ix_notification_states_lookup
                    ON notification_states(logical_row_key, deadline_version, alert_group);
                CREATE INDEX IF NOT EXISTS ix_import_snapshots_source
                    ON import_snapshots(source_file_id, imported_at_utc);
            "}
        };
    }
}
