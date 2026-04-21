using ARIA.Core.Interfaces;
using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

public sealed class ResumeCommand(IConversationStore conversationStore) : IBotCommand
{
    public string Command => "/resume";
    public string Description => "Resume a previous session: /resume <id>";

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

        var query = ParseSessionId(message.Text);
        if (string.IsNullOrWhiteSpace(query))
        {
            await bot.SendMarkdownAsync(
                message.Chat.Id,
                Markdown.Escape("Usage: /resume <session-id>"),
                ct);
            return;
        }

        var session = await FindUserSessionAsync(userId.Value, query, ct);
        if (session is null)
        {
            await bot.SendMarkdownAsync(
                message.Chat.Id,
                Markdown.Escape("No matching session found. Use /sessions to list recent sessions."),
                ct);
            return;
        }

        var resumed = await conversationStore.ResumeSessionAsync(userId.Value, session.SessionId, ct);
        if (!resumed)
        {
            await bot.SendMarkdownAsync(
                message.Chat.Id,
                Markdown.Escape("No matching session found. Use /sessions to list recent sessions."),
                ct);
            return;
        }

        await bot.SendMarkdownAsync(
            message.Chat.Id,
            $"{Markdown.Bold("Session Resumed")}\n\n" +
            $"{Markdown.Escape("Active session is now ")}{Markdown.Code(ShortId(session.SessionId))}{Markdown.Escape(".")}",
            ct);
    }

    private async Task<ARIA.Core.Models.Session?> FindUserSessionAsync(
        long userId,
        string query,
        CancellationToken ct)
    {
        if (query.Length == 32)
        {
            var session = await conversationStore.GetSessionByIdAsync(query, ct);
            return session?.TelegramUserId == userId ? session : null;
        }

        var sessions = await conversationStore.ListRecentSessionsAsync(userId, retentionDays: 0, ct);
        var matches = sessions
            .Where(s => s.SessionId.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    private static string? ParseSessionId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var parts = text.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? parts[1] : null;
    }

    private static string ShortId(string sessionId) =>
        sessionId.Length <= 8 ? sessionId : sessionId[..8];
}
