using System.Text.Json;
using ARIA.Core.Models;

namespace ARIA.LlmAdapter.Ollama;

public sealed class ToolCallFallbackParser
{
    private static readonly string[] FenceMarkers = ["```json", "```"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<ToolCall> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        foreach (var candidate in EnumerateCandidates(text))
        {
            var parsed = TryParseCandidate(candidate);
            if (parsed.Count > 0)
                return parsed;
        }

        return [];
    }

    private static IEnumerable<string> EnumerateCandidates(string text)
    {
        yield return text.Trim();

        var searchStart = 0;
        while (searchStart < text.Length)
        {
            var start = text.IndexOf("```", searchStart, StringComparison.Ordinal);
            if (start < 0)
                yield break;

            var contentStart = start + 3;
            if (text.AsSpan(contentStart).StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                contentStart += 4;
                if (contentStart < text.Length && text[contentStart] is '\r' or '\n')
                    contentStart++;
                if (contentStart < text.Length && text[contentStart - 1] == '\r' && text[contentStart] == '\n')
                    contentStart++;
            }

            var end = text.IndexOf("```", contentStart, StringComparison.Ordinal);
            if (end < 0)
                yield break;

            yield return text[contentStart..end].Trim();
            searchStart = end + 3;
        }
    }

    private static IReadOnlyList<ToolCall> TryParseCandidate(string candidate)
    {
        candidate = StripMarkers(candidate);
        if (candidate[0] is not ('{' or '['))
            return [];

        try
        {
            using var document = JsonDocument.Parse(candidate);
            return ParseRoot(document.RootElement);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<ToolCall> ParseRoot(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return ParseArray(root);

        if (root.ValueKind != JsonValueKind.Object)
            return [];

        if (root.TryGetProperty("tool_calls", out var toolCalls) ||
            root.TryGetProperty("toolCalls", out toolCalls))
            return ParseArray(toolCalls);

        var single = TryParseToolCall(root);
        return single is null ? [] : [single];
    }

    private static IReadOnlyList<ToolCall> ParseArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return [];

        var calls = new List<ToolCall>();
        foreach (var item in element.EnumerateArray())
        {
            var call = TryParseToolCall(item);
            if (call is not null)
                calls.Add(call);
        }

        return calls;
    }

    private static ToolCall? TryParseToolCall(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var id = GetString(element, "id") ?? Guid.NewGuid().ToString("N");
        var name = GetString(element, "name") ??
                   GetString(element, "tool_name") ??
                   GetString(element, "toolName");
        JsonElement? arguments = null;

        if (element.TryGetProperty("function", out var function) &&
            function.ValueKind == JsonValueKind.Object)
        {
            name ??= GetString(function, "name");
            if (function.TryGetProperty("arguments", out var functionArguments))
                arguments = functionArguments;
        }

        if (element.TryGetProperty("arguments", out var rootArguments))
            arguments = rootArguments;

        if (string.IsNullOrWhiteSpace(name))
            return null;

        var argumentsJson = arguments is { } args
            ? JsonSerializer.Serialize(args, JsonOptions)
            : "{}";

        return new ToolCall(id, name, argumentsJson);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string StripMarkers(string candidate)
    {
        foreach (var marker in FenceMarkers)
        {
            if (candidate.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                candidate = candidate[marker.Length..].Trim();
        }

        return candidate.EndsWith("```", StringComparison.Ordinal)
            ? candidate[..^3].Trim()
            : candidate;
    }
}
