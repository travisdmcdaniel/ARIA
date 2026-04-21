namespace ARIA.Core.Models;

public sealed record ConversationTurn(
    long TurnId,
    string SessionId,
    long TelegramUserId,
    DateTime Timestamp,
    string Role,
    string? TextContent,
    string? ToolCallsJson,
    string? ToolResultJson,
    string? ImageDataJson);

public static class ConversationRole
{
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";
    public const string System = "system";
}
