using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Handlers;

public interface IMessageRouter
{
    Task RouteAsync(ITelegramBotClient bot, Message message, CancellationToken ct);

    Task SendAgentResponseAsync(
        ITelegramBotClient bot,
        ChatId chatId,
        string text,
        CancellationToken ct);
}
