using System.Text;
using ARIA.Core.Interfaces;
using ARIA.Core.Models;
using ARIA.Core.Options;
using ARIA.Telegram.Helpers;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

public sealed class SessionsCommand(
    IConversationStore conversationStore,
    IOptions<AriaOptions> options) : IBotCommand
{
    public string Command => "/sessions";
    public string Description => "List your recent conversation sessions.";

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

        var retentionDays = options.Value.Agent.SessionRetentionDays;
        var sessions = await conversationStore.ListRecentSessionsAsync(userId.Value, retentionDays, ct);

        if (sessions.Count == 0)
        {
            await bot.SendMarkdownAsync(
                message.Chat.Id,
                Markdown.Escape("No recent sessions found."),
                ct);
            return;
        }

        var text = FormatSessions(sessions, retentionDays);
        await bot.SendMarkdownAsync(
            message.Chat.Id,
            text,
            ct);
    }

    private static string FormatSessions(IReadOnlyList<Session> sessions, int retentionDays)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Markdown.Bold("Recent Sessions"));
        sb.AppendLine();

        foreach (var session in sessions)
        {
            var marker = session.IsActive ? "current" : "archived";
            sb.Append(Markdown.Code(ShortId(session.SessionId)));
            sb.Append(" — ");
            sb.Append(Markdown.Escape(marker));
            sb.Append(" — ");
            sb.Append(Markdown.Escape($"started {FormatUtc(session.StartedAt)}"));
            sb.Append(" — ");
            sb.Append(Markdown.Escape($"last active {FormatUtc(session.LastActivityAt)}"));
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.Append(Markdown.Escape(
            retentionDays > 0
                ? $"Showing sessions active in the last {retentionDays} days."
                : "Showing all sessions."));
        return sb.ToString();
    }

    private static string ShortId(string sessionId) =>
        sessionId.Length <= 8 ? sessionId : sessionId[..8];

    private static string FormatUtc(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'");
}
