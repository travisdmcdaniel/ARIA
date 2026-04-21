using ARIA.Core.Models;

namespace ARIA.Core.Interfaces;

public interface IToolExecutor
{
    Task<ToolInvocationResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct = default);
}
