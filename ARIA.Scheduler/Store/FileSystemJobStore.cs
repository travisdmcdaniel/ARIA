using System.Text.Json;
using ARIA.Core.Models;
using ARIA.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCrontab;

namespace ARIA.Scheduler.Store;

public sealed class FileSystemJobStore(
    IOptions<AriaOptions> options,
    ILogger<FileSystemJobStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly AriaOptions _options = options.Value;

    public string JobsDirectory =>
        _options.Scheduler.GetResolvedDirectory(_options.Workspace.GetResolvedRootPath());

    public async Task<IReadOnlyList<ScheduledJob>> LoadAsync(
        IReadOnlyDictionary<string, ScheduledJob> previousJobs,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(JobsDirectory);

        var jobs = new List<ScheduledJob>();
        foreach (var file in Directory.EnumerateFiles(JobsDirectory, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            jobs.Add(await LoadFileAsync(file, previousJobs, ct));
        }

        return jobs
            .OrderBy(j => j.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<ScheduledJob> LoadFileAsync(
        string filePath,
        IReadOnlyDictionary<string, ScheduledJob> previousJobs,
        CancellationToken ct)
    {
        var fileName = Path.GetFileName(filePath);
        var jobId = Path.GetFileNameWithoutExtension(filePath);
        var disabledByName = IsDisabledByFileName(fileName);
        var telegramUserId = _options.Telegram.AuthorizedUserIds.FirstOrDefault();
        previousJobs.TryGetValue(jobId, out var previous);

        try
        {
            await using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var definition = await JsonSerializer.DeserializeAsync<JobFileDefinition>(stream, JsonOptions, ct);

            if (definition is null)
                return Invalid(filePath, fileName, jobId, disabledByName, telegramUserId, "Job file is empty.", previous);

            var name = string.IsNullOrWhiteSpace(definition.Name)
                ? jobId
                : definition.Name.Trim();

            var schedule = definition.Schedule;
            if (schedule is null)
                return Invalid(filePath, fileName, jobId, disabledByName, telegramUserId, "Missing schedule object.", previous, name);

            if (!string.Equals(schedule.Kind, "cron", StringComparison.OrdinalIgnoreCase))
                return Invalid(filePath, fileName, jobId, disabledByName, telegramUserId, "schedule.kind must be \"cron\".", previous, name);

            if (string.IsNullOrWhiteSpace(schedule.Expr))
                return Invalid(filePath, fileName, jobId, disabledByName, telegramUserId, "schedule.expr is required.", previous, name);

            CrontabSchedule.Parse(schedule.Expr);

            var timeZoneId = string.IsNullOrWhiteSpace(schedule.Tz)
                ? "UTC"
                : schedule.Tz.Trim();
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

            var payload = definition.Payload;
            if (payload is null)
                return Invalid(filePath, fileName, jobId, disabledByName, telegramUserId, "Missing payload object.", previous, name);

            var payloadKind = string.IsNullOrWhiteSpace(payload.Kind)
                ? "agentTurn"
                : payload.Kind.Trim();
            if (!string.Equals(payloadKind, "agentTurn", StringComparison.OrdinalIgnoreCase))
                return Invalid(filePath, fileName, jobId, disabledByName, telegramUserId, "Only payload.kind \"agentTurn\" is supported in M7.", previous, name);

            if (string.IsNullOrWhiteSpace(payload.Message))
                return Invalid(filePath, fileName, jobId, disabledByName, telegramUserId, "payload.message is required.", previous, name);

            var sessionTarget = string.IsNullOrWhiteSpace(definition.SessionTarget)
                ? "isolated"
                : definition.SessionTarget.Trim();

            if (!string.Equals(sessionTarget, "isolated", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(sessionTarget, "main", StringComparison.OrdinalIgnoreCase))
            {
                return Invalid(filePath, fileName, jobId, disabledByName, telegramUserId, "sessionTarget must be \"isolated\" or \"main\".", previous, name);
            }

            return new ScheduledJob(
                JobId: jobId,
                FileName: fileName,
                FilePath: filePath,
                TelegramUserId: telegramUserId,
                Name: name,
                CronExpression: schedule.Expr.Trim(),
                TimeZoneId: timeZoneId,
                PayloadKind: payloadKind,
                Prompt: payload.Message,
                SessionTarget: sessionTarget,
                Enabled: definition.Enabled ?? true,
                DisabledByFileName: disabledByName,
                IsValid: true,
                ValidationError: null,
                LoadedAt: DateTime.UtcNow,
                LastFiredAt: previous?.LastFiredAt,
                NextFireAt: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Job file {FileName} is not currently valid", fileName);
            return Invalid(filePath, fileName, jobId, disabledByName, telegramUserId, ex.Message, previous);
        }
    }

    private static ScheduledJob Invalid(
        string filePath,
        string fileName,
        string jobId,
        bool disabledByName,
        long telegramUserId,
        string error,
        ScheduledJob? previous,
        string? name = null) =>
        new(
            JobId: jobId,
            FileName: fileName,
            FilePath: filePath,
            TelegramUserId: telegramUserId,
            Name: string.IsNullOrWhiteSpace(name) ? jobId : name,
            CronExpression: string.Empty,
            TimeZoneId: "UTC",
            PayloadKind: "agentTurn",
            Prompt: string.Empty,
            SessionTarget: "isolated",
            Enabled: false,
            DisabledByFileName: disabledByName,
            IsValid: false,
            ValidationError: error,
            LoadedAt: DateTime.UtcNow,
            LastFiredAt: previous?.LastFiredAt,
            NextFireAt: null);

    private static bool IsDisabledByFileName(string fileName) =>
        fileName.StartsWith('_') ||
        fileName.StartsWith("disabled", StringComparison.OrdinalIgnoreCase);

    private sealed class JobFileDefinition
    {
        public string? Name { get; set; }
        public ScheduleDefinition? Schedule { get; set; }
        public PayloadDefinition? Payload { get; set; }
        public string? SessionTarget { get; set; }
        public bool? Enabled { get; set; }
    }

    private sealed class ScheduleDefinition
    {
        public string? Kind { get; set; }
        public string? Expr { get; set; }
        public string? Tz { get; set; }
    }

    private sealed class PayloadDefinition
    {
        public string? Kind { get; set; }
        public string? Message { get; set; }
    }
}
