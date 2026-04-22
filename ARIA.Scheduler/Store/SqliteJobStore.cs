using ARIA.Core.Models;
using ARIA.Core.Options;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ARIA.Scheduler.Store;

public sealed class SqliteJobStore
{
    private readonly string _connectionString;

    public SqliteJobStore(IOptions<AriaOptions> options)
    {
        var path = options.Value.Workspace.GetResolvedDatabasePath();
        _connectionString = $"Data Source={path}";
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await EnsureColumnAsync(conn, "scheduled_jobs", "file_name", "TEXT");
        await EnsureColumnAsync(conn, "scheduled_jobs", "file_path", "TEXT");
        await EnsureColumnAsync(conn, "scheduled_jobs", "time_zone_id", "TEXT NOT NULL DEFAULT 'UTC'");
        await EnsureColumnAsync(conn, "scheduled_jobs", "payload_kind", "TEXT NOT NULL DEFAULT 'agentTurn'");
        await EnsureColumnAsync(conn, "scheduled_jobs", "session_target", "TEXT NOT NULL DEFAULT 'isolated'");
        await EnsureColumnAsync(conn, "scheduled_jobs", "loaded_at", "TEXT");
    }

    public async Task<Dictionary<string, ScheduledJob>> LoadMirrorAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var conn = await OpenAsync(ct);

        var rows = await conn.QueryAsync<JobRow>(
            """
            SELECT job_id, file_name, file_path, telegram_user_id, name, cron_expression,
                   time_zone_id, payload_kind, prompt, session_target, is_active,
                   loaded_at, last_fired_at, next_fire_at
            FROM scheduled_jobs
            """);

        return rows
            .Select(Map)
            .ToDictionary(j => j.JobId, StringComparer.OrdinalIgnoreCase);
    }

    public async Task ReplaceMirrorAsync(IReadOnlyList<ScheduledJob> jobs, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var seenIds = jobs.Where(j => j.IsValid).Select(j => j.JobId).ToArray();

        foreach (var job in jobs.Where(j => j.IsValid))
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO scheduled_jobs
                    (job_id, file_name, file_path, telegram_user_id, name, cron_expression,
                     time_zone_id, payload_kind, prompt, session_target, is_active,
                     created_at, loaded_at, last_fired_at, next_fire_at)
                VALUES
                    (@job_id, @file_name, @file_path, @telegram_user_id, @name, @cron_expression,
                     @time_zone_id, @payload_kind, @prompt, @session_target, @is_active,
                     @created_at, @loaded_at, @last_fired_at, @next_fire_at)
                ON CONFLICT(job_id) DO UPDATE SET
                    file_name = excluded.file_name,
                    file_path = excluded.file_path,
                    telegram_user_id = excluded.telegram_user_id,
                    name = excluded.name,
                    cron_expression = excluded.cron_expression,
                    time_zone_id = excluded.time_zone_id,
                    payload_kind = excluded.payload_kind,
                    prompt = excluded.prompt,
                    session_target = excluded.session_target,
                    is_active = excluded.is_active,
                    loaded_at = excluded.loaded_at,
                    last_fired_at = excluded.last_fired_at,
                    next_fire_at = excluded.next_fire_at
                """,
                ToParams(job),
                tx);
        }

        if (seenIds.Length == 0)
        {
            await conn.ExecuteAsync("DELETE FROM scheduled_jobs", transaction: tx);
        }
        else
        {
            await conn.ExecuteAsync(
                "DELETE FROM scheduled_jobs WHERE job_id NOT IN @seenIds",
                new { seenIds },
                tx);
        }

        await tx.CommitAsync(ct);
    }

    public async Task MarkFiredAsync(string jobId, DateTime firedAt, DateTime? nextFireAt, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE scheduled_jobs
            SET last_fired_at = @last_fired_at,
                next_fire_at = @next_fire_at
            WHERE job_id = @job_id
            """,
            new
            {
                job_id = jobId,
                last_fired_at = firedAt.ToString("O"),
                next_fire_at = nextFireAt?.ToString("O")
            });
    }

    public async Task AppendExecutionLogAsync(
        string jobId,
        DateTime startedAt,
        DateTime? completedAt,
        bool success,
        string? output,
        string? errorMessage,
        CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO job_execution_log
                (job_id, started_at, completed_at, success, output, error_message)
            VALUES
                (@job_id, @started_at, @completed_at, @success, @output, @error_message)
            """,
            new
            {
                job_id = jobId,
                started_at = startedAt.ToString("O"),
                completed_at = completedAt?.ToString("O"),
                success = success ? 1 : 0,
                output,
                error_message = errorMessage
            });
    }

    private static object ToParams(ScheduledJob job) => new
    {
        job_id = job.JobId,
        file_name = job.FileName,
        file_path = job.FilePath,
        telegram_user_id = job.TelegramUserId,
        name = job.Name,
        cron_expression = job.CronExpression,
        time_zone_id = job.TimeZoneId,
        payload_kind = job.PayloadKind,
        prompt = job.Prompt,
        session_target = job.SessionTarget,
        is_active = job.IsActive ? 1 : 0,
        created_at = job.LoadedAt.ToString("O"),
        loaded_at = job.LoadedAt.ToString("O"),
        last_fired_at = job.LastFiredAt?.ToString("O"),
        next_fire_at = job.NextFireAt?.ToString("O")
    };

    private static ScheduledJob Map(JobRow row) => new(
        JobId: row.job_id,
        FileName: row.file_name ?? $"{row.job_id}.json",
        FilePath: row.file_path ?? string.Empty,
        TelegramUserId: row.telegram_user_id,
        Name: row.name,
        CronExpression: row.cron_expression,
        TimeZoneId: string.IsNullOrWhiteSpace(row.time_zone_id) ? "UTC" : row.time_zone_id,
        PayloadKind: string.IsNullOrWhiteSpace(row.payload_kind) ? "agentTurn" : row.payload_kind,
        Prompt: row.prompt,
        SessionTarget: string.IsNullOrWhiteSpace(row.session_target) ? "isolated" : row.session_target,
        Enabled: row.is_active != 0,
        DisabledByFileName: false,
        IsValid: true,
        ValidationError: null,
        LoadedAt: ParseOptional(row.loaded_at) ?? DateTime.UtcNow,
        LastFiredAt: ParseOptional(row.last_fired_at),
        NextFireAt: ParseOptional(row.next_fire_at));

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static async Task EnsureColumnAsync(SqliteConnection conn, string table, string column, string definition)
    {
        var rows = await conn.QueryAsync<TableColumn>($"PRAGMA table_info({table})");
        if (rows.Any(r => string.Equals(r.name, column, StringComparison.OrdinalIgnoreCase)))
            return;

        await conn.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }

    private static DateTime? ParseOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : DateTime.Parse(value).ToUniversalTime();

    private sealed class JobRow
    {
        public string job_id { get; set; } = string.Empty;
        public string? file_name { get; set; }
        public string? file_path { get; set; }
        public long telegram_user_id { get; set; }
        public string name { get; set; } = string.Empty;
        public string cron_expression { get; set; } = string.Empty;
        public string time_zone_id { get; set; } = "UTC";
        public string payload_kind { get; set; } = "agentTurn";
        public string prompt { get; set; } = string.Empty;
        public string session_target { get; set; } = "isolated";
        public long is_active { get; set; }
        public string? loaded_at { get; set; }
        public string? last_fired_at { get; set; }
        public string? next_fire_at { get; set; }
    }

    private sealed class TableColumn
    {
        public string name { get; set; } = string.Empty;
    }
}
