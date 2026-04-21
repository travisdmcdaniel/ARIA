using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

/// <summary>Full implementation in M6: calls ISkillStore.ReloadAsync().</summary>
public sealed class ReloadSkillsCommand : IBotCommand
{
    public string Command => "/reloadskills";
    public string Description => "Hot-reload all SKILL.md files from the skills directory.";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        await bot.SendMarkdownAsync(
            message.Chat.Id,
            Markdown.Italic("Skill reloading will be available in M6 (skill engine)."),
            ct);
    }
}
