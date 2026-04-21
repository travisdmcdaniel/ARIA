using ARIA.Telegram.Commands;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Handlers;

/// <summary>
/// Routes an incoming message to a bot command handler or the agent conversation loop.
/// </summary>
public sealed class MessageRouter(
    CommandRegistry commands,
    ILogger<MessageRouter> logger) : IMessageRouter
{
    public async Task RouteAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        if (await commands.TryHandleAsync(bot, message, ct))
            return;

        // M3 will replace this echo with the full ConversationLoop.
        var text = message.Text ?? "(no text)";
        logger.LogDebug("Echoing message from user {UserId}: {Text}", message.From?.Id, text);

        await bot.SendMessage(
            message.Chat.Id,
            $"Echo: {text}",
            cancellationToken: ct);
    }
}
