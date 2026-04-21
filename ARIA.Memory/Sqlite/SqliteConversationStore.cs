using ARIA.Core.Interfaces;
using ARIA.Core.Models;
using ARIA.Core.Options;
using Microsoft.Extensions.Options;

namespace ARIA.Memory.Sqlite;

/// <summary>
/// SQLite-backed conversation store.
/// Stub implementation — filled in during M4.
/// </summary>
public sealed class SqliteConversationStore : IConversationStore
{
    private readonly string _connectionString;

    public SqliteConversationStore(IOptions<AriaOptions> options)
    {
        var path = options.Value.Workspace.GetResolvedDatabasePath();
        _connectionString = $"Data Source={path}";
    }

    public Task<Session> GetOrCreateActiveSessionAsync(long telegramUserId, CancellationToken ct = default)
        => throw new NotImplementedException("SqliteConversationStore not yet implemented (M4)");

    public Task ArchiveSessionAsync(string sessionId, CancellationToken ct = default)
        => throw new NotImplementedException("SqliteConversationStore not yet implemented (M4)");

    public Task AppendTurnAsync(ConversationTurn turn, CancellationToken ct = default)
        => throw new NotImplementedException("SqliteConversationStore not yet implemented (M4)");

    public Task<IReadOnlyList<ConversationTurn>> GetRecentTurnsAsync(string sessionId, int maxTurns, CancellationToken ct = default)
        => throw new NotImplementedException("SqliteConversationStore not yet implemented (M4)");

    public Task<IReadOnlyList<Session>> ListRecentSessionsAsync(long telegramUserId, int retentionDays, CancellationToken ct = default)
        => throw new NotImplementedException("SqliteConversationStore not yet implemented (M4)");

    public Task<Session?> GetSessionByIdAsync(string sessionId, CancellationToken ct = default)
        => throw new NotImplementedException("SqliteConversationStore not yet implemented (M4)");
}
