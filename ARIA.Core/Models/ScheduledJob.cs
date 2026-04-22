namespace ARIA.Core.Models;

public sealed record ScheduledJob(
    string JobId,
    string FileName,
    string FilePath,
    long TelegramUserId,
    string Name,
    string CronExpression,
    string TimeZoneId,
    string PayloadKind,
    string Prompt,
    string SessionTarget,
    bool Enabled,
    bool DisabledByFileName,
    bool IsValid,
    string? ValidationError,
    DateTime LoadedAt,
    DateTime? LastFiredAt,
    DateTime? NextFireAt)
{
    public bool IsActive => IsValid && Enabled && !DisabledByFileName;
}

public sealed record JobExecutionLog(
    long LogId,
    string JobId,
    DateTime StartedAt,
    DateTime? CompletedAt,
    bool Success,
    string? Output,
    string? ErrorMessage);
