# ARIA — Implementation Plan
**Version 0.1 | April 2026**
**Stack: C# / .NET 8**

---

## 1. Repository & Solution Structure

Before writing any code, establish the solution layout. ARIA is a multi-project solution — getting the boundaries right up front avoids painful refactoring later.

### 1.1 Recommended Solution Layout

```
ARIA.sln
│
├── src/
│   ├── ARIA.Core/               # Domain models, interfaces, shared abstractions (no dependencies)
│   ├── ARIA.Agent/              # Agent Core: conversation loop, tool dispatch, context injection
│   ├── ARIA.LlmAdapter/         # LLM adapter abstraction + Ollama implementation
│   ├── ARIA.Skills/             # Skill & Tool Engine: manifest loader, executor, built-in skills
│   ├── ARIA.Memory/             # Conversation store, session management, context file I/O
│   ├── ARIA.Scheduler/          # Cron/interval scheduler, job persistence
│   ├── ARIA.Google/             # Google OAuth flow, token storage, Gmail/Calendar API wrappers
│   ├── ARIA.Telegram/           # Telegram bot connection, message handler, command router
│   ├── ARIA.Service/            # .NET Worker Service host — wires everything together
│   └── ARIA.TrayHost/           # WinForms tray icon + WPF settings window
│
├── installer/
│   └── ARIA.Installer/          # WiX Toolset v4 or Inno Setup project
│
└── tests/
    ├── ARIA.Core.Tests/
    ├── ARIA.Agent.Tests/
    ├── ARIA.Skills.Tests/
    ├── ARIA.Memory.Tests/
    └── ARIA.Scheduler.Tests/
```

### 1.2 Project Dependency Rules

The dependency graph must be a DAG — no circular references. The rule is: lower layers know nothing about higher layers.

```
ARIA.Core          (no dependencies on other ARIA projects)
    ↑
ARIA.Memory        (depends on Core)
ARIA.LlmAdapter    (depends on Core)
ARIA.Skills        (depends on Core, Memory)
ARIA.Scheduler     (depends on Core, Memory)
ARIA.Google        (depends on Core)
ARIA.Telegram      (depends on Core)
    ↑
ARIA.Agent         (depends on all of the above)
    ↑
ARIA.Service       (depends on Agent, TrayHost; composition root)
ARIA.TrayHost      (depends on Core only — no agent logic in the UI)
```

`ARIA.Core` contains only interfaces, domain models, and value objects. Nothing in it references an external NuGet package beyond the .NET BCL. This makes it trivially testable and swap-friendly.

### 1.3 NuGet Packages — Initial Setup

Add these to the relevant projects at solution creation time so you're not hunting for them later.

| Package | Project(s) | Purpose |
|---|---|---|
| `Telegram.Bot` (v21+) | `ARIA.Telegram` | Telegram Bot API client |
| `OllamaSharp` or raw `HttpClient` | `ARIA.LlmAdapter` | Ollama REST calls |
| `Google.Apis.Auth` | `ARIA.Google` | OAuth 2.0 flow |
| `Google.Apis.Gmail.v1` | `ARIA.Google` | Gmail API |
| `Google.Apis.Calendar.v3` | `ARIA.Google` | Calendar API |
| `Microsoft.Data.Sqlite` | `ARIA.Memory` | SQLite driver |
| `Dapper` | `ARIA.Memory` | Lightweight SQL mapping |
| `Quartz` (Quartz.NET) | `ARIA.Scheduler` | Cron job scheduling |
| `Serilog` + `Serilog.Sinks.File` | `ARIA.Service` | Rolling file logging |
| `Microsoft.Extensions.Configuration.Json` | `ARIA.Service` | JSON config provider |
| `Microsoft.Extensions.Hosting` | `ARIA.Service` | Generic Host / Worker Service |
| `CommunityToolkit.Mvvm` | `ARIA.TrayHost` | WPF MVVM bindings |
| `System.Text.Json` | `ARIA.Core`, `ARIA.Skills` | JSON serialization |
| `NCrontab` | `ARIA.Scheduler` | Cron expression parsing (if not using Quartz) |

---

## 2. Core Abstractions (ARIA.Core)

Define these interfaces and models before implementing anything else. Every other project depends on them, and getting the contracts right here avoids breaking changes across the board later.

### 2.1 LLM Adapter Interface

```csharp
// The capability flags reported by the active model
public record LlmCapabilities(
    bool SupportsToolCalling,
    bool SupportsVision,
    bool SupportsStreaming
);

// A single message in the conversation
public record ChatMessage(
    string Role,           // "system" | "user" | "assistant" | "tool"
    string? Content,       // null when there are tool calls
    string? Name,          // for tool result messages
    IReadOnlyList<ToolCall>? ToolCalls,
    IReadOnlyList<ImageAttachment>? Images
);

public record ImageAttachment(string MimeType, byte[] Data);

public record ToolCall(string Id, string FunctionName, string ArgumentsJson);

public record ToolResult(string ToolCallId, string ResultJson);

// The response from one LLM turn
public record LlmResponse(
    string? TextContent,
    IReadOnlyList<ToolCall>? ToolCalls,
    bool IsComplete
);

public interface ILlmAdapter
{
    LlmCapabilities Capabilities { get; }

    Task<LlmResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken ct);

    IAsyncEnumerable<LlmResponse> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken ct);

    Task<bool> CheckConnectivityAsync(CancellationToken ct);
}
```

### 2.2 Skill & Tool Interfaces

```csharp
public record ToolDefinition(
    string Name,
    string Description,
    JsonElement ParameterSchema   // OpenAI-compatible JSON Schema object
);

public record ToolInvocation(
    string ToolName,
    string ArgumentsJson,
    long TelegramUserId,
    string SessionId
);

public record ToolInvocationResult(
    bool Success,
    string ResultJson,
    string? ErrorMessage
);

public interface ISkillExecutor
{
    Task<ToolInvocationResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct);
}

public interface ISkillRegistry
{
    IReadOnlyList<ToolDefinition> GetAllToolDefinitions();
    ISkillExecutor? FindExecutor(string toolName);
    Task ReloadAsync(CancellationToken ct);
}
```

### 2.3 Agent & Session Models

