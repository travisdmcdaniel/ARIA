using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ARIA.Core.Interfaces;
using ARIA.Core.Models;
using ARIA.Core.Options;

namespace ARIA.LlmAdapter.Ollama;

public sealed class OllamaAdapter : ILlmAdapter, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly OllamaOptions _options;
    private readonly HttpClient _http;
    private readonly bool _disposeHttpClient;
    private readonly OllamaRequestBuilder _requestBuilder;
    private readonly ToolCallFallbackParser _fallbackParser;

    public OllamaAdapter(OllamaOptions options)
        : this(options, new HttpClient(), disposeHttpClient: true)
    {
    }

    public OllamaAdapter(
        OllamaOptions options,
        HttpClient httpClient,
        OllamaRequestBuilder? requestBuilder = null,
        ToolCallFallbackParser? fallbackParser = null,
        bool disposeHttpClient = false)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _disposeHttpClient = disposeHttpClient;
        _requestBuilder = requestBuilder ?? new OllamaRequestBuilder();
        _fallbackParser = fallbackParser ?? new ToolCallFallbackParser();

        _http.BaseAddress ??= NormalizeBaseUri(options.BaseUrl);
    }

    public LlmCapabilities? Capabilities { get; private set; }

    public async Task<LlmCapabilities> DetectCapabilitiesAsync(CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(
            "api/show",
            new { model = _options.Model },
            JsonOptions,
            ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var show = await JsonSerializer.DeserializeAsync<OllamaShowResponse>(stream, JsonOptions, ct)
                   ?? new OllamaShowResponse();

        var supportsVision = ContainsClipProjector(show) || ContainsCapability(show, "vision");
        var supportsTools = ContainsCapability(show, "tools") || ContainsCapability(show, "tool");

        Capabilities = new LlmCapabilities(
            SupportsVision: supportsVision,
            SupportsToolCalling: supportsTools,
            SupportsStreaming: true);

        return Capabilities;
    }

    public async Task<LlmResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken ct = default)
    {
        var request = BuildRequest(messages, tools, stream: false);
        using var response = await _http.PostAsJsonAsync("api/chat", request, JsonOptions, ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var chatResponse = await JsonSerializer.DeserializeAsync<OllamaChatResponse>(stream, JsonOptions, ct)
                           ?? new OllamaChatResponse();

        return MapCompleteResponse(chatResponse);
    }

    public async IAsyncEnumerable<LlmResponse> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (tools is { Count: > 0 })
        {
            yield return await CompleteAsync(messages, tools, ct);
            yield break;
        }

        var request = BuildRequest(messages, tools, stream: true);
        using var response = await _http.PostAsJsonAsync("api/chat", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = new StringBuilder();

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line, JsonOptions);
            if (chunk is null)
                continue;

            var content = chunk.Message?.Content;
            if (!string.IsNullOrEmpty(content))
            {
                text.Append(content);
                yield return new LlmResponse(content, null, IsComplete: false);
            }

            if (chunk.Done)
            {
                var nativeCalls = MapToolCalls(chunk.Message?.ToolCalls);
                var calls = nativeCalls.Count > 0 ? nativeCalls : _fallbackParser.Parse(text.ToString());

                yield return new LlmResponse(
                    TextContent: calls.Count > 0 ? null : string.Empty,
                    ToolCalls: calls.Count > 0 ? calls : null,
                    IsComplete: true,
                    PromptTokens: chunk.PromptEvalCount,
                    CompletionTokens: chunk.EvalCount);
            }
        }
    }

    public async Task<bool> CheckConnectivityAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(string.Empty, ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
            _http.Dispose();
    }

    private OllamaChatRequest BuildRequest(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        bool stream)
    {
        return _requestBuilder.BuildChatRequest(
            _options.Model,
            messages,
            tools,
            stream,
            Capabilities?.SupportsVision ?? false);
    }

    private LlmResponse MapCompleteResponse(OllamaChatResponse response)
    {
        var text = response.Message?.Content;
        var nativeCalls = MapToolCalls(response.Message?.ToolCalls);
        var calls = nativeCalls.Count > 0 ? nativeCalls : _fallbackParser.Parse(text);

        return new LlmResponse(
            TextContent: calls.Count > 0 ? null : text,
            ToolCalls: calls.Count > 0 ? calls : null,
            IsComplete: true,
            PromptTokens: response.PromptEvalCount,
            CompletionTokens: response.EvalCount);
    }

    private static IReadOnlyList<ToolCall> MapToolCalls(IReadOnlyList<OllamaToolCall>? toolCalls)
    {
        if (toolCalls is not { Count: > 0 })
            return [];

        var calls = new List<ToolCall>();
        foreach (var toolCall in toolCalls)
        {
            var function = toolCall.Function;
            if (string.IsNullOrWhiteSpace(function.Name))
                continue;

            var arguments = function.Arguments.ValueKind == JsonValueKind.Undefined
                ? "{}"
                : JsonSerializer.Serialize(function.Arguments, JsonOptions);

            calls.Add(new ToolCall(
                string.IsNullOrWhiteSpace(toolCall.Id) ? Guid.NewGuid().ToString("N") : toolCall.Id,
                function.Name,
                arguments));
        }

        return calls;
    }

    private static bool ContainsClipProjector(OllamaShowResponse show)
    {
        if (show.Projector.ValueKind == JsonValueKind.Undefined ||
            show.Projector.ValueKind == JsonValueKind.Null)
            return false;

        var projectorJson = show.Projector.GetRawText();
        return projectorJson.Contains("clip", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsCapability(OllamaShowResponse show, string capability)
    {
        return show.Capabilities?.Any(c =>
            string.Equals(c, capability, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static Uri NormalizeBaseUri(string baseUrl)
    {
        var uri = new Uri(string.IsNullOrWhiteSpace(baseUrl)
            ? "http://localhost:11434"
            : baseUrl);

        var value = uri.ToString();
        return value.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(value + "/");
    }
}

internal sealed class OllamaChatResponse
{
    [JsonPropertyName("message")]
    public OllamaChatMessage? Message { get; init; }

    [JsonPropertyName("done")]
    public bool Done { get; init; }

    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; init; }

    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; init; }
}

internal sealed class OllamaShowResponse
{
    [JsonPropertyName("projector_info")]
    public JsonElement Projector { get; init; }

    [JsonPropertyName("capabilities")]
    public string[]? Capabilities { get; init; }
}
