using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

/// <summary>Full implementation in M4: lists recent sessions from SQLite.</summary>
public sealed class SessionsCommand : IBotCommand
{
    public string Command => "/sessions";
    public string Description => "List your recent conversation sessions.";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        await bot.SendMarkdownAsync(
            message.Chat.Id,
            Markdown.Italic("Session history will be available in M4 (conversation persistence)."),
            ct);
    }
}
