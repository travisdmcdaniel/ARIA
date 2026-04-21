using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ARIA.Telegram.Commands;

/// <summary>
/// Dispatches bot command messages (e.g. /new, /status) to the registered handlers.
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, IBotCommand> _commands;
    private readonly ILogger<CommandRegistry> _logger;

    public CommandRegistry(IEnumerable<IBotCommand> commands, ILogger<CommandRegistry> logger)
    {
        _commands = commands.ToDictionary(c => c.Command, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    /// <summary>
    /// Returns true and executes the command if the message is a known /command.
    /// Returns false if no matching command is registered.
    /// </summary>
    public async Task<bool> TryHandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var text = message.Text;
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith('/'))
            return false;

        // Strip @botname suffix (e.g. /new@MyBot → /new)
        var parts = text.Split(' ', 2);
        var commandToken = parts[0].Split('@')[0].ToLowerInvariant();

        if (!_commands.TryGetValue(commandToken, out var handler))
            return false;

        _logger.LogDebug("Dispatching command {Command} for user {UserId}",
            commandToken, message.From?.Id);

        await handler.HandleAsync(bot, message, ct);
        return true;
    }
}