```csharp
public record Session(
    string SessionId,
    long TelegramUserId,
    DateTime StartedAt,
    DateTime LastActivityAt,
    bool IsActive
);

public record ConversationTurn(
    long TurnId,
    string SessionId,
    long TelegramUserId,
    DateTime Timestamp,
    string Role,
    string? TextContent,
    string? ToolCallsJson,
    string? ToolResultJson,
    string? ImageDataJson
);

public interface IConversationStore
{
    Task<Session> GetOrCreateActiveSessionAsync(long userId, CancellationToken ct);
    Task ArchiveSessionAsync(string sessionId, CancellationToken ct);
    Task<Session?> GetSessionByIdAsync(string sessionId, CancellationToken ct);
    Task<IReadOnlyList<Session>> ListRecentSessionsAsync(long userId, int maxCount, CancellationToken ct);
    Task AppendTurnAsync(ConversationTurn turn, CancellationToken ct);
    Task<IReadOnlyList<ConversationTurn>> GetRecentTurnsAsync(string sessionId, int maxTurns, CancellationToken ct);
}
```

### 2.4 Context File Interface

```csharp
public enum ContextFile { Identity, Soul, User }

public interface IContextFileStore
{
    Task<string> ReadAsync(ContextFile file, CancellationToken ct);
    Task WriteAsync(ContextFile file, string content, CancellationToken ct);
    Task<DateTime> GetLastModifiedAsync(ContextFile file, CancellationToken ct);
}
```

### 2.5 Scheduled Job Model

```csharp
public record ScheduledJob(
    string JobId,
    long CreatedByUserId,
    string CronExpression,
    string TaskPrompt,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastFiredAt,
    DateTime? NextFireAt
);

public record JobExecutionLog(
    long LogId,
    string JobId,
    DateTime StartedAt,
    DateTime? CompletedAt,
    bool Success,
    string? ErrorMessage
);
```

---

## 3. Milestone Implementation Guide

Each milestone below describes what to build, how to structure it, key decisions to make, and the gotchas most likely to bite you.

---

### M1: Foundation — Windows Service + Tray Icon + Config + SQLite

**Goal:** A deployable skeleton with no real functionality. The point is to have the hosting infrastructure solid before adding any features on top of it.

#### 3.1.1 .NET Worker Service Host (ARIA.Service)

Use the Generic Host pattern. This gives you dependency injection, configuration, logging, and lifecycle management for free.

```csharp
// Program.cs
var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "ARIAService")
    .ConfigureAppConfiguration(config =>
    {
        config.AddJsonFile(AriaConfig.ConfigFilePath, optional: false, reloadOnChange: true);
    })
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<AriaOptions>(ctx.Configuration);
        services.AddSingleton<IConversationStore, SqliteConversationStore>();
        services.AddSingleton<IContextFileStore, MarkdownContextFileStore>();
        // ... register all other services
        services.AddHostedService<AgentWorker>();
    })
    .UseSerilog((ctx, logConfig) =>
    {
        logConfig.ReadFrom.Configuration(ctx.Configuration)
                 .WriteTo.File(
                     AriaConfig.LogFilePath,
                     rollingInterval: RollingInterval.Day,
                     retainedFileCountLimit: 30);
    })
    .Build();

await host.RunAsync();
```

`AriaOptions` is a strongly-typed POCO that mirrors `config.json`. Use `IOptions<AriaOptions>` throughout — never read `IConfiguration` directly in non-infrastructure code.

#### 3.1.2 Configuration Schema (ARIA.Core)

```csharp
public class AriaOptions
{
    public WorkspaceOptions Workspace { get; set; } = new();
    public OllamaOptions Ollama { get; set; } = new();
    public TelegramOptions Telegram { get; set; } = new();
    public GoogleOptions Google { get; set; } = new();
    public AgentOptions Agent { get; set; } = new();
    public SkillsOptions Skills { get; set; } = new();
    public SchedulerOptions Scheduler { get; set; } = new();
    public LoggingOptions Logging { get; set; } = new();
}
```

Sensitive values (bot token, client secret, OAuth tokens) are never in `config.json`. Implement a `ICredentialStore` interface backed by Windows DPAPI, and inject it wherever credentials are needed:

```csharp
public interface ICredentialStore
{
    void Save(string key, string value);
    string? Load(string key);
    void Delete(string key);
}

// Implementation using ProtectedData (DPAPI)
public class DpapiCredentialStore : ICredentialStore
{
    private readonly string _storePath; // encrypted blob file per key in %LOCALAPPDATA%\ARIA\creds\

    public void Save(string key, string value)
    {
        var plainBytes = Encoding.UTF8.GetBytes(value);
        var encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(GetKeyPath(key), encrypted);
    }
    // ...
}
```

Alternatively use `Windows.Security.Credentials.PasswordVault` (Credential Locker) — either is fine, DPAPI is simpler.

#### 3.1.3 SQLite Initialization (ARIA.Memory)

Run schema migrations at startup using a simple versioned migration runner rather than a full ORM. This keeps things lightweight and gives you full control.

```csharp
public class DatabaseMigrator
{
    private readonly string _dbPath;

    public async Task MigrateAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();

        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);

            CREATE TABLE IF NOT EXISTS sessions (
                session_id TEXT PRIMARY KEY,
                telegram_user_id INTEGER NOT NULL,
                started_at TEXT NOT NULL,
                last_activity_at TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS conversation_turns (
                turn_id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                telegram_user_id INTEGER NOT NULL,
                timestamp TEXT NOT NULL,
                role TEXT NOT NULL,
                text_content TEXT,
                tool_calls_json TEXT,
                tool_result_json TEXT,
                image_data_json TEXT,
                FOREIGN KEY (session_id) REFERENCES sessions(session_id)
            );

            CREATE TABLE IF NOT EXISTS scheduled_jobs (
                job_id TEXT PRIMARY KEY,
                created_by_user_id INTEGER NOT NULL,
                cron_expression TEXT NOT NULL,
                task_prompt TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                last_fired_at TEXT,
                next_fire_at TEXT
            );

            CREATE TABLE IF NOT EXISTS job_execution_log (
                log_id INTEGER PRIMARY KEY AUTOINCREMENT,
                job_id TEXT NOT NULL,
                started_at TEXT NOT NULL,
                completed_at TEXT,
                success INTEGER NOT NULL,
                error_message TEXT,
                FOREIGN KEY (job_id) REFERENCES scheduled_jobs(job_id)
            );
        ");
    }
}
```

