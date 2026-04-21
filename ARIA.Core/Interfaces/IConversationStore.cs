using ARIA.Core.Models;

namespace ARIA.Core.Interfaces;

public interface IConversationStore
{
    Task<Session> GetOrCreateActiveSessionAsync(long telegramUserId, CancellationToken ct = default);
    Task ArchiveSessionAsync(string sessionId, CancellationToken ct = default);
    Task AppendTurnAsync(ConversationTurn turn, CancellationToken ct = default);
    Task<IReadOnlyList<ConversationTurn>> GetRecentTurnsAsync(string sessionId, int maxTurns, CancellationToken ct = default);
    Task<IReadOnlyList<Session>> ListRecentSessionsAsync(long telegramUserId, int retentionDays, CancellationToken ct = default);
    Task<Session?> GetSessionByIdAsync(string sessionId, CancellationToken ct = default);
}
