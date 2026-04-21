using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

/// <summary>
/// Starts the Google OAuth flow. ARIA opens a temporary loopback HTTP listener,
/// generates the authorization URL, and sends it to the user. If the user opens
/// it in the local browser the callback is caught automatically and auth completes
/// with no further commands. For remote use, the user can paste the authorization
/// code via /google_complete.
///
/// Full implementation in M8.
/// </summary>
public sealed class GoogleConnectCommand : IBotCommand
{
    public string Command => "/google_connect";
    public string Description => "Start the Google OAuth flow and receive an authorization link.";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        await bot.SendMarkdownAsync(
            message.Chat.Id,
            Markdown.Italic("Google OAuth will be available in M8 (Google integration)."),
            ct);
    }
}
