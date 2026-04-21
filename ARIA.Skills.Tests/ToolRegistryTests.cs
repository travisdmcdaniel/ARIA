using ARIA.Core.Options;
using ARIA.Skills.BuiltIn;
using ARIA.Skills.BuiltIn.ContextFileTools;
using ARIA.Skills.BuiltIn.FileTools;
using ARIA.Skills.Sandbox;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ARIA.Skills.Tests;

public sealed class ToolRegistryTests
{
    [Fact]
    public void GetToolDefinitions_ReturnsFileAndContextTools()
    {
        var registry = CreateRegistry();

        var names = registry.GetToolDefinitions().Select(d => d.Name);

        names.Should().Contain([
            FileToolDefinitions.ReadFile,
            FileToolDefinitions.WriteFile,
            ContextFileToolDefinitions.ReadContextFile,
            ContextFileToolDefinitions.WriteContextFile
        ]);
    }

    [Fact]
    public void GetExecutor_ReturnsExecutorForKnownTools()
    {
        var registry = CreateRegistry();

        registry.GetExecutor(FileToolDefinitions.ReadFile).Should().NotBeNull();
        registry.GetExecutor(ContextFileToolDefinitions.ReadContextFile).Should().NotBeNull();
        registry.GetExecutor("missing_tool").Should().BeNull();
    }

    private static ToolRegistry CreateRegistry()
    {
        var root = Path.Combine(Path.GetTempPath(), "aria-registry-tests", Guid.NewGuid().ToString("N"));
        var options = Options.Create(new AriaOptions
        {
            Workspace =
            {
                RootPath = root,
                ContextDirectory = "context"
            }
        });
        var sandbox = new WorkspaceSandbox(options);

        return new ToolRegistry(
            new FileToolsExecutor(sandbox),
            new ContextFileToolsExecutor(sandbox, options));
    }
}
