using System.Text.Json;
using ARIA.Core.Exceptions;
using ARIA.Core.Interfaces;
using ARIA.Core.Models;
using ARIA.Core.Options;
using ARIA.Skills.Sandbox;
using Microsoft.Extensions.Options;

namespace ARIA.Skills.BuiltIn.ContextFileTools;

public sealed class ContextFileToolsExecutor(
    WorkspaceSandbox sandbox,
    IOptions<AriaOptions> options) : IToolExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _contextDirectory = Path.GetFullPath(
        options.Value.Workspace.GetResolvedContextDirectory())
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public IReadOnlyList<string> ToolNames { get; } =
    [
        ContextFileToolDefinitions.ReadContextFile,
        ContextFileToolDefinitions.WriteContextFile
    ];

    public async Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken ct = default)
    {
        try
        {
            var result = invocation.ToolName switch
            {
                ContextFileToolDefinitions.ReadContextFile => await ReadAsync(invocation.ArgumentsJson, ct),
                ContextFileToolDefinitions.WriteContextFile => await WriteAsync(invocation.ArgumentsJson, ct),
                _ => Error($"Unsupported context file tool: {invocation.ToolName}")
            };

            return new ToolInvocationResult(
                invocation.ToolCallId,
                invocation.ToolName,
                JsonSerializer.Serialize(result, JsonOptions),
                IsError: result is ToolError);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ToolInvocationResult(
                invocation.ToolCallId,
                invocation.ToolName,
                JsonSerializer.Serialize(Error(ex.Message), JsonOptions),
                IsError: true);
        }
    }

    private async Task<object> ReadAsync(string argumentsJson, CancellationToken ct)
    {
        var args = Parse<PathArgs>(argumentsJson);
        var path = ResolveContextPath(args.Path);
        var content = await File.ReadAllTextAsync(path, ct);
        return new { path = args.Path, content };
    }

    private async Task<object> WriteAsync(string argumentsJson, CancellationToken ct)
    {
        var args = Parse<WriteArgs>(argumentsJson);
        var path = ResolveContextPath(args.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, args.Content, ct);
        return new { path = args.Path, bytes = new FileInfo(path).Length };
    }

    private string ResolveContextPath(string relativePath)
    {
        var contextRelativePath = Path.GetRelativePath(sandbox.RootPath, _contextDirectory);
        if (contextRelativePath == ".." ||
            contextRelativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            contextRelativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new WorkspaceSandboxException("Context directory must be inside the workspace root.");

        var fullPath = sandbox.ResolveSafe(Path.Combine(contextRelativePath, relativePath));
        if (!IsWithinContextDirectory(fullPath))
            throw new WorkspaceSandboxException("Path escapes the configured context directory.");

        return fullPath;
    }

    private bool IsWithinContextDirectory(string fullPath) =>
        string.Equals(fullPath, _contextDirectory, StringComparison.OrdinalIgnoreCase) ||
        fullPath.StartsWith(_contextDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        fullPath.StartsWith(_contextDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static T Parse<T>(string argumentsJson)
    {
        var parsed = JsonSerializer.Deserialize<T>(argumentsJson, JsonOptions);
        return parsed ?? throw new ArgumentException("Invalid tool arguments.");
    }

    private static ToolError Error(string message) => new(message);

    private sealed record ToolError(string Error);

    private sealed class PathArgs
    {
        public required string Path { get; init; }
    }

    private sealed class WriteArgs
    {
        public required string Path { get; init; }
        public required string Content { get; init; }
    }
}
