using ARIA.Core.Constants;
using ARIA.Core.Interfaces;
using ARIA.Core.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ARIA.Telegram.Handlers;

/// <summary>
/// BackgroundService that maintains the Telegram bot long-poll connection.
/// Authorizes messages against the configured user ID whitelist and routes
/// approved messages to IMessageRouter.
/// </summary>
public sealed class TelegramWorker : BackgroundService
{
    private readonly IMessageRouter _router;
    private readonly ICredentialStore _credentials;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramWorker> _logger;

    public TelegramWorker(
        IMessageRouter router,
        ICredentialStore credentials,
        IOptions<AriaOptions> options,
        ILogger<TelegramWorker> logger)
    {
        _router = router;
        _credentials = credentials;
        _options = options.Value.Telegram;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = ResolveToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning(
                "Telegram bot token not configured. " +
                "Set it in the credential store with key '{Key}' or directly in config.",
                CredentialKeys.TelegramBotToken);
            return;
        }

        var bot = new TelegramBotClient(token);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message],
            DropPendingUpdates = false
        };

        _logger.LogInformation("Telegram worker starting long-poll loop");

        var delay = TimeSpan.FromSeconds(1);
        const int maxDelaySeconds = 60;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await bot.ReceiveAsync(
                    updateHandler: HandleUpdateAsync,
                    errorHandler: HandleErrorAsync,
                    receiverOptions: receiverOptions,
                    cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telegram receive loop crashed — retrying in {Delay}s", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelaySeconds));
            }
        }

        _logger.LogInformation("Telegram worker stopped");
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.Message is not { } message)
            return;

        var userId = message.From?.Id ?? 0;

        if (!IsAuthorized(userId))
        {
            _logger.LogDebug("Silently ignoring message from unauthorized user {UserId}", userId);
            return;
        }

        try
        {
            await _router.RouteAsync(bot, message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unhandled error routing message from user {UserId}", userId);
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        if (ex is ApiRequestException api)
            _logger.LogWarning("Telegram API error [{Code}]: {Message}", api.ErrorCode, api.Message);
        else
            _logger.LogError(ex, "Telegram polling error");

        return Task.CompletedTask;
    }

    private bool IsAuthorized(long userId) =>
        _options.AuthorizedUserIds.Length == 0 || _options.AuthorizedUserIds.Contains(userId);

    private string? ResolveToken()
    {
        // Prefer credential store; fall back to config value if it isn't the placeholder
        var stored = _credentials.Load(CredentialKeys.TelegramBotToken);
        if (!string.IsNullOrWhiteSpace(stored))
            return stored;

        var configured = _options.BotToken;
        return configured == "USE_CREDENTIAL_STORE" ? null : configured;
    }
}
