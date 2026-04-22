using ARIA.Core.Interfaces;
using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

public sealed class CancelJobCommand(ISchedulerService scheduler) : IBotCommand
{
    public string Command => "/canceljob";
    public string Description => "Disable a scheduled job file: /canceljob <id-or-filename>";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var parts = (message.Text ?? string.Empty).Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await bot.SendMarkdownAsync(
                message.Chat.Id,
                Markdown.Escape("Usage: /canceljob <id-or-filename>"),
                ct);
            return;
        }

        var jobId = parts[1].Trim();
        var before = await scheduler.GetJobsAsync(ct);
        var exists = before.Any(j =>
            string.Equals(j.JobId, jobId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(j.FileName, jobId, StringComparison.OrdinalIgnoreCase));

        if (!exists)
        {
            await bot.SendMarkdownAsync(
                message.Chat.Id,
                Markdown.Escape($"No scheduled job found for '{jobId}'."),
                ct);
            return;
        }

        await scheduler.DisableJobAsync(jobId, ct);

        await bot.SendMarkdownAsync(
            message.Chat.Id,
            Markdown.Escape($"Disabled scheduled job '{jobId}'."),
            ct);
    }
}
