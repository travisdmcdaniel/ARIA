namespace ARIA.Core.Models;

public sealed record ScheduledJob(
    string JobId,
    long TelegramUserId,
    string Name,
    string CronExpression,
    string Prompt,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastFiredAt,
    DateTime? NextFireAt);

public sealed record JobExecutionLog(
    long LogId,
    string JobId,
    DateTime StartedAt,
    DateTime? CompletedAt,
    bool Success,
    string? Output,
    string? ErrorMessage);
