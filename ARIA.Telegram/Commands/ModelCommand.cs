using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

/// <summary>
/// /model          — show the currently active model name.
/// /model &lt;name&gt;   — switch to the named model.
///
/// Full implementation in M3: validates the model exists in Ollama,
/// updates the active model, and persists the change to config.
/// </summary>
public sealed class ModelCommand : IBotCommand
{
    public string Command => "/model";
    public string Description => "Show the active model, or switch to a different one: /model [name]";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        await bot.SendMarkdownAsync(
            message.Chat.Id,
            Markdown.Italic("Model switching will be available in M3 (LLM integration)."),
            ct);
    }
}
