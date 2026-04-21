using System.Text.Json;
using System.Text.Json.Serialization;
using ARIA.Core.Interfaces;
using ARIA.Core.Models;
using ARIA.Skills.Sandbox;

namespace ARIA.Skills.BuiltIn.FileTools;

public sealed class FileToolsExecutor(WorkspaceSandbox sandbox) : IToolExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<string> ToolNames { get; } =
    [
        FileToolDefinitions.ReadFile,
        FileToolDefinitions.WriteFile,
        FileToolDefinitions.AppendFile,
        FileToolDefinitions.ListDirectory,
        FileToolDefinitions.CreateDirectory,
        FileToolDefinitions.DeleteFile,
        FileToolDefinitions.MoveFile,
        FileToolDefinitions.FileExists
    ];

    public async Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken ct = default)
    {
        try
        {
            var result = invocation.ToolName switch
            {
                FileToolDefinitions.ReadFile => await ReadFileAsync(invocation.ArgumentsJson, ct),
                FileToolDefinitions.WriteFile => await WriteFileAsync(invocation.ArgumentsJson, append: false, ct),
                FileToolDefinitions.AppendFile => await WriteFileAsync(invocation.ArgumentsJson, append: true, ct),
                FileToolDefinitions.ListDirectory => ListDirectory(invocation.ArgumentsJson),
                FileToolDefinitions.CreateDirectory => CreateDirectory(invocation.ArgumentsJson),
                FileToolDefinitions.DeleteFile => DeleteFile(invocation.ArgumentsJson),
                FileToolDefinitions.MoveFile => MoveFile(invocation.ArgumentsJson),
                FileToolDefinitions.FileExists => FileExists(invocation.ArgumentsJson),
                _ => Error($"Unsupported file tool: {invocation.ToolName}")
            };

            return new ToolInvocationResult(
                invocation.ToolCallId,
                invocation.ToolName,
                ToJson(result),
                IsError: result is ToolError);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ToolInvocationResult(
                invocation.ToolCallId,
                invocation.ToolName,
                ToJson(Error(ex.Message)),
                IsError: true);
        }
    }

    private string ResolveSafe(string relativePath) => sandbox.ResolveSafe(relativePath);

    private async Task<object> ReadFileAsync(string argumentsJson, CancellationToken ct)
    {
        var args = Parse<PathArgs>(argumentsJson);
        var path = ResolveSafe(args.Path);
        var content = await File.ReadAllTextAsync(path, ct);
        return new { path = args.Path, content };
    }

    private async Task<object> WriteFileAsync(string argumentsJson, bool append, CancellationToken ct)
    {
        var args = Parse<WriteFileArgs>(argumentsJson);
        var path = ResolveSafe(args.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var content = append ? NormalizeAppendContent(args.Content) : args.Content;

        if (append)
            await File.AppendAllTextAsync(path, content, ct);
        else
            await File.WriteAllTextAsync(path, content, ct);

        return new
        {
            path = args.Path,
            bytes = File.Exists(path) ? new FileInfo(path).Length : 0,
            operation = append ? "append" : "write"
        };
    }

    private object ListDirectory(string argumentsJson)
    {
        var args = Parse<PathArgs>(argumentsJson);
        var path = ResolveSafe(args.Path);
        var entries = Directory.EnumerateFileSystemEntries(path)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => new
            {
                name = Path.GetFileName(p),
                type = Directory.Exists(p) ? "directory" : "file",
                size = File.Exists(p) ? new FileInfo(p).Length : (long?)null
            })
            .ToArray();

        return new { path = args.Path, entries };
    }

    private object CreateDirectory(string argumentsJson)
    {
        var args = Parse<PathArgs>(argumentsJson);
        var path = ResolveSafe(args.Path);
        Directory.CreateDirectory(path);
        return new { path = args.Path, created = true };
    }

    private object DeleteFile(string argumentsJson)
    {
        var args = Parse<PathArgs>(argumentsJson);
        var path = ResolveSafe(args.Path);
        if (!File.Exists(path))
            return new { path = args.Path, deleted = false };

        File.Delete(path);
        return new { path = args.Path, deleted = true };
    }

    private object MoveFile(string argumentsJson)
    {
        var args = Parse<MoveFileArgs>(argumentsJson);
        var source = ResolveSafe(args.SourcePath);
        var destination = ResolveSafe(args.DestinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (Directory.Exists(source))
        {
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new IOException("Destination already exists.");

            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination, args.Overwrite);
        }

        return new
        {
            source_path = args.SourcePath,
            destination_path = args.DestinationPath,
            moved = true
        };
    }

    private object FileExists(string argumentsJson)
    {
        var args = Parse<PathArgs>(argumentsJson);
        var path = ResolveSafe(args.Path);
        return new
        {
            path = args.Path,
            exists = File.Exists(path) || Directory.Exists(path),
            type = Directory.Exists(path) ? "directory" : File.Exists(path) ? "file" : null
        };
    }

    private static T Parse<T>(string argumentsJson)
    {
        var parsed = JsonSerializer.Deserialize<T>(argumentsJson, JsonOptions);
        return parsed ?? throw new ArgumentException("Invalid tool arguments.");
    }

    private static string NormalizeAppendContent(string content) =>
        content
            .Replace("\\r\\n", Environment.NewLine, StringComparison.Ordinal)
            .Replace("\\n", Environment.NewLine, StringComparison.Ordinal)
            .Replace("\\r", Environment.NewLine, StringComparison.Ordinal);

    private static ToolError Error(string message) => new(message);

    private static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private sealed record ToolError(string Error);

    private sealed class PathArgs
    {
        public required string Path { get; init; }
    }

    private sealed class WriteFileArgs
    {
        public required string Path { get; init; }
        public required string Content { get; init; }
    }

    private sealed class MoveFileArgs
    {
        [JsonPropertyName("source_path")]
        public required string SourcePath { get; init; }

        [JsonPropertyName("destination_path")]
        public required string DestinationPath { get; init; }

        public bool Overwrite { get; init; }
    }
}
