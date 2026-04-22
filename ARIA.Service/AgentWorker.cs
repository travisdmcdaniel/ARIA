using ARIA.Core.Interfaces;
using ARIA.Core.Options;
using ARIA.Memory.Migrations;
using ARIA.Skills.Loader;
using Microsoft.Extensions.Options;

namespace ARIA.Service;

/// <summary>
/// Primary background worker. Runs database migrations and LLM capability detection
/// on startup, then idles while other hosted services handle actual work.
/// </summary>
public sealed class AgentWorker(
    DatabaseMigrator migrator,
    ILlmAdapter llmAdapter,
    SkillSeeder skillSeeder,
    ISkillStore skillStore,
    IOptions<AriaOptions> options,
    ILogger<AgentWorker> logger) : BackgroundService
{
    private readonly AriaOptions _options = options.Value;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("ARIA Agent Worker starting");

        try
        {
            await migrator.MigrateAsync(cancellationToken);
            await skillSeeder.SeedAsync(cancellationToken);
            await skillStore.ReloadAsync(cancellationToken);
            LogOnboardingHintIfNeeded();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogCritical(ex, "Startup initialization failed — service cannot start");
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

    private void LogOnboardingHintIfNeeded()
    {
        var identityPath = Path.Combine(
            _options.Workspace.GetResolvedContextDirectory(),
            "IDENTITY.md");

        if (!File.Exists(identityPath))
            logger.LogInformation("IDENTITY.md is not present. Send /onboarding in Telegram to run first-time setup.");
    }
}
