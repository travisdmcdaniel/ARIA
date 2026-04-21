using ARIA.Agent.Conversation;
using ARIA.Agent.Prompts;
using ARIA.Core.Interfaces;
using ARIA.Core.Options;
using ARIA.LlmAdapter.Ollama;
using ARIA.Memory.Context;
using ARIA.Memory.Migrations;
using ARIA.Memory.Sqlite;
using ARIA.Service;
using ARIA.Service.Security;
using ARIA.Skills.BuiltIn;
using ARIA.Skills.Loader;
using ARIA.Telegram.Commands;
using ARIA.Telegram.Handlers;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

// ── Bootstrap logger (writes before DI is ready) ─────────────────────────────
var appDataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ARIA");
Directory.CreateDirectory(appDataDir);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File(
        Path.Combine(appDataDir, "logs", "aria-bootstrap-.log"),
        rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();

try
{
    // ── Config path: %LOCALAPPDATA%\ARIA\config.json; fallback to local file ──
    var productionConfig = Path.Combine(appDataDir, "config.json");
    var devConfig = Path.Combine(AppContext.BaseDirectory, "config.json");
    var configPath = File.Exists(productionConfig) ? productionConfig : devConfig;

    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddWindowsService(options => options.ServiceName = "ARIAService");

    var devConfigPath = Path.Combine(
        Path.GetDirectoryName(configPath)!, "config.development.json");

    builder.Configuration
        .AddJsonFile(configPath, optional: true, reloadOnChange: false)
        .AddJsonFile(devConfigPath, optional: true, reloadOnChange: false);

    // Bind flat config sections to AriaOptions (config keys are root-level: "workspace", "ollama", etc.)
    builder.Services.Configure<AriaOptions>(builder.Configuration);

    // ── Serilog (uses configured log level) ───────────────────────────────────
    var rawLevel = builder.Configuration["logging:level"] ?? "Information";
    var logLevel = Enum.TryParse<LogEventLevel>(rawLevel, ignoreCase: true, out var parsedLevel)
        ? parsedLevel
        : LogEventLevel.Information;
    var logsDir = Path.Combine(appDataDir, "logs");

    builder.Services.AddSerilog((services, cfg) =>
        cfg
            .MinimumLevel.Is(logLevel)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "ARIA")
            .WriteTo.File(
                Path.Combine(logsDir, "aria-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

    // ── Core services ─────────────────────────────────────────────────────────
    builder.Services.AddSingleton<ICredentialStore, DpapiCredentialStore>();
    builder.Services.AddSingleton<IConversationStore, SqliteConversationStore>();
    builder.Services.AddSingleton<IContextFileStore, MarkdownContextFileStore>();

    builder.Services.AddSingleton<DatabaseMigrator>(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<AriaOptions>>().Value;
        var dbPath = opts.Workspace.GetResolvedDatabasePath();
        var logger = sp.GetRequiredService<ILogger<DatabaseMigrator>>();
        return new DatabaseMigrator(dbPath, logger);
    });

    // ── LLM adapter ───────────────────────────────────────────────────────────
    builder.Services.AddSingleton<ILlmAdapter>(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<AriaOptions>>().Value.Ollama;
        return new OllamaAdapter(opts);
    });

    // ── Skills / tool registry (no-op stubs until M5 and M6) ─────────────────
    builder.Services.AddSingleton<IToolRegistry, EmptyToolRegistry>();
    builder.Services.AddSingleton<ISkillStore, EmptySkillStore>();

    // ── Agent ─────────────────────────────────────────────────────────────────
    builder.Services.AddSingleton<SystemPromptBuilder>();
    builder.Services.AddSingleton<ConversationLoop>();
    builder.Services.AddSingleton<IAgentTurnHandler>(sp => sp.GetRequiredService<ConversationLoop>());

    // ── Telegram commands ─────────────────────────────────────────────────────
    // General
    builder.Services.AddSingleton<IBotCommand, HelpCommand>();
    builder.Services.AddSingleton<IBotCommand, StatusCommand>();
    builder.Services.AddSingleton<IBotCommand, NewSessionCommand>();
    builder.Services.AddSingleton<IBotCommand, SessionsCommand>();
    builder.Services.AddSingleton<IBotCommand, ResumeCommand>();
    // Model management (M3)
    builder.Services.AddSingleton<IBotCommand, ModelsCommand>();
    builder.Services.AddSingleton<IBotCommand, ModelCommand>();
    // Skills (M6)
    builder.Services.AddSingleton<IBotCommand, ReloadSkillsCommand>();
    builder.Services.AddSingleton<IBotCommand, OnboardingCommand>();
    // Scheduler (M7)
    builder.Services.AddSingleton<IBotCommand, JobsCommand>();
    builder.Services.AddSingleton<IBotCommand, CancelJobCommand>();
    // Google OAuth (M8)
    builder.Services.AddSingleton<IBotCommand, GoogleSetupCommand>();
    builder.Services.AddSingleton<IBotCommand, GoogleConnectCommand>();
    builder.Services.AddSingleton<IBotCommand, GoogleCompleteCommand>();
    builder.Services.AddSingleton<IBotCommand, GoogleDisconnectCommand>();

    builder.Services.AddSingleton<CommandRegistry>();
    builder.Services.AddSingleton<IMessageRouter, MessageRouter>();
    builder.Services.AddHostedService<TelegramWorker>();

    // ── Worker ────────────────────────────────────────────────────────────────
    builder.Services.AddHostedService<AgentWorker>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "ARIA host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
