namespace ARIA.Core.Interfaces;

public enum ContextFile
{
    Identity,
    Soul,
    User
}

public interface IContextFileStore
{
    Task<string> ReadAsync(ContextFile file, CancellationToken ct = default);
    Task WriteAsync(ContextFile file, string content, CancellationToken ct = default);
    void InvalidateCache(ContextFile file);
}
