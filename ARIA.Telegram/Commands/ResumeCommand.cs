using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

/// <summary>Full implementation in M4: resumes a previous session by ID.</summary>
public sealed class ResumeCommand : IBotCommand
{
    public string Command => "/resume";
    public string Description => "Resume a previous session: /resume <id>";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        await bot.SendMarkdownAsync(
            message.Chat.Id,
            Markdown.Italic("Session resume will be available in M4 (conversation persistence)."),
            ct);
    }
}
