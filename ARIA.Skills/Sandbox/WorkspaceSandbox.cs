using ARIA.Core.Exceptions;
using ARIA.Core.Options;
using Microsoft.Extensions.Options;

namespace ARIA.Skills.Sandbox;

public sealed class WorkspaceSandbox
{
    private readonly string _rootPath;

    public WorkspaceSandbox(IOptions<AriaOptions> options)
    {
        _rootPath = NormalizeRoot(options.Value.Workspace.GetResolvedRootPath());
        Directory.CreateDirectory(_rootPath);
    }

    public string RootPath => _rootPath;

    public string ResolveSafe(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new WorkspaceSandboxException("Path is required.");

        var combined = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(_rootPath, relativePath);

        var fullPath = Path.GetFullPath(combined);
        if (!IsWithinRoot(fullPath))
            throw new WorkspaceSandboxException("Path escapes the configured workspace root.");

        RejectSymlinkSegments(fullPath);
        return fullPath;
    }

    private bool IsWithinRoot(string fullPath) =>
        string.Equals(fullPath, _rootPath, StringComparison.OrdinalIgnoreCase) ||
        fullPath.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        fullPath.StartsWith(_rootPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private void RejectSymlinkSegments(string fullPath)
    {
        var relative = Path.GetRelativePath(_rootPath, fullPath);
        if (relative == ".")
            return;

        var current = _rootPath;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
                continue;

            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new WorkspaceSandboxException("Path contains a symlink or reparse point.");
        }
    }

    private static string NormalizeRoot(string rootPath)
    {
        var fullPath = Path.GetFullPath(rootPath);
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
