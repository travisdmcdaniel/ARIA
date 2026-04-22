using System.Collections.Concurrent;
using System.Net.Http.Json;
using ARIA.Core.Constants;
using ARIA.Core.Interfaces;
using ARIA.Core.Models;
using ARIA.Core.Options;
using ARIA.Scheduler.Store;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCrontab;

namespace ARIA.Scheduler;

public sealed class SchedulerService(
    FileSystemJobStore fileStore,
    SqliteJobStore sqliteStore,
    IAgentTurnHandler agent,
    IConversationStore conversations,
    ICredentialStore credentials,
    IOptions<AriaOptions> options,
    ILogger<SchedulerService> logger) : BackgroundService, ISchedulerService
{
    private readonly AriaOptions _options = options.Value;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly Queue<string> _pendingJobIds = new();
    private readonly HashSet<string> _queuedJobIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ScheduledJob> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _reloadDebounceCts;
    private DateTime _lastPeriodicReloadUtc = DateTime.MinValue;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Scheduler.Enabled)
        {
            logger.LogInformation("Scheduler disabled by configuration");
            return;
        }

        await LoadJobFilesAsync(cancellationToken);
        StartWatcher();
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Scheduler.Enabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow - _lastPeriodicReloadUtc >= TimeSpan.FromMinutes(1))
                {
                    await ReloadJobsAsync(stoppingToken);
                    _lastPeriodicReloadUtc = DateTime.UtcNow;
                }

                await EnqueueDueJobsAsync(DateTime.UtcNow, stoppingToken);
                await DrainQueueAsync(stoppingToken);
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduler loop error");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        _reloadDebounceCts?.Dispose();
        _stateLock.Dispose();
        base.Dispose();
    }

    public async Task ScheduleJobAsync(ScheduledJob job, CancellationToken ct = default)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            _jobs[job.JobId] = WithNextFire(job, DateTime.UtcNow);
            await sqliteStore.ReplaceMirrorAsync(_jobs.Values.ToList(), ct);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task DisableJobAsync(string jobIdOrFileName, CancellationToken ct = default)
    {
        ScheduledJob? job;
        await _stateLock.WaitAsync(ct);
        try
        {
            job = FindJob(jobIdOrFileName);
            if (job is null)
                return;
        }
        finally
        {
            _stateLock.Release();
        }

        await DisableJobFileAsync(job, ct);
        await ReloadJobsAsync(ct);
    }

    public Task CancelJobAsync(string jobIdOrFileName, CancellationToken ct = default) =>
        DisableJobAsync(jobIdOrFileName, ct);

    public async Task LoadJobFilesAsync(CancellationToken ct = default)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            var previous = await sqliteStore.LoadMirrorAsync(ct);
            foreach (var current in _jobs.Values)
                previous[current.JobId] = current;

            var loaded = await fileStore.LoadAsync(previous, ct);
            var missedJobIds = new List<string>();
            _jobs.Clear();

            foreach (var job in loaded)
            {
                if (_options.Scheduler.RunMissedJobsAsap &&
                    job.IsActive &&
                    previous.TryGetValue(job.JobId, out var previousJob) &&
                    previousJob.NextFireAt is { } missedFire &&
                    missedFire <= DateTime.UtcNow)
                {
                    missedJobIds.Add(job.JobId);
                }

                var withNext = job.IsActive
                    ? WithNextFire(job, DateTime.UtcNow)
                    : job with { NextFireAt = null };

                _jobs[withNext.JobId] = withNext;
            }

            foreach (var jobId in missedJobIds)
                EnqueueLocked(jobId);

            await sqliteStore.ReplaceMirrorAsync(_jobs.Values.ToList(), ct);
            _lastPeriodicReloadUtc = DateTime.UtcNow;
            logger.LogInformation(
                "Loaded {ActiveCount} active scheduled job(s), {InvalidCount} invalid job file(s)",
                _jobs.Values.Count(j => j.IsActive),
                _jobs.Values.Count(j => !j.IsValid));
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public Task ReloadJobsAsync(CancellationToken ct = default) => LoadJobFilesAsync(ct);

    public Task LoadPersistedJobsAsync(CancellationToken ct = default) => LoadJobFilesAsync(ct);

    public Task<IReadOnlyList<ScheduledJob>> GetJobsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ScheduledJob> jobs = _jobs.Values
            .OrderBy(j => j.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult(jobs);
    }

    public Task<IReadOnlyList<ScheduledJob>> GetActiveJobsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ScheduledJob> jobs = _jobs.Values
            .Where(j => j.IsActive)
            .OrderBy(j => j.NextFireAt)
            .ThenBy(j => j.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult(jobs);
    }

    private async Task EnqueueDueJobsAsync(DateTime nowUtc, CancellationToken ct)
    {
        var due = new List<ScheduledJob>();

        await _stateLock.WaitAsync(ct);
        try
        {
            foreach (var job in _jobs.Values.Where(j => j.IsActive))
            {
                if (job.NextFireAt is null || job.NextFireAt > nowUtc)
                    continue;

                due.Add(job);
            }

            foreach (var job in due)
            {
                if (PauseFlag.IsSet && !_options.Scheduler.RunMissedJobsAsap)
                {
                    _jobs[job.JobId] = WithNextFire(job, nowUtc);
                    continue;
                }

                EnqueueLocked(job.JobId);
            }

            if (due.Count > 0)
                await sqliteStore.ReplaceMirrorAsync(_jobs.Values.ToList(), ct);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task DrainQueueAsync(CancellationToken ct)
    {
        while (true)
        {
            ScheduledJob? job;

            await _stateLock.WaitAsync(ct);
            try
            {
                if (_pendingJobIds.Count == 0)
                    return;

                var jobId = _pendingJobIds.Dequeue();
                _queuedJobIds.Remove(jobId);
                _jobs.TryGetValue(jobId, out job);
            }
            finally
            {
                _stateLock.Release();
            }

            if (job is null || !job.IsActive)
                continue;

            if (PauseFlag.IsSet)
            {
                if (_options.Scheduler.RunMissedJobsAsap)
                {
                    await _stateLock.WaitAsync(ct);
                    try
                    {
                        EnqueueLocked(job.JobId);
                    }
                    finally
                    {
                        _stateLock.Release();
                    }
                }

                return;
            }

            await RunJobAsync(job, ct);
        }
    }

    private async Task RunJobAsync(ScheduledJob job, CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        logger.LogInformation("Running scheduled job {JobId} ({Name})", job.JobId, job.Name);

        try
        {
            var response = await RunAgentTurnAsync(job, ct);
            var completedAt = DateTime.UtcNow;

            await sqliteStore.AppendExecutionLogAsync(
                job.JobId,
                startedAt,
                completedAt,
                success: true,
                output: response,
                errorMessage: null,
                ct);

            await MarkJobFiredAsync(job.JobId, completedAt, ct);
            await SendTelegramAsync(job, response, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var completedAt = DateTime.UtcNow;
            logger.LogError(ex, "Scheduled job {JobId} failed", job.JobId);

            await sqliteStore.AppendExecutionLogAsync(
                job.JobId,
                startedAt,
                completedAt,
                success: false,
                output: null,
                errorMessage: ex.Message,
                ct);

            await SendTelegramAsync(job, $"Scheduled job \"{job.Name}\" failed: {ex.Message}", ct);
        }
    }

    private async Task MarkJobFiredAsync(string jobId, DateTime firedAt, CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            if (!_jobs.TryGetValue(jobId, out var job))
                return;

            var updated = WithNextFire(job with { LastFiredAt = firedAt }, firedAt);
            _jobs[jobId] = updated;
            await sqliteStore.MarkFiredAsync(jobId, firedAt, updated.NextFireAt, ct);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task SendTelegramAsync(ScheduledJob job, string text, CancellationToken ct)
    {
        if (job.TelegramUserId == 0)
        {
            logger.LogWarning("Scheduled job {JobId} has no Telegram target; response was not sent", job.JobId);
            return;
        }

        var token = ResolveTelegramToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Telegram bot token is unavailable; scheduled job {JobId} response was not sent", job.JobId);
            return;
        }

        using var http = new HttpClient();
        var response = await http.PostAsJsonAsync(
            $"https://api.telegram.org/bot{token}/sendMessage",
            new
            {
                chat_id = job.TelegramUserId,
                text = $"Scheduled job: {job.Name}\n\n{text}"
            },
            ct);

        if (!response.IsSuccessStatusCode)
            logger.LogWarning("Telegram sendMessage failed for scheduled job {JobId}: {StatusCode}", job.JobId, response.StatusCode);
    }

    private async Task<string> RunAgentTurnAsync(ScheduledJob job, CancellationToken ct)
    {
        if (!string.Equals(job.SessionTarget, "isolated", StringComparison.OrdinalIgnoreCase))
            return await agent.RunTurnAsync(job.TelegramUserId, job.Prompt, null, ct);

        var previousSession = await conversations.GetOrCreateActiveSessionAsync(job.TelegramUserId, ct);
        await conversations.ArchiveSessionAsync(previousSession.SessionId, ct);

        try
        {
            var response = await agent.RunTurnAsync(job.TelegramUserId, job.Prompt, null, ct);
            var isolatedSession = await conversations.GetOrCreateActiveSessionAsync(job.TelegramUserId, ct);
            await conversations.ArchiveSessionAsync(isolatedSession.SessionId, ct);
            return response;
        }
        finally
        {
            await conversations.ResumeSessionAsync(job.TelegramUserId, previousSession.SessionId, ct);
        }
    }

    private string? ResolveTelegramToken()
    {
        var stored = credentials.Load(CredentialKeys.TelegramBotToken);
        if (!string.IsNullOrWhiteSpace(stored))
            return stored;

        var configured = _options.Telegram.BotToken;
        return configured == "USE_CREDENTIAL_STORE" ? null : configured;
    }

    private ScheduledJob? FindJob(string jobIdOrFileName) =>
        _jobs.Values.FirstOrDefault(j =>
            string.Equals(j.JobId, jobIdOrFileName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(j.FileName, jobIdOrFileName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileNameWithoutExtension(j.FileName), jobIdOrFileName, StringComparison.OrdinalIgnoreCase));

    private static ScheduledJob WithNextFire(ScheduledJob job, DateTime fromUtc)
    {
        if (!job.IsActive)
            return job with { NextFireAt = null };

        var schedule = CrontabSchedule.Parse(job.CronExpression);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(job.TimeZoneId);
        var localFrom = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, timeZone);
        var nextLocal = schedule.GetNextOccurrence(localFrom);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified), timeZone);
        return job with { NextFireAt = nextUtc };
    }

    private void StartWatcher()
    {
        Directory.CreateDirectory(fileStore.JobsDirectory);
        _watcher = new FileSystemWatcher(fileStore.JobsDirectory, "*.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Created += (_, _) => DebouncedReload();
        _watcher.Changed += (_, _) => DebouncedReload();
        _watcher.Renamed += (_, _) => DebouncedReload();
        _watcher.Deleted += (_, _) => DebouncedReload();
    }

    private void DebouncedReload()
    {
        var current = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _reloadDebounceCts, current);
        previous?.Cancel();
        previous?.Dispose();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), current.Token);
                await ReloadJobsAsync(current.Token);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to reload scheduled jobs after file change");
            }
        });
    }

    private async Task DisableJobFileAsync(ScheduledJob job, CancellationToken ct)
    {
        if (job.IsValid)
        {
            var json = await File.ReadAllTextAsync(job.FilePath, ct);
            var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json)
                      ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            map["enabled"] = false;
            var output = System.Text.Json.JsonSerializer.Serialize(
                map,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(job.FilePath, output, ct);
        }
        else
        {
            var disabledPath = Path.Combine(
                Path.GetDirectoryName(job.FilePath)!,
                $"_{Path.GetFileName(job.FilePath)}");

            if (!File.Exists(disabledPath))
                File.Move(job.FilePath, disabledPath);
        }
    }

    private void EnqueueLocked(string jobId)
    {
        if (!_queuedJobIds.Add(jobId))
            return;

        _pendingJobIds.Enqueue(jobId);
    }
}
