using ARIA.Core.Exceptions;
using ARIA.Core.Options;
using ARIA.Skills.Sandbox;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ARIA.Skills.Tests;

public sealed class WorkspaceSandboxTests
{
    [Fact]
    public void ResolveSafe_ReturnsWorkspacePath_ForRelativePath()
    {
        var root = CreateTempRoot();
        var sandbox = CreateSandbox(root);

        var resolved = sandbox.ResolveSafe("notes/today.md");

        resolved.Should().Be(Path.Combine(root, "notes", "today.md"));
    }

    [Fact]
    public void ResolveSafe_RejectsTraversalOutsideWorkspace()
    {
        var sandbox = CreateSandbox(CreateTempRoot());

        var act = () => sandbox.ResolveSafe("../outside.txt");

        act.Should().Throw<WorkspaceSandboxException>();
    }

    [Fact]
    public void ResolveSafe_RejectsAbsolutePathOutsideWorkspace()
    {
        var sandbox = CreateSandbox(CreateTempRoot());

        var act = () => sandbox.ResolveSafe(Path.GetTempPath());

        act.Should().Throw<WorkspaceSandboxException>();
    }

    private static WorkspaceSandbox CreateSandbox(string root) =>
        new(Options.Create(new AriaOptions
        {
            Workspace =
            {
                RootPath = root
            }
        }));

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "aria-sandbox-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
