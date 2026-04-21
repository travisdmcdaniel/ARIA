using ARIA.Core.Interfaces;
using ARIA.Core.Models;
using ARIA.Skills.BuiltIn.ContextFileTools;
using ARIA.Skills.BuiltIn.FileTools;

namespace ARIA.Skills.BuiltIn;

public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyList<ToolDefinition> _definitions;
    private readonly IReadOnlyDictionary<string, IToolExecutor> _executors;

    public ToolRegistry(
        FileToolsExecutor fileTools,
        ContextFileToolsExecutor contextFileTools)
    {
        _definitions =
        [
            ..FileToolDefinitions.All,
            ..ContextFileToolDefinitions.All
        ];

        var executors = new Dictionary<string, IToolExecutor>(StringComparer.OrdinalIgnoreCase);
        foreach (var toolName in fileTools.ToolNames)
            executors[toolName] = fileTools;

        foreach (var toolName in contextFileTools.ToolNames)
            executors[toolName] = contextFileTools;

        _executors = executors;
    }

    public IReadOnlyList<ToolDefinition> GetToolDefinitions() => _definitions;

    public IToolExecutor? GetExecutor(string toolName) =>
        _executors.GetValueOrDefault(toolName);
}
