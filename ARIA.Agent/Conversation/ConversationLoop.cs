using System.Text.Json;
using ARIA.Agent.Prompts;
using ARIA.Core.Interfaces;
using ARIA.Core.Models;
using ARIA.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ARIA.Agent.Conversation;

public sealed class ConversationLoop : IAgentTurnHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILlmAdapter _llm;
    private readonly IConversationStore _conversationStore;
    private readonly IToolRegistry _toolRegistry;
    private readonly SystemPromptBuilder _promptBuilder;
    private readonly AgentOptions _agentOptions;
    private readonly PersonalityOptions _personalityOptions;
    private readonly ILogger<ConversationLoop> _logger;

    public ConversationLoop(
        ILlmAdapter llm,
        IConversationStore conversationStore,
        IToolRegistry toolRegistry,
        SystemPromptBuilder promptBuilder,
        IOptions<AriaOptions> options,
        ILogger<ConversationLoop> logger)
    {
        _llm = llm;
        _conversationStore = conversationStore;
        _toolRegistry = toolRegistry;
        _promptBuilder = promptBuilder;
        _agentOptions = options.Value.Agent;
        _personalityOptions = options.Value.Personality;
        _logger = logger;
    }

    public async Task<string> RunTurnAsync(
        long telegramUserId,
        string userText,
        IReadOnlyList<ImageAttachment>? images = null,
        CancellationToken ct = default)
    {
        if (images is { Count: > 0 } && !await SupportsVisionAsync(ct))
        {
            return "The current model does not support image input. Please switch to a vision-capable model or send a text message.";
        }

        var session = await _conversationStore.GetOrCreateActiveSessionAsync(telegramUserId, ct);
        var systemPrompt = await _promptBuilder.BuildAsync(session.SessionId, ct);

        var history = _personalityOptions.Memory.Enabled
            ? await _conversationStore.GetRecentTurnsAsync(
                session.SessionId, _agentOptions.MaxConversationTurns, ct)
            : (IReadOnlyList<ConversationTurn>)[];

        await _conversationStore.AppendTurnAsync(new ConversationTurn(
            TurnId: 0,
            SessionId: session.SessionId,
            TelegramUserId: telegramUserId,
            Timestamp: DateTime.UtcNow,
            Role: ConversationRole.User,
            TextContent: userText,
            ToolCallsJson: null,
            ToolResultJson: null,
            ImageDataJson: images is { Count: > 0 }
                ? JsonSerializer.Serialize(images, JsonOptions)
                : null), ct);

        var messages = BuildMessages(systemPrompt, history, userText, images);
        var tools = _toolRegistry.GetToolDefinitions();
        string? finalText = null;

        for (var iteration = 0; iteration < _agentOptions.MaxToolCallIterations; iteration++)
        {
            var response = await _llm.CompleteAsync(
                messages,
                tools.Count > 0 ? tools : null,
                ct);

            if (response.ToolCalls is { Count: > 0 })
            {
                messages.Add(new ChatMessage(
                    ConversationRole.Assistant,
                    response.TextContent,
                    ToolCalls: response.ToolCalls));

                await _conversationStore.AppendTurnAsync(new ConversationTurn(
                    TurnId: 0,
                    SessionId: session.SessionId,
                    TelegramUserId: telegramUserId,
                    Timestamp: DateTime.UtcNow,
                    Role: ConversationRole.Assistant,
                    TextContent: response.TextContent,
                    ToolCallsJson: JsonSerializer.Serialize(response.ToolCalls, JsonOptions),
                    ToolResultJson: null,
                    ImageDataJson: null), ct);

                foreach (var toolCall in response.ToolCalls)
                {
                    var result = await ExecuteToolAsync(toolCall, ct);

                    messages.Add(new ChatMessage(
                        ConversationRole.Tool,
                        result.ResultJson,
                        ToolCallId: result.ToolCallId));

                    await _conversationStore.AppendTurnAsync(new ConversationTurn(
                        TurnId: 0,
                        SessionId: session.SessionId,
                        TelegramUserId: telegramUserId,
                        Timestamp: DateTime.UtcNow,
                        Role: ConversationRole.Tool,
                        TextContent: null,
                        ToolCallsJson: null,
                        ToolResultJson: JsonSerializer.Serialize(result, JsonOptions),
                        ImageDataJson: null), ct);
                }
            }
            else
            {
                finalText = response.TextContent ?? string.Empty;
                break;
            }
        }

        finalText ??= "I reached the maximum number of tool call iterations without producing a response.";

        await _conversationStore.AppendTurnAsync(new ConversationTurn(
            TurnId: 0,
            SessionId: session.SessionId,
            TelegramUserId: telegramUserId,
            Timestamp: DateTime.UtcNow,
            Role: ConversationRole.Assistant,
            TextContent: finalText,
            ToolCallsJson: null,
            ToolResultJson: null,
            ImageDataJson: null), ct);

        return finalText;
    }

    private async Task<bool> SupportsVisionAsync(CancellationToken ct)
    {
        if (_llm.Capabilities is { } capabilities)
            return capabilities.SupportsVision;

        try
        {
            capabilities = await _llm.DetectCapabilitiesAsync(ct);
            return capabilities.SupportsVision;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not detect LLM capabilities while processing image input");
            return false;
        }
    }

    private async Task<ToolInvocationResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken ct)
    {
        var executor = _toolRegistry.GetExecutor(toolCall.ToolName);
        if (executor is null)
        {
            _logger.LogWarning("No executor found for tool {ToolName}", toolCall.ToolName);
            return new ToolInvocationResult(
                toolCall.Id,
                toolCall.ToolName,
                JsonSerializer.Serialize(new { error = $"Unknown tool: {toolCall.ToolName}" }, JsonOptions),
                IsError: true);
        }

        try
        {
            return await executor.ExecuteAsync(
                new ToolInvocation(toolCall.Id, toolCall.ToolName, toolCall.ArgumentsJson),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {ToolName} threw an unhandled exception", toolCall.ToolName);
            return new ToolInvocationResult(
                toolCall.Id,
                toolCall.ToolName,
                JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions),
                IsError: true);
        }
    }

    private static List<ChatMessage> BuildMessages(
        string systemPrompt,
        IReadOnlyList<ConversationTurn> history,
        string userText,
        IReadOnlyList<ImageAttachment>? images)
    {
        var messages = new List<ChatMessage>
        {
            new(ConversationRole.System, systemPrompt)
        };

        foreach (var turn in history)
        {
            var msg = MapTurnToMessage(turn);
            if (msg is not null)
                messages.Add(msg);
        }

        messages.Add(new ChatMessage(
            ConversationRole.User,
            userText,
            Images: images is { Count: > 0 } ? images : null));

        return messages;
    }

    private static ChatMessage? MapTurnToMessage(ConversationTurn turn)
    {
        switch (turn.Role)
        {
            case ConversationRole.User:
            {
                IReadOnlyList<ImageAttachment>? images = null;
                if (!string.IsNullOrEmpty(turn.ImageDataJson))
                    images = JsonSerializer.Deserialize<List<ImageAttachment>>(turn.ImageDataJson, JsonOptions);
                return new ChatMessage(ConversationRole.User, turn.TextContent, Images: images);
            }

            case ConversationRole.Assistant:
            {
                IReadOnlyList<ToolCall>? toolCalls = null;
                if (!string.IsNullOrEmpty(turn.ToolCallsJson))
                    toolCalls = JsonSerializer.Deserialize<List<ToolCall>>(turn.ToolCallsJson, JsonOptions);
                return new ChatMessage(ConversationRole.Assistant, turn.TextContent, ToolCalls: toolCalls);
            }

            case ConversationRole.Tool:
            {
                if (string.IsNullOrEmpty(turn.ToolResultJson))
                    return null;
                var result = JsonSerializer.Deserialize<ToolInvocationResult>(turn.ToolResultJson, JsonOptions);
                if (result is null)
                    return null;
                return new ChatMessage(ConversationRole.Tool, result.ResultJson, ToolCallId: result.ToolCallId);
            }

            default:
                return null;
        }
    }
}
