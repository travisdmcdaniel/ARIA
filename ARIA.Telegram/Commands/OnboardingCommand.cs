using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

public sealed class OnboardingCommand : IBotCommand
{
    public string Command => "/onboarding";
    public string Description => "Run the onboarding interview to set up your agent's identity and personality.";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        // Full implementation in M6 — requires file tools (M5) and the onboarding SKILL.md.
        await bot.SendMarkdownAsync(
            message.Chat.Id,
            Markdown.Escape("Onboarding is not yet available. It requires the file tool system (M5) and skill system (M6) to be complete."),
            ct);
    }
}
