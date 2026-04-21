using ARIA.Core.Constants;
using ARIA.Core.Interfaces;
using ARIA.Core.Options;
using ARIA.Telegram.Handlers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Agent.Heartbeat;

public sealed class HeartbeatWorker : BackgroundService
{
    public const string NoTelegramUpdateToken = "ARIA_HEARTBEAT_NO_UPDATE";

    private readonly IAgentTurnHandler _agent;
    private readonly IMessageRouter _router;
    private readonly ICredentialStore _credentials;
    private readonly AriaOptions _options;
    private readonly ILogger<HeartbeatWorker> _logger;

    public HeartbeatWorker(
        IAgentTurnHandler agent,
        IMessageRouter router,
        ICredentialStore credentials,
        IOptions<AriaOptions> options,
        ILogger<HeartbeatWorker> logger)
    {
        _agent = agent;
        _router = router;
        _credentials = credentials;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Heartbeat.Enabled)
        {
            _logger.LogDebug("Heartbeat worker disabled by configuration");
            return;
        }

        var interval = GetInterval();
        using var timer = new PeriodicTimer(interval);

        _logger.LogInformation("Heartbeat worker started with interval {Interval}", interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RunHeartbeatAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Heartbeat tick failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunHeartbeatAsync(CancellationToken ct)
    {
        if (PauseFlag.IsSet)
        {
            _logger.LogDebug("Agent paused; skipping heartbeat tick");
            return;
        }

        var heartbeatPath = Path.Combine(
            _options.Workspace.GetResolvedContextDirectory(),
            "HEARTBEAT.md");

        if (!File.Exists(heartbeatPath))
        {
            _logger.LogDebug("Heartbeat file {HeartbeatPath} does not exist; skipping tick", heartbeatPath);
            return;
        }

        var heartbeatContent = await File.ReadAllTextAsync(heartbeatPath, ct);
        if (string.IsNullOrWhiteSpace(heartbeatContent))
        {
            _logger.LogDebug("Heartbeat file {HeartbeatPath} is empty; skipping tick", heartbeatPath);
            return;
        }

        var authorizedUserIds = _options.Telegram.AuthorizedUserIds;
        if (authorizedUserIds.Length == 0)
        {
            _logger.LogWarning(
                "Heartbeat is enabled but no telegram:authorizedUserIds are configured; skipping tick");
            return;
        }

        var token = ResolveTelegramToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning(
                "Heartbeat is enabled but Telegram bot token is not configured. " +
                "Set it in the credential store with key '{Key}' or directly in config.",
                CredentialKeys.TelegramBotToken);
            return;
        }

        var bot = new TelegramBotClient(token);

        foreach (var userId in authorizedUserIds)
        {
            try
            {
                var response = await _agent.RunTurnAsync(userId, BuildSyntheticPrompt(heartbeatContent), null, ct);
                if (!ShouldSendTelegramUpdate(response))
                {
                    _logger.LogDebug("Heartbeat produced no Telegram update for user {UserId}", userId);
                    continue;
                }

                await _router.SendAgentResponseAsync(bot, new ChatId(userId), response, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Heartbeat failed for user {UserId}", userId);
            }
        }
    }

    public static bool ShouldSendTelegramUpdate(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return false;

        return !string.Equals(
            response.Trim(),
            NoTelegramUpdateToken,
            StringComparison.OrdinalIgnoreCase);
    }

    private string BuildSyntheticPrompt(string heartbeatContent) =>
        $"""
        This is ARIA's scheduled heartbeat cycle.

        Read and follow the HEARTBEAT.md instructions below. Perform any needed reasoning or tool use.
        Only send the user a Telegram update when there is something useful, actionable, or time-sensitive for them to know now.
        If no user-facing Telegram update is warranted, respond with exactly:
        {NoTelegramUpdateToken}

        HEARTBEAT.md:
        {heartbeatContent}
        """;

    private TimeSpan GetInterval()
    {
        if (_options.Heartbeat.IntervalMinutes > 0)
            return TimeSpan.FromMinutes(_options.Heartbeat.IntervalMinutes);

        _logger.LogWarning(
            "Invalid heartbeat interval {IntervalMinutes}; using 30 minutes",
            _options.Heartbeat.IntervalMinutes);
        return TimeSpan.FromMinutes(30);
    }

    private string? ResolveTelegramToken()
    {
        var stored = _credentials.Load(CredentialKeys.TelegramBotToken);
        if (!string.IsNullOrWhiteSpace(stored))
            return stored;

        var configured = _options.Telegram.BotToken;
        return configured == "USE_CREDENTIAL_STORE" ? null : configured;
    }
}
