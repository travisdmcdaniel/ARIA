using System.Text.Json;
using ARIA.Core.Models;
using ARIA.Core.Options;
using ARIA.Skills.BuiltIn.CreateScheduledJob;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ARIA.Skills.Tests;

public sealed class CreateScheduledJobExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WritesJobFile()
    {
        var root = CreateTempRoot();
        var executor = CreateExecutor(root);

        var result = await executor.ExecuteAsync(new ToolInvocation(
            "call-1",
            CreateScheduledJobDefinitions.CreateScheduledJob,
            """
            {
              "name": "test2",
              "schedule": { "kind": "cron", "expr": "0 10 * * *", "tz": "UTC" },
              "payload": { "kind": "agentTurn", "message": "Send a message to the user that the second test was completed successfully." },
              "sessionTarget": "isolated",
              "enabled": true
            }
            """));

        result.IsError.Should().BeFalse();
        var path = Path.Combine(root, "jobs", "test2.json");
        File.Exists(path).Should().BeTrue();

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        document.RootElement.GetProperty("name").GetString().Should().Be("test2");
        document.RootElement.GetProperty("schedule").GetProperty("expr").GetString().Should().Be("0 10 * * *");
        document.RootElement.GetProperty("payload").GetProperty("message").GetString()
            .Should().Be("Send a message to the user that the second test was completed successfully.");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsError_WhenCronExpressionIsNotFiveFields()
    {
        var executor = CreateExecutor(CreateTempRoot());

        var result = await executor.ExecuteAsync(new ToolInvocation(
            "call-1",
            CreateScheduledJobDefinitions.CreateScheduledJob,
            """
            {
              "name": "bad",
              "schedule": { "kind": "cron", "expr": "0 10 * *", "tz": "UTC" },
              "payload": { "kind": "agentTurn", "message": "Run." }
            }
            """));

        result.IsError.Should().BeTrue();
        result.ResultJson.Should().Contain("five-field cron expression");
    }

    private static CreateScheduledJobExecutor CreateExecutor(string root) =>
        new(
            Options.Create(new AriaOptions
            {
                Workspace = new WorkspaceOptions { RootPath = root },
                Scheduler = new SchedulerOptions { Directory = "jobs" }
            }));

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "aria-create-job-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
