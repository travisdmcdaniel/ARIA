using ARIA.Core.Models;

namespace ARIA.Core.Interfaces;

public interface IToolRegistry
{
    IReadOnlyList<ToolDefinition> GetToolDefinitions();
    IToolExecutor? GetExecutor(string toolName);
}
