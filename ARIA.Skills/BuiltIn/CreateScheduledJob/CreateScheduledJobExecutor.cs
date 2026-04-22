using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ARIA.Core.Interfaces;
using ARIA.Core.Models;
using ARIA.Core.Options;
using Microsoft.Extensions.Options;

namespace ARIA.Skills.BuiltIn.CreateScheduledJob;

public sealed class CreateScheduledJobExecutor(
    IOptions<AriaOptions> options) : IToolExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public IReadOnlyList<string> ToolNames { get; } =
    [
        CreateScheduledJobDefinitions.CreateScheduledJob
    ];

    public async Task<ToolInvocationResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken ct = default)
    {
        try
        {
            var args = Parse(invocation.ArgumentsJson);
            var fileName = $"{ToKebabCase(args.Name)}.json";
            var jobsDirectory = options.Value.Scheduler.GetResolvedDirectory(
                options.Value.Workspace.GetResolvedRootPath());
            var fullPath = Path.Combine(jobsDirectory, fileName);

            if (File.Exists(fullPath) && !args.Overwrite)
                return Result(invocation, Error($"Job file already exists: {fileName}. Set overwrite to true to replace it."), isError: true);

            Directory.CreateDirectory(jobsDirectory);

            var jobFile = new JobFile(
                args.Name,
                new ScheduleFile("cron", args.CronExpression, args.TimeZoneId),
                new PayloadFile("agentTurn", args.Message),
                args.SessionTarget,
                args.Enabled);

            await File.WriteAllTextAsync(
                fullPath,
                JsonSerializer.Serialize(jobFile, JsonOptions),
                ct);

            var relativePath = Path.GetRelativePath(
                    options.Value.Workspace.GetResolvedRootPath(),
                    fullPath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');

            return Result(invocation, new
            {
                created = true,
                path = relativePath,
                file_name = fileName,
                reload = "scheduled",
                job = jobFile
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result(invocation, Error(ex.Message), isError: true);
        }
    }

    private static NormalizedCreateScheduledJobArgs Parse(string argumentsJson)
    {
        var args = JsonSerializer.Deserialize<CreateScheduledJobArgs>(argumentsJson, JsonOptions)
                   ?? throw new ArgumentException("Invalid tool arguments.");

        var name = FirstNonEmpty(args.Name, args.JobName)
                   ?? throw new ArgumentException("name is required.");

        var cron = FirstNonEmpty(
            args.Schedule?.Expr,
            args.Schedule?.Expression,
            args.CronExpression,
            args.Cron)
                   ?? throw new ArgumentException("schedule.expr is required.");

        if (cron.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 5)
            throw new ArgumentException("schedule.expr must be a five-field cron expression.");

        var timeZone = FirstNonEmpty(
            args.Schedule?.Tz,
            args.Schedule?.Timezone,
            args.Tz,
            args.Timezone,
            args.TimeZoneId)
                       ?? throw new ArgumentException("schedule.tz is required and must be an IANA time zone.");

        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZone);

        var scheduleKind = FirstNonEmpty(args.Schedule?.Kind, "cron")!;
        if (!string.Equals(scheduleKind, "cron", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("schedule.kind must be cron.");

        var payloadKind = FirstNonEmpty(args.Payload?.Kind, "agentTurn")!;
        if (!string.Equals(payloadKind, "agentTurn", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("payload.kind must be agentTurn.");

        var message = FirstNonEmpty(args.Payload?.Message, args.Message, args.PayloadMessage)
                      ?? throw new ArgumentException("payload.message is required.");

        var sessionTarget = FirstNonEmpty(args.SessionTarget, args.Session_Target, "isolated")!;
        if (!string.Equals(sessionTarget, "isolated", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sessionTarget, "main", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("sessionTarget must be isolated or main.");
        }

        return new NormalizedCreateScheduledJobArgs(
            name,
            cron,
            timeZone,
            message,
            sessionTarget,
            args.Enabled ?? true,
            args.Overwrite);
    }

    private static string ToKebabCase(string value)
    {
        var sb = new StringBuilder(value.Length);
        var previousWasSeparator = false;

        foreach (var c in value.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (char.IsUpper(c) && sb.Length > 0 && !previousWasSeparator)
                    sb.Append('-');

                sb.Append(char.ToLowerInvariant(c));
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && sb.Length > 0)
            {
                sb.Append('-');
                previousWasSeparator = true;
            }
        }

        var result = sb.ToString().Trim('-');
        if (string.IsNullOrWhiteSpace(result))
            throw new ArgumentException("name must contain at least one letter or digit.");

        return result;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static ToolInvocationResult Result(ToolInvocation invocation, object value, bool isError = false) =>
        new(
            invocation.ToolCallId,
            invocation.ToolName,
            JsonSerializer.Serialize(value, JsonOptions),
            isError);

    private static object Error(string message) => new { error = message };

    private sealed class CreateScheduledJobArgs
    {
        public string? Name { get; set; }

        [JsonPropertyName("job_name")]
        public string? JobName { get; set; }

        public ScheduleArgs? Schedule { get; set; }
        public PayloadArgs? Payload { get; set; }
        public string? Message { get; set; }

        [JsonPropertyName("payload_message")]
        public string? PayloadMessage { get; set; }

        [JsonPropertyName("cron_expression")]
        public string? CronExpression { get; set; }

        public string? Cron { get; set; }
        public string? Tz { get; set; }
        public string? Timezone { get; set; }

        [JsonPropertyName("time_zone_id")]
        public string? TimeZoneId { get; set; }

        public string? SessionTarget { get; set; }

        [JsonPropertyName("session_target")]
        public string? Session_Target { get; set; }

        public bool? Enabled { get; set; }
        public bool Overwrite { get; set; }
    }

    private sealed record NormalizedCreateScheduledJobArgs(
        string Name,
        string CronExpression,
        string TimeZoneId,
        string Message,
        string SessionTarget,
        bool Enabled,
        bool Overwrite);

    private sealed class ScheduleArgs
    {
        public string? Kind { get; set; }
        public string? Expr { get; set; }
        public string? Expression { get; set; }
        public string? Tz { get; set; }
        public string? Timezone { get; set; }
    }

    private sealed class PayloadArgs
    {
        public string? Kind { get; set; }
        public string? Message { get; set; }
    }

    private sealed record JobFile(
        string Name,
        ScheduleFile Schedule,
        PayloadFile Payload,
        string SessionTarget,
        bool Enabled);

    private sealed record ScheduleFile(string Kind, string Expr, string Tz);

    private sealed record PayloadFile(string Kind, string Message);
}
