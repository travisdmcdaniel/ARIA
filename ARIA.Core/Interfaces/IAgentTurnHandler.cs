using ARIA.Core.Models;

namespace ARIA.Core.Interfaces;

public interface IAgentTurnHandler
{
    Task<string> RunTurnAsync(
        long telegramUserId,
        string userText,
        IReadOnlyList<ImageAttachment>? images = null,
        CancellationToken ct = default);
}
