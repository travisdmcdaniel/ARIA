using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

public sealed class HelpCommand(IServiceProvider services) : IBotCommand
{
    public string Command => "/help";
    public string Description => "List all available commands.";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var commands = (IEnumerable<IBotCommand>)services.GetService(typeof(IEnumerable<IBotCommand>))!;

        var lines = commands
            .OrderBy(c => c.Command)
            .Select(c => $"{Markdown.Code(c.Command)} — {Markdown.Escape(c.Description)}");

        var text = $"{Markdown.Bold("ARIA Commands")}\n\n{string.Join("\n", lines)}";

        await bot.SendMarkdownAsync(message.Chat.Id, text, ct);
    }
}
