using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

/// <summary>Full implementation in M8: revokes the OAuth token and clears stored credentials.</summary>
public sealed class GoogleDisconnectCommand : IBotCommand
{
    public string Command => "/google_disconnect";
    public string Description => "Revoke Google access and delete stored OAuth tokens.";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        await bot.SendMarkdownAsync(
            message.Chat.Id,
            Markdown.Italic("Google OAuth will be available in M8 (Google integration)."),
            ct);
    }
}
