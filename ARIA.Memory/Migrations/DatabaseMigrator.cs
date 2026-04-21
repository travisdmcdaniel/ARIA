using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ARIA.Memory.Migrations;

public sealed class DatabaseMigrator
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseMigrator> _logger;

    public DatabaseMigrator(string databasePath, ILogger<DatabaseMigrator> logger)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = $"Data Source={databasePath}";
        _logger = logger;
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Running database migrations against {Path}", _connectionString);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync("PRAGMA journal_mode=WAL;");

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS schema_version (
                version     INTEGER NOT NULL,
                applied_at  TEXT    NOT NULL
            );
            """);

        var version = await connection.QuerySingleOrDefaultAsync<int?>(
            "SELECT MAX(version) FROM schema_version");

        if (version is null or < 1)
        {
            _logger.LogInformation("Applying migration v1 — initial schema");
            await ApplyV1Async(connection);
            await connection.ExecuteAsync(
                "INSERT INTO schema_version (version, applied_at) VALUES (1, @now)",
                new { now = DateTime.UtcNow.ToString("O") });
            _logger.LogInformation("Migration v1 complete");
        }
        else
        {
            _logger.LogInformation("Database schema is up to date (version {Version})", version);
        }
    }

    private static async Task ApplyV1Async(SqliteConnection connection)
    {
        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS sessions (
                session_id          TEXT    PRIMARY KEY,
                telegram_user_id    INTEGER NOT NULL,
                started_at          TEXT    NOT NULL,
                last_activity_at    TEXT    NOT NULL,
                is_active           INTEGER NOT NULL DEFAULT 1
            );

            CREATE INDEX IF NOT EXISTS idx_sessions_user
                ON sessions(telegram_user_id, is_active);

            CREATE TABLE IF NOT EXISTS conversation_turns (
                turn_id             INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id          TEXT    NOT NULL,
                telegram_user_id    INTEGER NOT NULL,
                timestamp           TEXT    NOT NULL,
                role                TEXT    NOT NULL,
                text_content        TEXT,
                tool_calls_json     TEXT,
                tool_result_json    TEXT,
                image_data_json     TEXT,
                FOREIGN KEY (session_id) REFERENCES sessions(session_id)
            );

            CREATE INDEX IF NOT EXISTS idx_turns_session
                ON conversation_turns(session_id, timestamp);

            CREATE TABLE IF NOT EXISTS scheduled_jobs (
                job_id              TEXT    PRIMARY KEY,
                telegram_user_id    INTEGER NOT NULL,
                name                TEXT    NOT NULL,
                cron_expression     TEXT    NOT NULL,
                prompt              TEXT    NOT NULL,
                is_active           INTEGER NOT NULL DEFAULT 1,
                created_at          TEXT    NOT NULL,
                last_fired_at       TEXT,
                next_fire_at        TEXT
            );

            CREATE TABLE IF NOT EXISTS job_execution_log (
                log_id          INTEGER PRIMARY KEY AUTOINCREMENT,
                job_id          TEXT    NOT NULL,
                started_at      TEXT    NOT NULL,
                completed_at    TEXT,
                success         INTEGER NOT NULL,
                output          TEXT,
                error_message   TEXT,
                FOREIGN KEY (job_id) REFERENCES scheduled_jobs(job_id)
            );
            """);
    }
}
