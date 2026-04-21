namespace ARIA.Core.Models;

public sealed record LlmCapabilities(
    bool SupportsVision,
    bool SupportsToolCalling,
    bool SupportsStreaming);

public sealed record ChatMessage(
    string Role,
    string? Content,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? ToolCallId = null,
    IReadOnlyList<ImageAttachment>? Images = null);

public sealed record ToolCall(
    string Id,
    string ToolName,
    string ArgumentsJson);

public sealed record LlmResponse(
    string? TextContent,
    IReadOnlyList<ToolCall>? ToolCalls,
    bool IsComplete,
    int? PromptTokens = null,
    int? CompletionTokens = null);

public sealed record ImageAttachment(
    string Base64Data,
    string MimeType);
