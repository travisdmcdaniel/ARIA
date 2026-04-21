using System.Text.Json;
using System.Text.Json.Serialization;
using ARIA.Core.Models;

namespace ARIA.LlmAdapter.Ollama;

public sealed class OllamaRequestBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public OllamaChatRequest BuildChatRequest(
        string model,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        bool stream,
        bool supportsVision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(messages);

        return new OllamaChatRequest
        {
            Model = model,
            Stream = stream,
            Messages = messages.Select(message => BuildMessage(message, supportsVision)).ToArray(),
            Tools = tools is { Count: > 0 }
                ? tools.Select(BuildTool).ToArray()
                : null
        };
    }

    private static OllamaChatMessage BuildMessage(ChatMessage message, bool supportsVision)
    {
        var images = supportsVision && message.Images is { Count: > 0 }
            ? message.Images.Select(i => i.Base64Data).ToArray()
            : null;

        return new OllamaChatMessage
        {
            Role = NormalizeRole(message.Role),
            Content = message.Content ?? string.Empty,
            Images = images,
            ToolCallId = message.ToolCallId,
            ToolCalls = message.ToolCalls is { Count: > 0 }
                ? message.ToolCalls.Select(BuildToolCall).ToArray()
                : null
        };
    }

    private static OllamaTool BuildTool(ToolDefinition tool)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool.Name);

        return new OllamaTool
        {
            Function = new OllamaToolFunction
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = ParseParameters(tool.ParametersSchemaJson)
            }
        };
    }

    private static OllamaToolCall BuildToolCall(ToolCall toolCall)
    {
        return new OllamaToolCall
        {
            Id = toolCall.Id,
            Function = new OllamaToolCallFunction
            {
                Name = toolCall.ToolName,
                Arguments = ParseArguments(toolCall.ArgumentsJson)
            }
        };
    }

    private static JsonElement ParseParameters(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return JsonSerializer.SerializeToElement(new { type = "object" }, JsonOptions);

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement ParseArguments(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return JsonSerializer.SerializeToElement(new { }, JsonOptions);

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string NormalizeRole(string role) =>
        role.Trim().ToLowerInvariant() switch
        {
            "system" => "system",
            "assistant" => "assistant",
            "tool" => "tool",
            _ => "user"
        };
}

public sealed class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<OllamaChatMessage> Messages { get; init; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OllamaTool>? Tools { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }
}

public sealed class OllamaChatMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    [JsonPropertyName("images")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Images { get; init; }

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; init; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OllamaToolCall>? ToolCalls { get; init; }
}

public sealed class OllamaTool
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public required OllamaToolFunction Function { get; init; }
}

public sealed class OllamaToolFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; init; }
}

public sealed class OllamaToolCall
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonPropertyName("function")]
    public required OllamaToolCallFunction Function { get; init; }
}

public sealed class OllamaToolCallFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; init; }
}
