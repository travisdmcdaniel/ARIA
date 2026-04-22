using System.Text;
using ARIA.Core.Interfaces;
using ARIA.Core.Models;
using ARIA.Telegram.Helpers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

public sealed class JobsCommand(ISchedulerService scheduler) : IBotCommand
{
    public string Command => "/jobs";
    public string Description => "List scheduled jobs loaded from workspace job files.";

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var jobs = await scheduler.GetJobsAsync(ct);
        if (jobs.Count == 0)
        {
            await bot.SendMarkdownAsync(
                message.Chat.Id,
                Markdown.Escape("No scheduled job files found."),
                ct);
            return;
        }

        await bot.SendMarkdownAsync(
            message.Chat.Id,
            FormatJobs(jobs),
            ct);
    }

    private static string FormatJobs(IReadOnlyList<ScheduledJob> jobs)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Markdown.Bold("Scheduled Jobs"));
        sb.AppendLine();

        foreach (var job in jobs)
        {
            sb.Append(Markdown.Code(job.JobId));
            sb.Append(" — ");
            sb.Append(Markdown.Escape(job.Name));
            sb.Append(" — ");

            if (!job.IsValid)
            {
                sb.Append(Markdown.Escape($"invalid: {job.ValidationError}"));
            }
            else if (!job.IsActive)
            {
                sb.Append(Markdown.Escape("disabled"));
            }
            else
            {
                sb.Append(Markdown.Escape($"{job.CronExpression} {job.TimeZoneId}"));
                if (job.NextFireAt is { } next)
                    sb.Append(Markdown.Escape($" — next {FormatUtc(next)}"));
            }

            sb.AppendLine();
            sb.Append("  ");
            sb.Append(Markdown.Escape(job.FileName));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string FormatUtc(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'");
}
