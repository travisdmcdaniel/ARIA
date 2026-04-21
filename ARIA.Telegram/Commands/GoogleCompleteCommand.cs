using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

/// <summary>
/// Fallback command for remote users who cannot trigger the loopback callback.
/// Accepts the authorization code (or full redirect URL) shown after Google sign-in
/// and completes the OAuth token exchange.
///
/// Usage: /google_complete &lt;code_or_redirect_url&gt;
///
/// Full implementation in M8.
/// </summary>
public sealed class GoogleCompleteCommand : IBotCommand
{
    public string Command => "/google_complete";
    public string Description => "Complete Google OAuth by pasting the authorization code: /google_complete <code>";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        await bot.SendMarkdownAsync(
            message.Chat.Id,
            Markdown.Italic("Google OAuth will be available in M8 (Google integration)."),
            ct);
    }
}
