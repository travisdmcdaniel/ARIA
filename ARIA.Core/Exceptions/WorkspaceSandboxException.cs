namespace ARIA.Core.Exceptions;

public sealed class WorkspaceSandboxException : Exception
{
    public WorkspaceSandboxException(string message) : base(message) { }
    public WorkspaceSandboxException(string message, Exception inner) : base(message, inner) { }
}
