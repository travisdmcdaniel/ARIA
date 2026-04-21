using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

public interface IBotCommand
{
    string Command { get; }
    Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct);
}