Call `MigrateAsync()` early in the host startup, before any `IHostedService` starts.

#### 3.1.4 Tray Host (ARIA.TrayHost)

The tray host is a separate executable, not the service itself. It runs as a regular user-mode app in the system tray and communicates with the service over a named pipe or via the Windows Service Control Manager.

**The WinForms + WPF hybrid pattern:**

```csharp
// Program.cs in ARIA.TrayHost
[STAThread]
static void Main()
{
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);

    using var trayApp = new TrayApplication(); // WinForms Application subclass
    Application.Run(); // Runs the WinForms message loop (no visible form)
}

public class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private SettingsWindow? _settingsWindow; // WPF window, opened on demand

    public TrayApplication()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = LoadStatusIcon(AgentStatus.Running),
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow();
        _settingsWindow.Show(); // WPF window runs on the same STA thread
    }
}
```

The `SettingsWindow` is a standard WPF `Window`. It hosts a WPF application context internally but renders on the WinForms STA thread — this works fine in practice. For the WPF XAML, use `CommunityToolkit.Mvvm` with `[ObservableProperty]` and `[RelayCommand]` attributes to keep the ViewModel lean.

**Tray icon status:** Use three distinct `.ico` files (green, amber, red) embedded as resources. Swap the `NotifyIcon.Icon` property when the agent reports a status change.

**IPC between tray and service:** The simplest approach is polling the service status via `ServiceController` and a lightweight named pipe for richer status data (current model, Google connection state, etc.). Implement this in M11 when the settings window gets fleshed out — for M1, a static "Running" icon is sufficient.

#### M1 Acceptance Checklist
- [ ] `dotnet publish` produces an executable
- [ ] Service can be installed with `sc create` and starts cleanly
- [ ] `config.json` is read; invalid config logs a clear error and exits
- [ ] SQLite database is created at the configured path on first run
- [ ] Tray icon appears in the notification area
- [ ] Right-click menu shows at minimum: Status, Exit
- [ ] Log file is created and rotates daily

---

### M2: Telegram Loop

**Goal:** A real Telegram connection that routes messages to authorized users and echoes them back. No LLM yet.

#### 3.2.1 Telegram Worker (ARIA.Telegram)

```csharp
public class TelegramWorker : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly IMessageRouter _router;
    private readonly TelegramOptions _options;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _bot.ReceiveAsync(HandleUpdateAsync, HandleErrorAsync,
                    receiverOptions, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Telegram polling crashed, retrying...");
                await Task.Delay(ExponentialBackoff.Next(), stoppingToken);
            }
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.Message is not { } message) return;

        // Authorization check — first gate
        if (!_options.AuthorizedUserIds.Contains(message.From!.Id))
            return; // silently ignore

        await _router.RouteAsync(message, ct);
    }
}
```

**`IMessageRouter`** is the seam between the Telegram layer and the Agent Core. In M2, the implementation just echoes the message back. In M3, it dispatches to the conversation loop.

#### 3.2.2 Command Handling

Use a command registry pattern rather than a chain of if/else:

```csharp
public interface IBotCommand
{
    string Command { get; }  // e.g. "new", "status", "jobs"
    Task ExecuteAsync(Message message, string? args, CancellationToken ct);
}

public class CommandRegistry
{
    private readonly Dictionary<string, IBotCommand> _commands;

    public async Task<bool> TryHandleAsync(Message message, CancellationToken ct)
    {
        if (message.Text is not { } text || !text.StartsWith('/'))
            return false;

        var parts = text.TrimStart('/').Split(' ', 2);
        var commandName = parts[0].ToLower();
        var args = parts.Length > 1 ? parts[1] : null;

        if (_commands.TryGetValue(commandName, out var command))
        {
            await command.ExecuteAsync(message, args, ct);
            return true;
        }
        return false;
    }
}
```

Implement `NewSessionCommand`, `StatusCommand` for M2. The remaining commands are implemented in their respective milestones.

#### M2 Acceptance Checklist
- [ ] Bot connects and receives updates
- [ ] Messages from unauthorized user IDs are silently dropped
- [ ] `/new` creates a new session and confirms via Telegram
- [ ] `/status` returns a static status string
- [ ] Any other text message gets echoed back
- [ ] Service restarts and reconnects after simulated network drop

---

### M3: LLM Integration

**Goal:** Real conversations, context file injection, streaming responses, and vision support.

#### 3.3.1 Ollama Adapter (ARIA.LlmAdapter)

Implement `ILlmAdapter` against the Ollama OpenAI-compatible endpoint (`/v1/chat/completions`).

```csharp
public class OllamaAdapter : ILlmAdapter
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;

    public LlmCapabilities Capabilities { get; private set; }

    public async Task DetectCapabilitiesAsync(CancellationToken ct)
    {
        // Query /api/show for the model's metadata to determine vision support.
        // Ollama's model info includes a "projectors" field; if it contains "clip",
        // the model supports vision.
        var info = await GetModelInfoAsync(_options.Model, ct);
        Capabilities = new LlmCapabilities(
            SupportsToolCalling: true,   // assume true; fall back gracefully if not
            SupportsVision: info.Projectors?.Contains("clip") ?? false,
            SupportsStreaming: true
        );
    }

    public async Task<LlmResponse> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken ct)
    {
        var request = BuildRequest(messages, tools, stream: false);
        var response = await _http.PostAsJsonAsync("/v1/chat/completions", request, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(ct: ct);
        return MapResponse(result!);
    }

    public async IAsyncEnumerable<LlmResponse> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var request = BuildRequest(messages, tools, stream: true);
        // Use SSE streaming; yield partial LlmResponse chunks as they arrive
        // ...
    }
}
```

**Vision:** When building the request, if any `ChatMessage` has `Images` populated and `Capabilities.SupportsVision` is true, encode images as base64 in the `content` array using the OpenAI multimodal format. If `SupportsVision` is false and images are present, the Agent Core should have already intercepted this and sent the user an explanatory message rather than calling the adapter.

