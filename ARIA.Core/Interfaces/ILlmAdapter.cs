using ARIA.Core.Models;

namespace ARIA.Core.Interfaces;

public interface ILlmAdapter
{
    LlmCapabilities? Capabilities { get; }

    Task<LlmCapabilities> DetectCapabilitiesAsync(CancellationToken ct = default);

    Task<LlmResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken ct = default);

    IAsyncEnumerable<LlmResponse> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        CancellationToken ct = default);

    Task<bool> CheckConnectivityAsync(CancellationToken ct = default);
}
