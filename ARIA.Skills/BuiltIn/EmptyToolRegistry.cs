using ARIA.Core.Interfaces;
using ARIA.Core.Models;

namespace ARIA.Skills.BuiltIn;

/// <summary>No-op tool registry used until M5 wires the real file and context tools.</summary>
public sealed class EmptyToolRegistry : IToolRegistry
{
    public IReadOnlyList<ToolDefinition> GetToolDefinitions() => [];
    public IToolExecutor? GetExecutor(string toolName) => null;
}
