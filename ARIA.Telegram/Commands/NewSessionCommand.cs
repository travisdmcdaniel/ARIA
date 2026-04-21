using ARIA.Core.Interfaces;
using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

public sealed class NewSessionCommand(IConversationStore conversationStore) : IBotCommand
{
    public string Command => "/new";
    public string Description => "Archive the current conversation and start a fresh session.";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var userId = message.From?.Id;
        if (userId is null)
        {
            await bot.SendMarkdownAsync(
                message.Chat.Id,
                Markdown.Escape("Could not identify the Telegram user for this message."),
                ct);
            return;
        }

        var current = await conversationStore.GetOrCreateActiveSessionAsync(userId.Value, ct);
        await conversationStore.ArchiveSessionAsync(current.SessionId, ct);
        var next = await conversationStore.GetOrCreateActiveSessionAsync(userId.Value, ct);

        await bot.SendMarkdownAsync(
            message.Chat.Id,
            $"{Markdown.Bold("New Session")}\n\n" +
            $"{Markdown.Escape("Archived previous session ")}{Markdown.Code(ShortId(current.SessionId))}{Markdown.Escape(".")}\n" +
            $"{Markdown.Escape("Started session ")}{Markdown.Code(ShortId(next.SessionId))}{Markdown.Escape(".")}",
            ct);
    }

    private static string ShortId(string sessionId) =>
        sessionId.Length <= 8 ? sessionId : sessionId[..8];
}
