using ARIA.Core.Interfaces;
using ARIA.Memory.Migrations;

namespace ARIA.Service;

/// <summary>
/// Primary background worker. Runs database migrations and LLM capability detection
/// on startup, then idles while other hosted services handle actual work.
/// </summary>
public sealed class AgentWorker(
    DatabaseMigrator migrator,
    ILlmAdapter llmAdapter,
    ILogger<AgentWorker> logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("ARIA Agent Worker starting");

        try
        {
            await migrator.MigrateAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogCritical(ex, "Database migration failed — service cannot start");
            throw;
        }

        try
        {
            var caps = await llmAdapter.DetectCapabilitiesAsync(cancellationToken);
            logger.LogInformation(
                "LLM capabilities detected — Vision: {Vision}, Tools: {Tools}, Streaming: {Streaming}",
                caps.SupportsVision, caps.SupportsToolCalling, caps.SupportsStreaming);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not detect LLM capabilities — Ollama may be offline");
        }

        logger.LogInformation("ARIA Agent Worker ready");

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
