using ARIA.Core.Interfaces;
using ARIA.Core.Options;
using Microsoft.Extensions.Options;

namespace ARIA.Memory.Context;

public sealed class MarkdownContextFileStore : IContextFileStore
{
    private readonly string _contextDirectory;

    public MarkdownContextFileStore(IOptions<AriaOptions> options)
    {
        _contextDirectory = options.Value.Workspace.GetResolvedContextDirectory();
    }

    public Task<string> ReadAsync(ContextFile file, CancellationToken ct = default)
        => File.ReadAllTextAsync(GetPath(file), ct);

    public async Task WriteAsync(ContextFile file, string content, CancellationToken ct = default)
    {
        var path = GetPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, ct);
    }

    public void InvalidateCache(ContextFile file)
    {
        // No in-memory cache until M9 adds FileSystemWatcher + Dictionary cache.
    }

    private string GetPath(ContextFile file) => file switch
    {
        ContextFile.Identity => Path.Combine(_contextDirectory, "IDENTITY.md"),
        ContextFile.Soul     => Path.Combine(_contextDirectory, "SOUL.md"),
        ContextFile.User     => Path.Combine(_contextDirectory, "USER.md"),
        _                    => throw new ArgumentOutOfRangeException(nameof(file))
    };
}
