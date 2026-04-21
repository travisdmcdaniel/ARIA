using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

public interface IBotCommand
{
    /// <summary>The command trigger, e.g. "/status".</summary>
    string Command { get; }

    /// <summary>One-line description shown in /help.</summary>
    string Description { get; }

    Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct);
}
