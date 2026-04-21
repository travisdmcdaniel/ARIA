namespace ARIA.Core.Models;

public sealed record Session(
    string SessionId,
    long TelegramUserId,
    DateTime StartedAt,
    DateTime LastActivityAt,
    bool IsActive);
