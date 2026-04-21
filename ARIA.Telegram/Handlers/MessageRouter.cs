using ARIA.Core.Constants;
using ARIA.Telegram.Commands;
using ARIA.Telegram.Helpers;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Handlers;

/// <summary>
/// Routes an incoming message to a bot command handler or the agent conversation loop.
/// Silently drops all messages when the agent is paused via PauseFlag.
/// </summary>
public sealed class MessageRouter(
    CommandRegistry commands,
    ILogger<MessageRouter> logger) : IMessageRouter
{
    public async Task RouteAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        if (PauseFlag.IsSet)
        {
            logger.LogDebug("Agent paused — dropping message from user {UserId}", message.From?.Id);
            return;
        }

        if (await commands.TryHandleAsync(bot, message, ct))
            return;

        // M3 will replace this echo with the full ConversationLoop.
        var text = message.Text ?? "(no text)";
        logger.LogDebug("Echoing message from user {UserId}", message.From?.Id);

        await bot.SendMarkdownAsync(
            message.Chat.Id,
            Markdown.Escape($"Echo: {text}"),
            ct);
    }
}
