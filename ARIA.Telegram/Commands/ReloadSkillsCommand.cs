using ARIA.Core.Interfaces;
using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

public sealed class ReloadSkillsCommand(ISkillStore skillStore) : IBotCommand
{
    public string Command => "/reloadskills";
    public string Description => "Hot-reload all SKILL.md files from the skills directory.";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        await skillStore.ReloadAsync(ct);
        var count = skillStore.GetAll().Count;

        await bot.SendMarkdownAsync(
            message.Chat.Id,
            $"{Markdown.Bold("Skills Reloaded")}\n\n{Markdown.Escape($"Loaded {count} skill(s).")}",
            ct);
    }
}
