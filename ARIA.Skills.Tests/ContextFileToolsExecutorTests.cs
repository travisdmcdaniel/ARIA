using System.Text.Json;
using ARIA.Core.Models;
using ARIA.Core.Options;
using ARIA.Skills.BuiltIn.ContextFileTools;
using ARIA.Skills.Sandbox;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ARIA.Skills.Tests;

public sealed class ContextFileToolsExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ReadsAndWritesOnlyUnderContextDirectory()
    {
        var root = CreateTempRoot();
        var executor = CreateExecutor(root);

        var write = await InvokeAsync(executor, ContextFileToolDefinitions.WriteContextFile, new
        {
            path = "IDENTITY.md",
            content = "# ARIA"
        });

        write.IsError.Should().BeFalse();
        File.ReadAllText(Path.Combine(root, "context", "IDENTITY.md")).Should().Be("# ARIA");

        var read = await InvokeAsync(executor, ContextFileToolDefinitions.ReadContextFile, new
        {
            path = "IDENTITY.md"
        });

        read.IsError.Should().BeFalse();
        read.ResultJson.Should().Contain("# ARIA");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsError_WhenContextPathEscapesContextDirectory()
    {
        var executor = CreateExecutor(CreateTempRoot());

        var result = await InvokeAsync(executor, ContextFileToolDefinitions.WriteContextFile, new
        {
            path = "../outside.md",
            content = "no"
        });

        result.IsError.Should().BeTrue();
        result.ResultJson.Should().Contain("escapes");
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotNestContextDirectory_WhenPathIncludesContextPrefix()
    {
        var root = CreateTempRoot();
        var executor = CreateExecutor(root);

        var result = await InvokeAsync(executor, ContextFileToolDefinitions.WriteContextFile, new
        {
            path = "context/IDENTITY.md",
            content = "# Identity"
        });

        result.IsError.Should().BeFalse();
        File.Exists(Path.Combine(root, "context", "IDENTITY.md")).Should().BeTrue();
        File.Exists(Path.Combine(root, "context", "context", "IDENTITY.md")).Should().BeFalse();
    }

    private static Task<ToolInvocationResult> InvokeAsync(
        ContextFileToolsExecutor executor,
        string toolName,
        object args) =>
        executor.ExecuteAsync(new ToolInvocation(
            "call-1",
            toolName,
            JsonSerializer.Serialize(args, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private static ContextFileToolsExecutor CreateExecutor(string root)
    {
        var options = Options.Create(new AriaOptions
        {
            Workspace =
            {
                RootPath = root,
                ContextDirectory = "context"
            }
        });

        return new ContextFileToolsExecutor(new WorkspaceSandbox(options), options);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "aria-context-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
