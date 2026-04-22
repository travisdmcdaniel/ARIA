using ARIA.Core.Interfaces;
using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ARIA.Telegram.Commands;

public sealed class OnboardingCommand(IAgentTurnHandler agent) : IBotCommand
{
    public string Command => "/onboarding";
    public string Description => "Run the onboarding interview to set up your agent's identity and personality.";

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

        using var typingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var typingTask = KeepTypingAsync(bot, message.Chat.Id, typingCts.Token);

        string response;
        try
        {
            response = await agent.RunTurnAsync(
                userId.Value,
                "Please begin the onboarding process as described in your available skills. Read the onboarding SKILL.md before starting.",
                images: null,
                ct);
        }
        finally
        {
            await typingCts.CancelAsync();
            await typingTask;
        }

        await bot.SendTextAsync(message.Chat.Id, response, ct);
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
}
