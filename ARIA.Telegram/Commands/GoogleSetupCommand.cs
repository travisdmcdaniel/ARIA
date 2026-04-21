using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

/// <summary>
/// Accepts a client_secret.json file attachment from the user, extracts the
/// OAuth client ID and client secret, and stores them in the credential store.
///
/// Full implementation in M8: parse the uploaded JSON and call ICredentialStore.Save().
/// </summary>
public sealed class GoogleSetupCommand : IBotCommand
{
    public string Command => "/google_setup";
    public string Description => "Upload your Google client_secret.json to configure OAuth credentials.";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        await bot.SendMarkdownAsync(
            message.Chat.Id,
            Markdown.Italic("Google OAuth setup will be available in M8 (Google integration)."),
            ct);
    }
}
