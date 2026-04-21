namespace ARIA.Core.Models;

public sealed record ToolDefinition(
    string Name,
    string Description,
    string ParametersSchemaJson);

public sealed record ToolInvocation(
    string ToolCallId,
    string ToolName,
    string ArgumentsJson);

public sealed record ToolInvocationResult(
    string ToolCallId,
    string ToolName,
    string ResultJson,
    bool IsError = false);