**Tool calling fallback:** If the model doesn't return well-formed tool calls (common with smaller models), implement a JSON extraction fallback — include the tool definitions in the system prompt as a JSON schema description and parse the model's text output for JSON blocks matching the schema.

#### 3.3.2 Agent Conversation Loop (ARIA.Agent)

The conversation loop is the heart of ARIA. It handles the full turn cycle: inject context → build message history → call LLM → dispatch tool calls → loop until done → respond.

```csharp
public class ConversationLoop
{
    public async Task<string?> RunTurnAsync(
        long userId,
        string userText,
        IReadOnlyList<ImageAttachment>? images,
        CancellationToken ct)
    {
        var session = await _conversationStore.GetOrCreateActiveSessionAsync(userId, ct);

        // 1. Check vision capability if images attached
        if (images?.Count > 0 && !_llmAdapter.Capabilities.SupportsVision)
            return "The current model does not support image input. Please switch to a vision-capable model.";

        // 2. Build system prompt: inject context files
        var systemPrompt = await BuildSystemPromptAsync(ct);

        // 3. Load recent conversation history
        var history = await _conversationStore.GetRecentTurnsAsync(
            session.SessionId, _options.MaxConversationTurns, ct);

        // 4. Persist the incoming user turn
        await _conversationStore.AppendTurnAsync(new ConversationTurn(
            TurnId: 0, session.SessionId, userId,
            DateTime.UtcNow, "user", userText,
            null, null, SerializeImages(images)), ct);

        // 5. Build the full message list
        var messages = BuildMessageList(systemPrompt, history, userText, images);

        // 6. Agentic loop: call LLM, handle tool calls, repeat
        string? finalResponse = null;
        for (int i = 0; i < MaxToolCallIterations; i++)
        {
            var response = await _llmAdapter.CompleteAsync(
                messages, _skillRegistry.GetAllToolDefinitions(), ct);

            if (response.ToolCalls?.Count > 0)
            {
                // Execute each tool call and add results to the message list
                foreach (var toolCall in response.ToolCalls)
                {
                    var result = await _skillRegistry
                        .FindExecutor(toolCall.FunctionName)!
                        .ExecuteAsync(new ToolInvocation(
                            toolCall.FunctionName, toolCall.ArgumentsJson, userId, session.SessionId), ct);

                    messages = messages.Append(new ChatMessage(
                        "tool", result.ResultJson, toolCall.FunctionName,
                        null, null)).ToList();

                    await _conversationStore.AppendTurnAsync(/* tool result turn */, ct);
                }
                // Continue loop — model may want to call more tools
            }
            else
            {
                finalResponse = response.TextContent;
                break;
            }
        }

        // 7. Persist the assistant turn
        if (finalResponse != null)
            await _conversationStore.AppendTurnAsync(/* assistant turn */, ct);

        return finalResponse;
    }
}
```

**Key decisions here:**
- `MaxToolCallIterations` prevents infinite tool-call loops. 10 is a reasonable default.
- The system prompt injection happens every turn. Keep context file reads fast (cache in memory, invalidate via file watcher).
- For streaming, stream the assistant text response back to Telegram as chunks via `bot.EditMessageTextAsync` on a placeholder message — this gives a typing-indicator-like effect.

#### M3 Acceptance Checklist
- [ ] Agent answers a free-text question using the local Ollama model
- [ ] System prompt includes IDENTITY.md, SOUL.md, USER.md content
- [ ] Multi-turn context is retained within a session
- [ ] Sending an image to a vision-capable model produces a meaningful response
- [ ] Sending an image to a non-vision model produces a clear "not supported" message
- [ ] Streaming response is surfaced progressively in Telegram

---

### M4: Conversation Persistence

**Goal:** History survives service restarts; sessions can be archived and resumed.

This is mostly `SqliteConversationStore` implementing the `IConversationStore` interface fully. The schema is already in place from M1. Focus areas:

