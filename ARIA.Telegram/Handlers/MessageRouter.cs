using ARIA.Core.Constants;
using ARIA.Core.Interfaces;
using ARIA.Core.Models;
using ARIA.Telegram.Commands;
using ARIA.Telegram.Helpers;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ARIA.Telegram.Handlers;

/// <summary>
/// Routes an incoming message to a bot command handler or the agent conversation loop.
/// Silently drops all messages when the agent is paused via PauseFlag.
/// </summary>
public sealed class MessageRouter(
    CommandRegistry commands,
    IAgentTurnHandler agent,
    ILogger<MessageRouter> logger) : IMessageRouter
{
    public async Task RouteAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        if (PauseFlag.IsSet)
        {
            logger.LogDebug("Agent paused — dropping message from user {UserId}", message.From?.Id);
            return;
        }

        if (await commands.TryHandleAsync(bot, message, ct))
            return;

        var userId = message.From?.Id ?? 0;
        var text = message.Text ?? message.Caption ?? string.Empty;

        IReadOnlyList<ImageAttachment>? images = null;
        if (message.Photo is { Length: > 0 } photos)
            images = await DownloadImageAsync(bot, photos[^1].FileId, "image/jpeg", ct);
        else if (message.Document is { } document &&
                 IsImageMimeType(document.MimeType))
        {
            images = await DownloadImageAsync(
                bot,
                document.FileId,
                document.MimeType ?? "application/octet-stream",
                ct);
        }

        using var typingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var typingTask = KeepTypingAsync(bot, message.Chat.Id, typingCts.Token);

        string responseText;
        try
        {
            responseText = await agent.RunTurnAsync(userId, text, images, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ConversationLoop threw for user {UserId}", userId);
            responseText = "An error occurred while processing your message.";
        }
        finally
        {
            await typingCts.CancelAsync();
            await typingTask;
        }

        await SendAgentResponseAsync(bot, message.Chat.Id, responseText, ct);
    }

    public async Task SendAgentResponseAsync(
        ITelegramBotClient bot,
        ChatId chatId,
        string text,
        CancellationToken ct)
    {
        var html = LlmResponseFormatter.ToTelegramHtml(text);
        try
        {
            await bot.SendHtmlAsync(chatId, html, ct);
        }
        catch (ApiRequestException ex)
        {
            logger.LogWarning(ex, "Failed to send HTML response — falling back to plain text");
            try
            {
                await bot.SendTextAsync(chatId, text, ct);
            }
            catch (Exception ex2)
            {
                logger.LogWarning(ex2, "Failed to send plain text response");
            }
        }
    }

    private static async Task KeepTypingAsync(ITelegramBotClient bot, ChatId chatId, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await bot.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);
                await Task.Delay(TimeSpan.FromSeconds(4), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    private static bool IsImageMimeType(string? mimeType) =>
        mimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<IReadOnlyList<ImageAttachment>?> DownloadImageAsync(
        ITelegramBotClient bot, string fileId, string mimeType, CancellationToken ct)
    {
        try
        {
            var file = await bot.GetFile(fileId, ct);
            if (string.IsNullOrEmpty(file.FilePath))
                return null;

            using var ms = new MemoryStream();
            await bot.DownloadFile(file.FilePath, ms, ct);
            return [new ImageAttachment(Convert.ToBase64String(ms.ToArray()), mimeType)];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to download image {FileId}", fileId);
            return null;
        }
    }

}
