using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

/// <summary>
/// Lists all models available in the local Ollama instance with their sizes.
/// The currently active model is marked with a ● indicator.
///
/// Full implementation in M3: calls the Ollama /api/tags endpoint.
/// </summary>
public sealed class ModelsCommand : IBotCommand
{
    public string Command => "/models";
    public string Description => "List all available Ollama models with sizes and the active model indicator.";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        await bot.SendMarkdownAsync(
            message.Chat.Id,
            Markdown.Italic("Model listing will be available in M3 (LLM integration)."),
            ct);
    }
}
