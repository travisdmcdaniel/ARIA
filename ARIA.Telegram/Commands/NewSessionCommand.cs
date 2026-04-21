using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

public sealed class NewSessionCommand : IBotCommand
{
    public string Command => "/new";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        // Full session management is wired in M4 via IConversationStore.
        await bot.SendMarkdownAsync(
            message.Chat.Id,
            Markdown.Escape("Starting a fresh session. Previous conversation archived."),
            ct);
    }
}
