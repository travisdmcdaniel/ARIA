using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ARIA.Telegram.Helpers;

/// <summary>
/// Convenience extensions on ITelegramBotClient that default to MarkdownV2.
/// Use these instead of calling bot.SendMessage directly so that parse mode
/// is never accidentally omitted.
/// </summary>
public static class BotClientExtensions
{
    /// <summary>
    /// Sends a MarkdownV2-formatted message. The caller is responsible for
    /// escaping all literal text via Markdown.Escape() or the formatting helpers.
    /// </summary>
    public static Task<Message> SendMarkdownAsync(
        this ITelegramBotClient bot,
        ChatId chatId,
        string markdownV2Text,
        CancellationToken ct = default) =>
        bot.SendMessage(
            chatId,
            markdownV2Text,
            parseMode: ParseMode.MarkdownV2,
            cancellationToken: ct);

    /// <summary>Sends a plain-text message with no formatting.</summary>
    public static Task<Message> SendTextAsync(
        this ITelegramBotClient bot,
        ChatId chatId,
        string text,
        CancellationToken ct = default) =>
        bot.SendMessage(chatId, text, cancellationToken: ct);
}
