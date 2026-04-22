using ARIA.Core.Options;
using ARIA.Core.Models;
using ARIA.Scheduler.Store;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ARIA.Scheduler.Tests;

public sealed class FileSystemJobStoreTests
{
    [Fact]
    public async Task LoadAsync_LoadsValidJobFile()
    {
        var root = CreateTempRoot();
        Directory.CreateDirectory(Path.Combine(root, "jobs"));
        await File.WriteAllTextAsync(Path.Combine(root, "jobs", "daily-briefing.json"), """
            {
              "name": "Daily Briefing",
              "schedule": { "kind": "cron", "expr": "30 7 * * *", "tz": "UTC" },
              "payload": { "kind": "agentTurn", "message": "Run the daily briefing skill." },
              "sessionTarget": "isolated",
              "enabled": true
            }
            """);

        var store = CreateStore(root);

        var jobs = await store.LoadAsync(new Dictionary<string, ScheduledJob>());

        jobs.Should().ContainSingle();
        var job = jobs[0];
        job.JobId.Should().Be("daily-briefing");
        job.IsActive.Should().BeTrue();
        job.CronExpression.Should().Be("30 7 * * *");
        job.TimeZoneId.Should().Be("UTC");
        job.Prompt.Should().Be("Run the daily briefing skill.");
    }

    [Fact]
    public async Task LoadAsync_ReturnsInvalidPendingJob_ForPartialJson()
    {
        var root = CreateTempRoot();
        Directory.CreateDirectory(Path.Combine(root, "jobs"));
        await File.WriteAllTextAsync(Path.Combine(root, "jobs", "draft.json"), "{");

        var store = CreateStore(root);

        var jobs = await store.LoadAsync(new Dictionary<string, ScheduledJob>());

        jobs.Should().ContainSingle();
        jobs[0].IsValid.Should().BeFalse();
        jobs[0].ValidationError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LoadAsync_DisablesJob_WhenFileNameStartsWithUnderscore()
    {
        var root = CreateTempRoot();
        Directory.CreateDirectory(Path.Combine(root, "jobs"));
        await File.WriteAllTextAsync(Path.Combine(root, "jobs", "_daily-briefing.json"), """
            {
              "name": "Daily Briefing",
              "schedule": { "kind": "cron", "expr": "30 7 * * *", "tz": "UTC" },
              "payload": { "kind": "agentTurn", "message": "Run the daily briefing skill." },
              "sessionTarget": "isolated",
              "enabled": true
            }
            """);

        var store = CreateStore(root);

        var jobs = await store.LoadAsync(new Dictionary<string, ScheduledJob>());

        jobs.Should().ContainSingle();
        jobs[0].IsValid.Should().BeTrue();
        jobs[0].DisabledByFileName.Should().BeTrue();
        jobs[0].IsActive.Should().BeFalse();
    }

    private static FileSystemJobStore CreateStore(string root) =>
        new(
            Options.Create(new AriaOptions
            {
                Workspace = new WorkspaceOptions { RootPath = root },
                Scheduler = new SchedulerOptions { Directory = "jobs" },
                Telegram = new TelegramOptions { AuthorizedUserIds = [42] }
            }),
            NullLogger<FileSystemJobStore>.Instance);

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "aria-scheduler-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
