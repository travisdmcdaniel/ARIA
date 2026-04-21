using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

public sealed class StatusCommand : IBotCommand
{
    public string Command => "/status";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var text = $"""
            {Markdown.Bold("ARIA Status")}

            Service: {Markdown.Escape("✅ Running")}
            LLM: {Markdown.Escape("⏳ Not yet connected (M3)")}
            Google: {Markdown.Escape("⏳ Not yet configured (M8)")}

            Use /new to start a fresh session\.
            """;

        await bot.SendMarkdownAsync(message.Chat.Id, text, ct);
    }
}
