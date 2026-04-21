using ARIA.Core.Interfaces;
using ARIA.Core.Models;
using ARIA.Core.Options;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ARIA.Memory.Sqlite;

public sealed class SqliteConversationStore : IConversationStore
{
    private readonly string _connectionString;

    public SqliteConversationStore(IOptions<AriaOptions> options)
    {
        var path = options.Value.Workspace.GetResolvedDatabasePath();
        _connectionString = $"Data Source={path}";
    }

    public async Task<Session> GetOrCreateActiveSessionAsync(long telegramUserId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<SessionRow>(
            """
            SELECT session_id, telegram_user_id, started_at, last_activity_at, is_active
            FROM sessions
            WHERE telegram_user_id = @userId AND is_active = 1
            ORDER BY last_activity_at DESC
            LIMIT 1
            """,
            new { userId = telegramUserId });

        if (row is not null)
            return MapSession(row);

        var newRow = new SessionRow
        {
            session_id       = Guid.NewGuid().ToString("N"),
            telegram_user_id = telegramUserId,
            started_at       = DateTime.UtcNow.ToString("O"),
            last_activity_at = DateTime.UtcNow.ToString("O"),
            is_active        = 1
        };

        await conn.ExecuteAsync(
            """
            INSERT INTO sessions (session_id, telegram_user_id, started_at, last_activity_at, is_active)
            VALUES (@session_id, @telegram_user_id, @started_at, @last_activity_at, @is_active)
            """,
            newRow);

        return MapSession(newRow);
    }

    public async Task AppendTurnAsync(ConversationTurn turn, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);

        await conn.ExecuteAsync(
            """
            INSERT INTO conversation_turns
                (session_id, telegram_user_id, timestamp, role, text_content,
                 tool_calls_json, tool_result_json, image_data_json)
            VALUES
                (@session_id, @telegram_user_id, @timestamp, @role, @text_content,
                 @tool_calls_json, @tool_result_json, @image_data_json)
            """,
            new
            {
                session_id        = turn.SessionId,
                telegram_user_id  = turn.TelegramUserId,
                timestamp         = turn.Timestamp.ToString("O"),
                role              = turn.Role,
                text_content      = turn.TextContent,
                tool_calls_json   = turn.ToolCallsJson,
                tool_result_json  = turn.ToolResultJson,
                image_data_json   = turn.ImageDataJson
            });

        await conn.ExecuteAsync(
            """
            UPDATE sessions SET last_activity_at = @now
            WHERE session_id = @sessionId
            """,
            new { now = DateTime.UtcNow.ToString("O"), sessionId = turn.SessionId });
    }

    public async Task<IReadOnlyList<ConversationTurn>> GetRecentTurnsAsync(
        string sessionId, int maxTurns, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);

        // Get the most recent N turns, then reverse to chronological order.
        var rows = await conn.QueryAsync<TurnRow>(
            """
            SELECT turn_id, session_id, telegram_user_id, timestamp, role,
                   text_content, tool_calls_json, tool_result_json, image_data_json
            FROM (
                SELECT * FROM conversation_turns
                WHERE session_id = @sessionId
                ORDER BY turn_id DESC
                LIMIT @maxTurns
            )
            ORDER BY turn_id ASC
            """,
            new { sessionId, maxTurns });

        return rows.Select(MapTurn).ToList();
    }

    public Task ArchiveSessionAsync(string sessionId, CancellationToken ct = default)
    {
        return ArchiveSessionInternalAsync(sessionId, ct);
    }

    public async Task<IReadOnlyList<Session>> ListRecentSessionsAsync(
        long telegramUserId, int retentionDays, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);

        IEnumerable<SessionRow> rows;
        if (retentionDays > 0)
        {
            rows = await conn.QueryAsync<SessionRow>(
                """
                SELECT session_id, telegram_user_id, started_at, last_activity_at, is_active
                FROM sessions
                WHERE telegram_user_id = @userId AND last_activity_at >= @cutoff
                ORDER BY last_activity_at DESC
                """,
                new
                {
                    userId = telegramUserId,
                    cutoff = DateTime.UtcNow.AddDays(-retentionDays).ToString("O")
                });
        }
        else
        {
            rows = await conn.QueryAsync<SessionRow>(
                """
                SELECT session_id, telegram_user_id, started_at, last_activity_at, is_active
                FROM sessions
                WHERE telegram_user_id = @userId
                ORDER BY last_activity_at DESC
                """,
                new { userId = telegramUserId });
        }

        return rows.Select(MapSession).ToList();
    }

    public async Task<Session?> GetSessionByIdAsync(string sessionId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<SessionRow>(
            """
            SELECT session_id, telegram_user_id, started_at, last_activity_at, is_active
            FROM sessions
            WHERE session_id = @sessionId
            """,
            new { sessionId });

        return row is null ? null : MapSession(row);
    }

    public async Task<bool> ResumeSessionAsync(
        long telegramUserId,
        string sessionId,
        CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        var targetUserId = await conn.QuerySingleOrDefaultAsync<long?>(
            """
            SELECT telegram_user_id
            FROM sessions
            WHERE session_id = @sessionId
            """,
            new { sessionId },
            tx);

        if (targetUserId != telegramUserId)
        {
            tx.Rollback();
            return false;
        }

        var now = DateTime.UtcNow.ToString("O");

        await conn.ExecuteAsync(
            """
            UPDATE sessions
            SET is_active = 0
            WHERE telegram_user_id = @telegramUserId
            """,
            new { telegramUserId },
            tx);

        var updated = await conn.ExecuteAsync(
            """
            UPDATE sessions
            SET is_active = 1, last_activity_at = @now
            WHERE session_id = @sessionId
            """,
            new { now, sessionId },
            tx);

        tx.Commit();
        return updated == 1;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task ArchiveSessionInternalAsync(string sessionId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);

        await conn.ExecuteAsync(
            """
            UPDATE sessions
            SET is_active = 0, last_activity_at = @now
            WHERE session_id = @sessionId
            """,
            new
            {
                now = DateTime.UtcNow.ToString("O"),
                sessionId
            });
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static Session MapSession(SessionRow r) => new(
        r.session_id,
        r.telegram_user_id,
        DateTime.Parse(r.started_at),
        DateTime.Parse(r.last_activity_at),
        r.is_active != 0);

    private static ConversationTurn MapTurn(TurnRow r) => new(
        r.turn_id,
        r.session_id,
        r.telegram_user_id,
        DateTime.Parse(r.timestamp),
        r.role,
        r.text_content,
        r.tool_calls_json,
        r.tool_result_json,
        r.image_data_json);

    private sealed class SessionRow
    {
        public string session_id { get; set; } = string.Empty;
        public long telegram_user_id { get; set; }
        public string started_at { get; set; } = string.Empty;
        public string last_activity_at { get; set; } = string.Empty;
        public long is_active { get; set; }
    }

    private sealed class TurnRow
    {
        public long turn_id { get; set; }
        public string session_id { get; set; } = string.Empty;
        public long telegram_user_id { get; set; }
        public string timestamp { get; set; } = string.Empty;
        public string role { get; set; } = string.Empty;
        public string? text_content { get; set; }
        public string? tool_calls_json { get; set; }
        public string? tool_result_json { get; set; }
        public string? image_data_json { get; set; }
    }
}
