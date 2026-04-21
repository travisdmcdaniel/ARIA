using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

/// <summary>Full implementation in M7: cancels a scheduled job by ID.</summary>
public sealed class CancelJobCommand : IBotCommand
{
    public string Command => "/canceljob";
    public string Description => "Cancel a scheduled job: /canceljob <id>";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        await bot.SendMarkdownAsync(
            message.Chat.Id,
            Markdown.Italic("Job cancellation will be available in M7 (scheduler)."),
            ct);
    }
}
