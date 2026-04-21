using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

/// <summary>Full implementation in M7: lists active scheduled jobs from SQLite.</summary>
public sealed class JobsCommand : IBotCommand
{
    public string Command => "/jobs";
    public string Description => "List all active scheduled jobs.";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        await bot.SendMarkdownAsync(
            message.Chat.Id,
            Markdown.Italic("Scheduled jobs will be available in M7 (scheduler)."),
            ct);
    }
}