- **`GetRecentTurnsAsync`:** Order by `timestamp DESC`, take N, then reverse for chronological order. Include tool call and tool result turns so the model maintains accurate tool use context.
- **`ListRecentSessionsAsync`:** Apply the configurable retention filter (e.g. exclude sessions with `last_activity_at` older than 90 days from the listing, but don't delete them).
- **`/sessions` command:** Format the session list cleanly — show session ID (shortened), start date, and last activity. Something like:
  ```
  Your recent sessions:
  [1] abc123 — started 3 days ago, last active 1 hour ago ✓ (current)
  [2] def456 — started 1 week ago, last active 5 days ago
  [3] ghi789 — started 2 weeks ago, last active 2 weeks ago
  ```
- **`/resume [id]`:** Archive the current session, then set the target session as active for the user.

#### M4 Acceptance Checklist
- [ ] Conversation history survives a service restart
- [ ] `/new` archives the current session and starts a fresh one
- [ ] `/sessions` lists recent sessions with readable metadata
- [ ] `/resume [id]` restores a prior session and continues from where it left off
- [ ] Multiple authorized users have completely isolated session histories

---

### M5: Workspace Tools

**Goal:** The agent can create, read, update, and delete files within the workspace. Path traversal is impossible.

#### 3.5.1 Sandbox Enforcement

Implement a `WorkspaceSandbox` utility class used by all file tools:

```csharp
public class WorkspaceSandbox
{
    private readonly string _rootPath;

    public string ResolveSafe(string relativePath)
    {
        // Normalize and combine
        var combined = Path.GetFullPath(Path.Combine(_rootPath, relativePath));

        // Verify the resolved path is within the workspace root
        if (!combined.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && combined != _rootPath)
        {
            throw new WorkspaceSandboxException(
                $"Path '{relativePath}' resolves outside the workspace.");
        }

        // Check for symlinks pointing outside
        var info = new FileInfo(combined);
        if (info.LinkTarget != null)
        {
            var target = Path.GetFullPath(info.LinkTarget);
            if (!target.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
                throw new WorkspaceSandboxException("Symlink points outside workspace.");
        }

        return combined;
    }
}
```

Every file tool must call `ResolveSafe` before touching the filesystem. Do not pass the raw path from the LLM to any `File.*` method directly.

#### 3.5.2 Built-in File Tools

Register these as a built-in skill (no manifest file needed — hardcoded into `ISkillRegistry` at startup):

| Tool name | Parameters | Description |
|---|---|---|
| `read_file` | `path: string` | Returns file contents as a string |
| `write_file` | `path: string, content: string` | Writes/overwrites file |
| `append_file` | `path: string, content: string` | Appends to file |
| `list_directory` | `path: string` | Returns directory listing as JSON array |
| `create_directory` | `path: string` | Creates directory (including parents) |
| `delete_file` | `path: string` | Deletes a file (not a directory) |
| `move_file` | `source: string, destination: string` | Moves or renames within workspace |
| `file_exists` | `path: string` | Returns boolean |

These are also used by the context file tools (IDENTITY.md, SOUL.md, USER.md) — the context file built-ins are just thin wrappers that resolve the context directory path before delegating to the workspace tools.

#### M5 Acceptance Checklist
- [ ] Agent can create, read, list, and delete files via natural language requests
- [ ] `../` traversal attempt returns an error and is logged
- [ ] Absolute paths outside the workspace are rejected
- [ ] Symlinks pointing outside the workspace are rejected
- [ ] Tool results are returned to the model and incorporated into the response

---

### M6: Skill System

**Goal:** The agent can load and use `SKILL.md` instruction files, and can author new skills by writing new `SKILL.md` files.

Skills are plain Markdown files — not executables or manifests. Each skill lives at `workspace/skills/<skill-name>/SKILL.md` and contains natural-language instructions the LLM reads to understand how to perform a capability. There is no subprocess execution, no entry-point validation, and no code involved. The agent accesses skill instructions through its normal file-reading tools and through the system prompt.

#### 3.6.1 Skill Loader (ARIA.Skills)

```csharp
public record SkillEntry(string Name, string Directory, string Content);

public class SkillLoader
{
    private readonly string _skillsDirectory;

    public IReadOnlyList<SkillEntry> Load()
    {
        if (!Directory.Exists(_skillsDirectory))
            return [];

        var entries = new List<SkillEntry>();
        foreach (var dir in Directory.EnumerateDirectories(_skillsDirectory))
        {
            var skillFile = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillFile))
            {
                _logger.LogWarning("Skill directory {Dir} has no SKILL.md, skipping", dir);
                continue;
            }
            var name = Path.GetFileName(dir);
            var content = File.ReadAllText(skillFile);
            entries.Add(new SkillEntry(name, dir, content));
        }
        return entries;
    }
}
```

#### 3.6.2 Skill Store (ARIA.Skills)

```csharp
public class SkillStore : ISkillStore
{
    private ImmutableList<SkillEntry> _skills = ImmutableList<SkillEntry>.Empty;
    private readonly SkillLoader _loader;
    private FileSystemWatcher? _watcher;

    public IReadOnlyList<SkillEntry> Skills => _skills;

    public Task ReloadAsync(CancellationToken ct)
    {
        _skills = _loader.Load().ToImmutableList();
        _logger.LogInformation("Loaded {Count} skill(s)", _skills.Count);
        return Task.CompletedTask;
    }

    public void StartWatching(string skillsDirectory)
    {
        _watcher = new FileSystemWatcher(skillsDirectory, "SKILL.md")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, _) => _ = ReloadAsync(CancellationToken.None);
        _watcher.Created += (_, _) => _ = ReloadAsync(CancellationToken.None);
    }
}
```

#### 3.6.3 Inject Skills into System Prompt

In `SystemPromptBuilder`, append a `## Available Skills` section after the context files:

```
## Available Skills

The following skills are available. Read the instructions in each to know how to apply them.

### create_new_skill
To create a new skill, create a new subdirectory under workspace/skills/<skill-name>/, then
write a SKILL.md file within it. The SKILL.md should describe the capability and provide
step-by-step instructions for how to carry it out. Use the create_directory and write_file
tools to do this, then inform the user the skill is ready.

### <other-skill-name>
<content of that SKILL.md>
```

If the combined skill content would push the system prompt over the token budget, include only the skill name and first paragraph of each `SKILL.md`, noting that the agent can `read_file` the full content if needed.

#### 3.6.4 Seed `create_new_skill` on First Run

On first run (during `OnboardingFlow` or at `DatabaseMigrator` startup), write the `create_new_skill` skill to disk if it does not already exist:

```csharp
var skillDir = Path.Combine(_options.Skills.Directory, "create_new_skill");
var skillFile = Path.Combine(skillDir, "SKILL.md");
if (!File.Exists(skillFile))
{
    Directory.CreateDirectory(skillDir);
    File.WriteAllText(skillFile, EmbeddedResources.CreateNewSkillMd);
}
```

The `SKILL.md` content is an embedded string resource, not generated at runtime.

#### M6 Acceptance Checklist
- [ ] Skills directory is scanned at startup; all `SKILL.md` files are loaded into `ISkillStore`
- [ ] Subdirectories missing a `SKILL.md` log a warning and are skipped without crashing
- [ ] Loaded skill instructions appear in the system prompt under `## Available Skills`
- [ ] The agent can write a new skill using `create_directory` + `write_file` and the skill appears after `/reloadskills`
- [ ] `/reloadskills` reloads the skill store and reports the new count
- [ ] `create_new_skill/SKILL.md` is seeded on first run if absent
- [ ] Editing an existing `SKILL.md` externally triggers automatic reload via `FileSystemWatcher`

---

### M7: Scheduler + Create Scheduled Job

**Goal:** Jobs fire on schedule and notify the user via Telegram; natural-language job creation works.

#### 3.7.1 Quartz.NET Setup (ARIA.Scheduler)

Quartz.NET is the right choice here — it handles cron expressions, missed firings, clustering (not needed for v1 but nice), and persists job state to the database (or in-memory for simplicity).

```csharp
// In service registration
services.AddQuartz(q =>
{
    q.UseInMemoryStore(); // Jobs are persisted in SQLite separately; Quartz just needs to know about live jobs
    q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 2);
});
services.AddQuartzHostedService(opt => opt.WaitForJobsToComplete = true);
services.AddSingleton<ISchedulerService, QuartzSchedulerService>();
```

The `ISchedulerService` wraps Quartz with ARIA-specific logic:

```csharp
public interface ISchedulerService
{
    Task ScheduleJobAsync(ScheduledJob job, CancellationToken ct);
    Task CancelJobAsync(string jobId, CancellationToken ct);
    Task LoadPersistedJobsAsync(CancellationToken ct); // called at startup
}
```

The Quartz job implementation:

```csharp
public class AgentTaskJob : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var jobId = context.JobDetail.Key.Name;
        var job = await _jobStore.GetJobAsync(jobId);
        if (job == null) return;

        var logEntry = new JobExecutionLog(/* ... */);
        try
        {
            // Inject a synthetic user turn into the conversation loop
            var response = await _conversationLoop.RunTurnAsync(
                userId: job.CreatedByUserId,
                userText: job.TaskPrompt,
                images: null,
                ct: context.CancellationToken);

            // Send response to the user's Telegram
            await _telegramBot.SendTextMessageAsync(job.CreatedByUserId, response);
            logEntry = logEntry with { Success = true, CompletedAt = DateTime.UtcNow };
        }
        catch (Exception ex)
        {
            logEntry = logEntry with { Success = false, ErrorMessage = ex.Message };
            await _telegramBot.SendTextMessageAsync(job.CreatedByUserId,
                $"⚠️ Scheduled job failed: {ex.Message}");
        }
        finally
        {
            await _jobStore.LogExecutionAsync(logEntry);
        }
    }
}
```

#### 3.7.2 Create Scheduled Job Built-in

The flow:

1. User sends: `"Every weekday at 8:30am, summarize my unread emails and list my calendar events for the day"`
2. Agent invokes `create_scheduled_job` tool with `description` parameter
3. LLM extracts cron expression and task prompt from the description
4. Present to user: `"I'll run this every weekday at 8:30am: 'Summarize unread emails and list calendar events.' Confirm? (yes/no)"`
5. On confirmation: persist to SQLite, register with Quartz immediately

For cron extraction, prompt the LLM explicitly:
```
Extract a cron expression (5-field: minute hour day month weekday) and a task prompt
from this job description. Return JSON only:
{"cron": "30 8 * * 1-5", "prompt": "Summarize my unread emails and list my calendar events for today."}
```

Use `NCrontab.CronSchedule.TryParse()` to validate the extracted cron expression before presenting to the user.

#### M7 Acceptance Checklist
- [ ] Jobs are loaded from SQLite and registered with Quartz on service startup
- [ ] A job fires at the scheduled time and sends the agent's response via Telegram
- [ ] `/jobs` lists all active jobs with their schedule and next fire time
- [ ] `/canceljob [id]` deactivates the job and removes it from Quartz
- [ ] Create Scheduled Job extracts cron expression and prompt from natural language
- [ ] Ambiguous descriptions prompt a clarifying question before proceeding
- [ ] Job failures notify the user and are logged to the database

---

### M8: Google OAuth & API Integration

**Goal:** Users can authorize Google access from Telegram; Gmail and Calendar skills work.

#### 3.8.1 OAuth Flow (ARIA.Google)

```csharp
public class GoogleAuthService
{
    public async Task<bool> AuthorizeAsync(CancellationToken ct)
    {
        var clientSecrets = new ClientSecrets
        {
            ClientId = _credentialStore.Load("google.clientId"),
            ClientSecret = _credentialStore.Load("google.clientSecret")
        };

        // This opens the default browser and starts a loopback listener
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets,
            scopes: [], // start with empty; incremental scopes added per skill
            "user",
            ct,
            new FileDataStore(GetTokenStorePath(), fullPath: true));

        _cachedCredential = credential;
        return true;
    }

    public async Task<UserCredential> GetCredentialWithScopesAsync(
        IReadOnlyList<string> requiredScopes, CancellationToken ct)
    {
        // If credential doesn't have all required scopes, re-authorize
        // This triggers incremental consent
        if (!requiredScopes.All(s => _cachedCredential!.Token.Scope?.Contains(s) ?? false))
        {
            await ReauthorizeWithScopesAsync(requiredScopes, ct);
        }
        return _cachedCredential!;
    }
}
```

**Token storage:** Use the Google library's `FileDataStore` pointed at a DPAPI-encrypted directory, or implement a custom `IDataStore` that stores tokens via `ICredentialStore`. The latter is cleaner but more work. For v1, the file-based approach is acceptable as long as the tokens directory is in `%LOCALAPPDATA%\ARIA\` with appropriate NTFS permissions.

**`/connectgoogle` command:** Invokes `AuthorizeAsync`, which opens the browser. Send a "Opening browser for Google authorization..." message first, then await the result and confirm success or failure.

#### 3.8.2 Gmail & Calendar Skills

Implement these as in-process skills (rather than subprocess) since they use the Google .NET client library directly:

```csharp
public class GmailSkillExecutor : ISkillExecutor
{
    public async Task<ToolInvocationResult> ExecuteAsync(ToolInvocation invocation, CancellationToken ct)
    {
        var credential = await _googleAuth.GetCredentialWithScopesAsync(
            ["https://www.googleapis.com/auth/gmail.readonly"], ct);

        var service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential
        });

        return invocation.ToolName switch
        {
            "search_gmail" => await SearchGmailAsync(service, invocation.ArgumentsJson, ct),
            "get_email" => await GetEmailAsync(service, invocation.ArgumentsJson, ct),
            "send_email" => await SendEmailAsync(service, invocation.ArgumentsJson, ct),
            _ => new ToolInvocationResult(false, "{}", $"Unknown tool: {invocation.ToolName}")
        };
    }
}
```

For each API call, handle `Google.Apis.Requests.RequestError` and surface a helpful message if the token needs re-authorizing.

#### M8 Acceptance Checklist
- [ ] `/connectgoogle` opens the browser; successful auth confirmed via Telegram
- [ ] `/disconnectgoogle` deletes tokens; subsequent Google skill calls fail gracefully
- [ ] `search_gmail` returns matching email subjects and summaries
- [ ] `send_email` sends a message via the Gmail API
- [ ] `get_calendar_events` returns events for a given date range
- [ ] Expired token is automatically refreshed; user not interrupted unless refresh fails
- [ ] Failed refresh notifies the user via Telegram to re-authorize

---

### M9: Onboarding & Context Files

**Goal:** Fresh install delivers a first-run conversational wizard; context files are seeded, injected, and self-updating.

#### 3.9.1 Onboarding Flow

The onboarding wizard runs inside the normal Telegram conversation on first startup. Detect "first run" by checking whether IDENTITY.md exists (or a `setup_complete` flag in the database).

```csharp
public class OnboardingFlow
{
    // State machine: each step returns the next step or null if complete
    private static readonly OnboardingStep[] Steps = [
        new ChooseAgentNameStep(),
        new ConfirmOllamaStep(),
        new GoogleCredentialsStep(),
        new ReviewIdentityStep(),
        new ReviewSoulStep(),
        new UserInterviewStep(),     // populates USER.md via Q&A
        new CompleteStep()
    ];
}
```

The `UserInterviewStep` runs a short conversational interview (5–7 questions) driven by the LLM:
- "What's your name?"
- "What do you do for work?"
- "What timezone are you in?"
- "What are you hoping I can help you with most?"
- etc.

After the interview, the agent writes the collected facts to USER.md using the `write_file` tool.

#### 3.9.2 Context File Injection

Build the system prompt by concatenating:

```
[IDENTITY.md content]
[SOUL.md content]
[USER.md content]
---
Current date and time: {DateTime.Now}
Active session: {sessionId}
```

Monitor combined token count. A rough estimate: 1 token ≈ 4 characters. If the context files exceed the configured limit (default 4,000 tokens), append a warning to the system prompt instructing the model to tell the user and suggest summarising.

#### 3.9.3 File Watcher

```csharp
public class ContextFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly IContextFileStore _store;

    public ContextFileWatcher(string contextDirectory, IContextFileStore store)
    {
        _watcher = new FileSystemWatcher(contextDirectory, "*.md")
        {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, e) => _store.InvalidateCache(e.Name);
    }
}
```

The cache in `IContextFileStore` is a simple `Dictionary<ContextFile, (string Content, DateTime LoadedAt)>`. Invalidation just clears the relevant entry; the next `ReadAsync` call reloads from disk.

#### M9 Acceptance Checklist
- [ ] First-run wizard fires on fresh install; agent name is collected as step 1
- [ ] IDENTITY.md is seeded with the user's chosen agent name
- [ ] SOUL.md and USER.md are seeded with default content
- [ ] USER.md is populated via conversational interview during first session
- [ ] Context files are injected correctly into every LLM request
- [ ] Editing a context file externally triggers a cache invalidation; next request uses new content
- [ ] Agent uses USER.md information (e.g. user's name, timezone) in responses without being prompted
- [ ] Context file token budget warning fires when files approach the limit

---

### M10: Installer

**Goal:** A single-file installer that sets up ARIA cleanly on a fresh Windows machine.

#### 3.10.1 Inno Setup (Recommended for v1)

Inno Setup is simpler to work with than WiX and produces a polished `.exe` installer. WiX produces a proper `.msi` (preferable for enterprise environments) but has a steeper learning curve. For a personal-use product, Inno Setup is the pragmatic choice.

Key installer script sections:

```iss
[Setup]
AppName=ARIA
AppVersion=1.0.0
DefaultDirName={autopf}\ARIA
DefaultGroupName=ARIA
OutputBaseFilename=ARIA-Setup-1.0.0
Compression=lzma2
SolidCompression=yes

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs
Source: "ARIA.TrayHost.exe"; DestDir: "{app}"

[Run]
; Install the Windows service
Filename: "{app}\ARIA.Service.exe"; Parameters: "install"; Flags: runhidden

; Install the tray host as a startup program
Filename: "{sys}\reg.exe"; Parameters: "add HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v ARIA /t REG_SZ /d ""{app}\ARIA.TrayHost.exe"" /f"; Flags: runhidden

[UninstallRun]
Filename: "{app}\ARIA.Service.exe"; Parameters: "uninstall"; Flags: runhidden
Filename: "{sys}\reg.exe"; Parameters: "delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v ARIA /f"; Flags: runhidden
```

**Prerequisite check:** Use `[Code]` section to check for .NET 8 runtime and offer to download it if missing. The `dotnet-install.ps1` script can be bundled or downloaded at install time.

**First-run config wizard:** After the service starts, the installer launches the Telegram bot, which begins the onboarding flow automatically when the user messages it. No separate GUI installer wizard is needed — the onboarding happens in Telegram.

**Upgrade support:** Run the new installer over the old one. The `[Run]` section stops the service before installation (`sc stop ARIAService`) and restarts it after.

#### M10 Acceptance Checklist
- [ ] Installer runs cleanly on a fresh Windows 10 (20H2+) and Windows 11 machine
- [ ] Service is registered and starts after install
- [ ] Tray host launches on login
- [ ] Start Menu shortcuts are created
- [ ] Running a newer installer over an existing one upgrades in-place without data loss
- [ ] Uninstaller removes service, files, and registry entries; prompts to keep workspace data

---

### M11: WPF Settings UI

**Goal:** The settings window is fully functional and shows live agent status.

#### 3.11.1 WPF Settings Window Structure

Organize the settings window into tabs:

- **General** — workspace path, log level
- **Model** — Ollama URL, model name, test connection button
- **Telegram** — bot token (masked), authorized user ID list (add/remove)
- **Google** — client ID, connect/disconnect button, current OAuth status
- **Context Files** — last modified dates for IDENTITY/SOUL/USER, open-in-editor buttons
- **Scheduled Jobs** — list of active jobs, cancel button per row

Use `CommunityToolkit.Mvvm`:

```csharp
public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _ollamaBaseUrl = "";
    [ObservableProperty] private string _ollamaModel = "";
    [ObservableProperty] private string _googleConnectionStatus = "Not connected";
    [ObservableProperty] private ObservableCollection<ScheduledJobRow> _scheduledJobs = [];

    [RelayCommand]
    private async Task TestOllamaConnectionAsync()
    {
        // Call service via named pipe to test connectivity
        // Update a status label
    }

    [RelayCommand]
    private void SaveAndRestart()
    {
        // Write config.json, then restart service via ServiceController
        var svc = new ServiceController("ARIAService");
        svc.Stop();
        svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
        // Write settings...
        svc.Start();
    }
}
```

#### 3.11.2 IPC: Tray Host ↔ Service

For real-time status updates (agent health, current model, Google connection state), implement a simple named pipe server in `ARIA.Service` and a client in `ARIA.TrayHost`:

```csharp
// Status message sent over named pipe (JSON)
public record AgentStatusMessage(
    AgentStatus Status,         // Running, Degraded, Stopped
    string CurrentModel,
    bool GoogleConnected,
    int ActiveJobCount,
    DateTime LastActivity
);
```

The tray host polls every 10 seconds and updates the tray icon color and tooltip accordingly.

#### M11 Acceptance Checklist
- [ ] All config fields are editable via the settings window
- [ ] Saving prompts for service restart and executes it
- [ ] Google OAuth connect/disconnect works from the settings window
- [ ] Tray icon color reflects live service status
- [ ] Scheduled jobs are listed and cancellable from the UI
- [ ] Context file last-modified dates are shown; "Open" button launches default Markdown editor

---

### M12: Polish & Hardening

This milestone has no new features — it's about making the existing features robust enough to run unattended.

#### Error Recovery Checklist
- [ ] **Ollama goes offline mid-conversation:** Catch `HttpRequestException`, send user a clear message, retry with exponential backoff (1s, 2s, 4s, 8s, max 60s). Resume conversation when Ollama is back — don't start a new session.
- [ ] **Telegram connection drops:** The `ReceiveAsync` loop already retries; verify backoff caps at 60s and doesn't flood logs.
- [ ] **Skill execution timeout:** Confirm the process is killed cleanly and the tool result message to the LLM says "Skill timed out" so the model can respond gracefully rather than hanging.
- [ ] **Context file token overflow:** Verify the warning message is surfaced in Telegram, not just logged.
- [ ] **SQLite locked:** Use `WAL` mode (`PRAGMA journal_mode=WAL`) to allow concurrent readers without blocking writers.
- [ ] **Service crash loop:** Implement a backoff in the service restart logic — if the service crashes more than 3 times in 5 minutes, stop restarting and notify the user.
- [ ] **Disk full / permissions error on workspace:** File tools should return a clear error JSON to the model rather than throwing an unhandled exception.

#### Resource Limits
- [ ] Verify idle RSS is under 100MB. If not, profile with `dotnet-counters` and look for caching allocations in the skill registry or context file store.
- [ ] Verify CPU stays < 1% when idle (no scheduled jobs running). The Quartz thread pool should be idle; the Telegram long-poll should block on network I/O.

#### Logging Hygiene
- [ ] Ensure no credential values, OAuth tokens, or Telegram user messages appear in log output at any level.
- [ ] Confirm log rotation is working and old files are cleaned up.
- [ ] Verify Serilog structured logging produces readable output (use `{SourceContext}` enricher).

---

## 4. Cross-Cutting Concerns

These don't belong to a single milestone but need to be handled consistently throughout.

### 4.1 Cancellation Token Discipline

Every async method that touches I/O must accept and forward a `CancellationToken`. The root token comes from `IHostedService.ExecuteAsync(CancellationToken stoppingToken)`. Create linked tokens for per-operation timeouts:

```csharp
using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
cts.CancelAfter(TimeSpan.FromSeconds(30));
await SomeOperationAsync(cts.Token);
```

### 4.2 Structured Logging Conventions

Use Serilog's message templates (not string interpolation) so values are captured as structured properties:

```csharp
// Good
_logger.LogInformation("Tool {ToolName} executed in {ElapsedMs}ms for user {UserId}",
    toolName, elapsed.TotalMilliseconds, userId);

// Bad — no structured properties
_logger.LogInformation($"Tool {toolName} executed in {elapsed.TotalMilliseconds}ms");
```

Never log: message content, file contents, API tokens, OAuth tokens, or user IDs in contexts where they'd be a privacy concern.

### 4.3 Credential Store Conventions

Define constants for all credential keys in `ARIA.Core`:

```csharp
public static class CredentialKeys
{
    public const string TelegramBotToken = "telegram.bot_token";
    public const string GoogleClientSecret = "google.client_secret";
    public const string GoogleOAuthTokens = "google.oauth_tokens";
}
```

Never construct credential key strings inline — use these constants everywhere.

### 4.4 Testing Strategy

Given the Windows-specific nature of much of this code, prioritize testing at the boundaries:

- **ARIA.Core** — 100% unit testable; no excuses. Test domain models and validation logic.
- **ARIA.Agent** — Unit test `ConversationLoop` with mock `ILlmAdapter`, `ISkillRegistry`, `IConversationStore`. This is the most important test surface.
- **ARIA.Skills** — Unit test manifest validation and schema parsing. Integration test skill execution with a real subprocess (a trivial PowerShell echo script).
- **ARIA.Memory** — Integration test `SqliteConversationStore` against a real SQLite in-memory database (`:memory:` connection string).
- **ARIA.Scheduler** — Unit test cron expression extraction and job persistence. Test Quartz integration with an in-memory store.
- **ARIA.Telegram / ARIA.Google** — Mostly integration; mock at the HTTP client level using `MockHttp` or similar.

---

## 5. Development Environment Setup

Before writing any code, ensure the following are in place:

1. **Visual Studio 2022** (v17.8+) or **Rider** — both support .NET 8 Worker Services and WPF natively
2. **Windows 10 or 11 dev machine** — WPF and the Windows Service APIs require Windows
3. **Ollama installed locally** — grab a small model for development (e.g. `llama3.2:3b` or `phi3:mini`) so you're not waiting on large model inference during iteration
4. **Telegram bot token** — create a bot via [@BotFather](https://t.me/BotFather) and note the token
5. **Google Cloud project** (optional for early milestones) — create a project, enable Gmail and Calendar APIs, create OAuth 2.0 credentials (Desktop app type)
6. **Inno Setup** — download and install for M10
7. **WiX Toolset v4** — optional alternative to Inno Setup; install the VS extension if you go this route

For the SQLite database during development, point `workspace.databasePath` to a local temp directory so you can delete and recreate it freely without affecting any real data.

---

*End of Document — ARIA Implementation Plan v0.1*
