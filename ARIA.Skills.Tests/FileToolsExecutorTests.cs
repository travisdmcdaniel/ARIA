using System.Text.Json;
using ARIA.Core.Models;
using ARIA.Core.Options;
using ARIA.Skills.BuiltIn.FileTools;
using ARIA.Skills.Sandbox;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ARIA.Skills.Tests;

public sealed class FileToolsExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WritesReadsAppendsListsMovesAndDeletesFiles()
    {
        var root = CreateTempRoot();
        var executor = CreateExecutor(root);

        var write = await InvokeAsync(executor, FileToolDefinitions.WriteFile, new
        {
            path = "notes/today.md",
            content = "hello"
        });
        write.IsError.Should().BeFalse();

        var append = await InvokeAsync(executor, FileToolDefinitions.AppendFile, new
        {
            path = "notes/today.md",
            content = "\\nworld"
        });
        append.IsError.Should().BeFalse();

        var read = await InvokeAsync(executor, FileToolDefinitions.ReadFile, new
        {
            path = "notes/today.md"
        });
        using var readJson = JsonDocument.Parse(read.ResultJson);
        readJson.RootElement.GetProperty("content").GetString()
            .Should().Be($"hello{Environment.NewLine}world");

        var list = await InvokeAsync(executor, FileToolDefinitions.ListDirectory, new
        {
            path = "notes"
        });
        list.ResultJson.Should().Contain("today.md");

        var move = await InvokeAsync(executor, FileToolDefinitions.MoveFile, new
        {
            source_path = "notes/today.md",
            destination_path = "archive/today.md"
        });
        move.IsError.Should().BeFalse();
        File.Exists(Path.Combine(root, "archive", "today.md")).Should().BeTrue();

        var exists = await InvokeAsync(executor, FileToolDefinitions.FileExists, new
        {
            path = "archive/today.md"
        });
        exists.ResultJson.Should().Contain("\"exists\":true");

        var delete = await InvokeAsync(executor, FileToolDefinitions.DeleteFile, new
        {
            path = "archive/today.md"
        });
        delete.IsError.Should().BeFalse();
        File.Exists(Path.Combine(root, "archive", "today.md")).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsError_ForPathTraversal()
    {
        var executor = CreateExecutor(CreateTempRoot());

        var result = await InvokeAsync(executor, FileToolDefinitions.ReadFile, new
        {
            path = "../outside.txt"
        });

        result.IsError.Should().BeTrue();
        result.ResultJson.Should().Contain("escapes");
    }

    private static Task<ToolInvocationResult> InvokeAsync(
        FileToolsExecutor executor,
        string toolName,
        object args) =>
        executor.ExecuteAsync(new ToolInvocation(
            "call-1",
            toolName,
            JsonSerializer.Serialize(args, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private static FileToolsExecutor CreateExecutor(string root) =>
        new(new WorkspaceSandbox(Options.Create(new AriaOptions
        {
            Workspace =
            {
                RootPath = root
            }
        })));

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "aria-file-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
