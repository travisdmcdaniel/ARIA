using ARIA.Memory.Migrations;

namespace ARIA.Service;

/// <summary>
/// Primary background worker. Runs database migrations on startup, then idles
/// while other hosted services (TelegramWorker, etc.) handle actual work.
/// </summary>
public sealed class AgentWorker(
    DatabaseMigrator migrator,
    ILogger<AgentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ARIA Agent Worker starting");

        try
        {
            await migrator.MigrateAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogCritical(ex, "Database migration failed — service cannot start");
            throw;
        }

        logger.LogInformation("ARIA Agent Worker ready");

        // Idle; other hosted services drive the actual work
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
