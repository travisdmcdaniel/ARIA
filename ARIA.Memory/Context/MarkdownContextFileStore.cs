using ARIA.Core.Interfaces;
using ARIA.Core.Options;
using Microsoft.Extensions.Options;

namespace ARIA.Memory.Context;

/// <summary>
/// Markdown file-backed context file store (IDENTITY.md, SOUL.md, USER.md).
/// Stub implementation — filled in during M9.
/// </summary>
public sealed class MarkdownContextFileStore : IContextFileStore
{
    private readonly string _contextDirectory;

    public MarkdownContextFileStore(IOptions<AriaOptions> options)
    {
        _contextDirectory = options.Value.Workspace.GetResolvedContextDirectory();
    }

    public Task<string> ReadAsync(ContextFile file, CancellationToken ct = default)
        => throw new NotImplementedException("MarkdownContextFileStore not yet implemented (M9)");

    public Task WriteAsync(ContextFile file, string content, CancellationToken ct = default)
        => throw new NotImplementedException("MarkdownContextFileStore not yet implemented (M9)");

    public void InvalidateCache(ContextFile file)
    {
        // No-op until M9
    }
}
