# ARIA — Concrete Implementation Plan

**Current state:** Phase 1 (M1 + M2) complete. Service runs, connects to Telegram, persists SQLite schema, and handles all commands below (fully or as informative stubs).

---

## Bot Command Reference

| Command | Description | Milestone |
|---|---|---|
| `/help` | List all available commands | M2 ✅ |
| `/new` | Archive current session and start fresh | M2 ✅ (session wiring M4) |
| `/status` | Agent health, connected services, active model | M2 ✅ (live data M3) |
| `/model [name]` | Show active model, or switch to a different one | M3 |
| `/models` | List all Ollama models with sizes and active indicator | M3 |
| `/sessions` | List recent conversation sessions | M4 |
| `/resume <id>` | Resume a previous session | M4 |
| `/reloadskills` | Hot-reload all SKILL.md files | M6 |
| `/jobs` | List active scheduled jobs | M7 |
| `/canceljob <id>` | Cancel a scheduled job | M7 |
| `/google_setup` | Upload client_secret.json to configure OAuth credentials | M8 |
| `/google_connect` | Start OAuth flow and receive an authorization link | M8 |
| `/google_complete <code>` | Complete OAuth manually (fallback for remote use) | M8 |
| `/google_disconnect` | Revoke Google access and delete stored tokens | M8 |

---

## Phase 1: Foundation (M1 + M2)

*Goal: A running Windows service that accepts Telegram messages from authorized users and echoes them back. No LLM yet.*

### M1 — Windows Service + Tray Icon + Config + SQLite

#### Step 1.1 — Core abstractions (`ARIA.Core`)

- [ ] `Options/AriaOptions.cs` — top-level config POCO with nested `WorkspaceOptions`, `OllamaOptions`, `TelegramOptions`, `GoogleOptions`, `AgentOptions`, `SkillsOptions`, `SchedulerOptions`, `HeartbeatOptions`, `PersonalityOptions`, `LoggingOptions`
- [ ] `Models/Session.cs` — `record Session(string SessionId, long TelegramUserId, DateTime StartedAt, DateTime LastActivityAt, bool IsActive)`
- [ ] `Models/ConversationTurn.cs` — `record ConversationTurn(long TurnId, string SessionId, long TelegramUserId, DateTime Timestamp, string Role, string? TextContent, string? ToolCallsJson, string? ToolResultJson, string? ImageDataJson)`
- [ ] `Models/ScheduledJob.cs` — `record ScheduledJob(...)` + `record JobExecutionLog(...)`
- [ ] `Models/LlmModels.cs` — `record LlmCapabilities`, `record ChatMessage`, `record ToolCall`, `record ToolResult`, `record LlmResponse`, `record ImageAttachment`
- [ ] `Models/ToolModels.cs` — `record ToolDefinition`, `record ToolInvocation`, `record ToolInvocationResult`
- [ ] `Models/SkillModels.cs` — `record SkillEntry(string Name, string Directory, string Content)` — represents a loaded `SKILL.md`
- [ ] `Interfaces/IConversationStore.cs`
- [ ] `Interfaces/IContextFileStore.cs` — includes `ContextFile` enum (`Identity`, `Soul`, `User`)
- [ ] `Interfaces/ILlmAdapter.cs`
- [ ] `Interfaces/IToolExecutor.cs` — dispatches LLM function-calling tool invocations for built-in tools (file ops, Google, etc.)
- [ ] `Interfaces/IToolRegistry.cs` — provides `IReadOnlyList<ToolDefinition>` for injection into LLM requests; finds `IToolExecutor` by tool name
- [ ] `Interfaces/ISkillStore.cs` — provides `IReadOnlyList<SkillEntry>` from loaded `SKILL.md` files; supports reload
- [ ] `Interfaces/ICredentialStore.cs`
- [ ] `Interfaces/ISchedulerService.cs`
- [ ] `Constants/CredentialKeys.cs` — `TelegramBotToken`, `GoogleClientSecret`, `GoogleOAuthTokens`
- [ ] `Exceptions/WorkspaceSandboxException.cs`

#### Step 1.2 — Credential store (`ARIA.Core` or inline in `ARIA.Service`)

- [ ] `ARIA.Service/Security/DpapiCredentialStore.cs` — implements `ICredentialStore` using `System.Security.Cryptography.ProtectedData`; stores one encrypted blob per key under `%LOCALAPPDATA%\ARIA\creds\`

#### Step 1.3 — SQLite schema + migrations (`ARIA.Memory`)

- [ ] `Migrations/DatabaseMigrator.cs` — creates `sessions`, `conversation_turns`, `scheduled_jobs`, `job_execution_log`, `schema_version` tables; enables WAL mode (`PRAGMA journal_mode=WAL`)
- [ ] `Sqlite/SqliteConversationStore.cs` — stub implementing `IConversationStore` (all methods `throw new NotImplementedException()` for now; filled in during M4)
- [ ] `Context/MarkdownContextFileStore.cs` — stub implementing `IContextFileStore`

#### Step 1.4 — Service host wiring (`ARIA.Service`)

- [ ] Rewrite `Program.cs` to use `UseWindowsService`, `ConfigureAppConfiguration` (read `config.json` from `%LOCALAPPDATA%\ARIA\config.json`), register `DpapiCredentialStore`, `SqliteConversationStore`, `MarkdownContextFileStore`, wire Serilog with rolling file sink
- [ ] Replace `AgentWorker.cs` stub with a real `BackgroundService` that runs `DatabaseMigrator.MigrateAsync()` on startup then idles

#### Step 1.5 — Tray host (`ARIA.TrayHost`)

- [ ] `TrayApplication.cs` — `ApplicationContext` subclass; creates `NotifyIcon` with a static green icon and a context menu: **Status**, **Restart**, **Open Logs**, **Settings**, **Exit**
- [ ] `SettingsWindow.xaml` + `SettingsWindow.xaml.cs` — empty WPF window opened on "Settings" click; placeholder tabs
- [ ] Rewrite `Program.cs` to instantiate `TrayApplication` and call `Application.Run(trayApp)`
- [ ] Embed three `.ico` resources: `green.ico`, `amber.ico`, `red.ico`

**M1 done when:** Service installs via `sc create`, starts cleanly, creates `aria.db`, tray icon appears, log file rotates.

---

### M2 — Telegram Loop

#### Step 2.1 — Telegram worker (`ARIA.Telegram`)

- [ ] `Handlers/TelegramWorker.cs` — `BackgroundService`; uses `ITelegramBotClient.ReceiveAsync` with exponential backoff on crash; authorization check against `TelegramOptions.AuthorizedUserIds` (silently drop unauthorized)
- [ ] `Handlers/MessageRouter.cs` + `IMessageRouter` interface — routes `Message` to either `CommandRegistry` or the agent (echo for now)
- [ ] `Commands/IBotCommand.cs` interface
- [ ] `Commands/CommandRegistry.cs` — dictionary-based dispatcher
- [ ] `Commands/NewSessionCommand.cs` — creates session, replies with confirmation
- [ ] `Commands/StatusCommand.cs` — returns static "ARIA is running" string

#### Step 2.2 — Wire into service (`ARIA.Service`)

- [ ] Register `ITelegramBotClient` (load token from `ICredentialStore`), `TelegramWorker`, `CommandRegistry`, and commands in DI
- [ ] Add `TelegramWorker` as a hosted service

**M2 done when:** Authorized user sends a message and gets an echo; unauthorized user is ignored; `/new` and `/status` work.

---

## Phase 2: Intelligence (M3 + M4)

*Goal: Real conversations with the local Ollama model, persistent across restarts.*

### M3 — LLM Integration

#### Step 3.1 — Ollama adapter (`ARIA.LlmAdapter`)

- [ ] `Ollama/OllamaAdapter.cs` — implements `ILlmAdapter` using `OllamaSharp`
  - `DetectCapabilitiesAsync()` — queries `/api/show`, sets `LlmCapabilities.SupportsVision` based on presence of `"clip"` in projectors
  - `CompleteAsync()` — non-streaming completion, maps to/from `ChatMessage`/`LlmResponse`
  - `StreamAsync()` — SSE streaming via `IAsyncEnumerable<LlmResponse>`
  - `CheckConnectivityAsync()` — HEAD or GET to `/` for health check
- [ ] `Ollama/OllamaRequestBuilder.cs` — builds the request payload (messages, tools, stream flag); encodes images as base64 in multimodal format when `SupportsVision` is true
- [ ] `Ollama/ToolCallFallbackParser.cs` — extracts JSON tool-call blocks from plain text for models that don't support native function calling

#### Step 3.2 — Conversation loop (`ARIA.Agent`)

- [ ] `Conversation/ConversationLoop.cs` — full agentic loop:
  1. Check vision capability; reject with message if image sent to non-vision model
  2. `BuildSystemPromptAsync()` — conditionally reads and concatenates context files based on config: `personality.identity.enabled` controls `IDENTITY.md`, `personality.soul.enabled` controls `SOUL.md`, and `personality.user.enabled` controls `USER.md`; appends skill summaries from `ISkillStore`; appends current UTC time and session ID
  3. Load recent N turns from `IConversationStore` only when `personality.memory.enabled` is true
  4. Persist incoming user turn
  5. Loop up to `MaxToolCallIterations` (10): call `ILlmAdapter.CompleteAsync` → if tool calls, execute via `IToolRegistry`, append results, continue; else break with final text
  6. Persist assistant turn
- [ ] `Prompts/SystemPromptBuilder.cs` — assembles the system prompt; estimates token count (chars ÷ 4); logs a warning if above threshold

#### Step 3.3 — Wire into Telegram + service

- [ ] Update `MessageRouter` to call `ConversationLoop.RunTurnAsync` and stream partial responses back via `EditMessageTextAsync` on a placeholder message
- [ ] Register `ILlmAdapter`, `ConversationLoop`, `SystemPromptBuilder` in DI; call `DetectCapabilitiesAsync` during startup

#### Step 3.4 — Heartbeat worker (`ARIA.Agent`)

- [ ] `Heartbeat/HeartbeatWorker.cs` — `BackgroundService`; reads `HeartbeatOptions` from config; if `Enabled` is false, exits immediately
- [ ] On each tick: read `HEARTBEAT.md` from `workspace/context/`; if the file does not exist, skip silently and log a debug message
- [ ] Inject the file content as a synthetic user turn into `ConversationLoop.RunTurnAsync`; send the LLM response to all authorized Telegram user IDs via `IMessageRouter`
- [ ] Timer interval driven by `HeartbeatOptions.IntervalMinutes`; first tick fires at the first interval boundary (not immediately on startup)
- [ ] Register as a hosted service in `ARIA.Service/Program.cs`

#### Step 3.5 — Seed `HEARTBEAT.md` (defer to M9)

- [ ] During the M9 onboarding flow, seed `workspace/context/HEARTBEAT.md` with default starter content instructing the agent to reflect on recent activity, check for pending tasks, and proactively surface anything the user should know

**M3 done when:** Free-text question answered using Ollama; enabled context files are injected; multi-turn context is retained only when `personality.memory.enabled` is true; image handling works per capability flags; heartbeat fires on schedule when enabled and `HEARTBEAT.md` exists.

---

### M4 — Conversation Persistence

#### Step 4.1 — Implement `SqliteConversationStore` (`ARIA.Memory`)

- [ ] `GetOrCreateActiveSessionAsync` — `SELECT` active session for user; `INSERT` new if none
- [ ] `ArchiveSessionAsync` — sets `is_active = 0`
- [ ] `AppendTurnAsync` — `INSERT` into `conversation_turns`
- [ ] `GetRecentTurnsAsync` — `SELECT ... ORDER BY timestamp DESC LIMIT N` then reverse; include tool call and tool result rows
- [ ] `ListRecentSessionsAsync` — respects configurable retention days filter
- [ ] `GetSessionByIdAsync`, `ArchiveSessionAsync`

#### Step 4.2 — Session commands (`ARIA.Telegram`)

- [ ] `Commands/SessionsCommand.cs` — formats session list (shortened ID, start date, last activity, current flag)
- [ ] `Commands/ResumeCommand.cs` — archives current, sets target session active

**M4 done when:** History survives restart; `/new`, `/sessions`, `/resume` work; multiple users have isolated histories.

---

## Phase 3: Actions (M5 + M6 + M7)

*Goal: The agent can read/write the workspace, run external skills, and fire scheduled tasks.*

### M5 — Workspace File Tools

#### Step 5.1 — Sandbox (`ARIA.Skills/Sandbox`)

- [ ] `WorkspaceSandbox.cs` — `ResolveSafe(string relativePath)`: `Path.GetFullPath(Path.Combine(root, rel))`, verify starts with root + separator, check for symlinks pointing outside

#### Step 5.2 — Built-in file tools (`ARIA.Skills/BuiltIn/FileTools`)

- [ ] `FileToolsExecutor.cs` — implements `ISkillExecutor`; dispatches on `ToolInvocation.ToolName`:
  - `read_file`, `write_file`, `append_file`, `list_directory`, `create_directory`, `delete_file`, `move_file`, `file_exists`
  - All paths go through `WorkspaceSandbox.ResolveSafe` before any `File.*` call
- [ ] `FileToolDefinitions.cs` — static list of `ToolDefinition` records (name, description, JSON Schema) for each file tool

#### Step 5.3 — Context file tools (built-in)

- [ ] `ContextFileToolsExecutor.cs` — thin wrapper; resolves context directory, then delegates to `FileToolsExecutor`; registers `read_context_file`, `write_context_file`

#### Step 5.4 — Wire into tool registry

- [ ] `ToolRegistry.cs` — implements `IToolRegistry`; aggregates all built-in tool executors; hardcodes file tool and context file tool registrations at startup

**M5 done when:** Agent creates, reads, lists, and deletes workspace files; path traversal rejected; symlinks blocked.

---

### M6 — Skill System (SKILL.md loader)

Skills are Markdown instruction files, not executables. Each skill lives at `workspace/skills/<skill-name>/SKILL.md` and contains natural-language instructions the LLM reads to understand how to perform a complex capability. The `create_new_skill` skill is itself a `SKILL.md` that teaches the agent how to author new skills.

#### Step 6.1 — Skill loader (`ARIA.Skills/Loader`)

- [ ] `SkillLoader.cs` — scans `workspace/skills/`; for each subdirectory containing a `SKILL.md`, reads the file and produces a `SkillEntry(Name, Directory, Content)`; logs a warning for subdirectories missing `SKILL.md`; supports reload via `FileSystemWatcher` on the skills directory
- [ ] `SkillStore.cs` — implements `ISkillStore`; holds the loaded `SkillEntry` list in memory; exposes `ReloadAsync()` and the current list; thread-safe (use `ImmutableList` or lock on swap)

#### Step 6.2 — Inject skills into system prompt

- [ ] Update `SystemPromptBuilder.cs` to include a `## Available Skills` section listing each skill name and its `SKILL.md` content (or a brief summary if token budget is tight); the LLM uses this to know what capabilities it has and how to apply them

#### Step 6.3 — Seed the `create_new_skill` skill

- [ ] On first run (detected in `OnboardingFlow` or `DatabaseMigrator`), write `workspace/skills/create_new_skill/SKILL.md` with instructions telling the LLM: "To create a new skill, create a subdirectory under `workspace/skills/<skill-name>/`, then write a `SKILL.md` file within it describing the capability and how to use it. Use the `create_directory` and `write_file` tools to do this."
- [ ] After writing, trigger `ISkillStore.ReloadAsync()` so the new skill is immediately available

#### Step 6.4 — `/reloadskills` bot command (`ARIA.Telegram`)

- [ ] `Commands/ReloadSkillsCommand.cs` — calls `ISkillStore.ReloadAsync()` and reports how many skills are now loaded

**M6 done when:** All `SKILL.md` files are loaded at startup; skill instructions appear in the system prompt; the agent can write a new skill by creating a subdirectory and `SKILL.md`; `/reloadskills` picks up newly written skills without a service restart.

---

### M7 — Scheduler + Create Scheduled Job

#### Step 7.1 — Scheduler service (`ARIA.Scheduler`)

- [ ] `Store/SqliteJobStore.cs` — CRUD for `scheduled_jobs` and `job_execution_log` tables
- [ ] `Jobs/AgentTaskJob.cs` — Quartz `IJob`; calls `ConversationLoop.RunTurnAsync` with the job's prompt; sends response via Telegram; logs to `job_execution_log`; notifies user on failure
- [ ] `QuartzSchedulerService.cs` — implements `ISchedulerService`; wraps Quartz `IScheduler`; `ScheduleJobAsync`, `CancelJobAsync`, `LoadPersistedJobsAsync`

#### Step 7.2 — Wire Quartz into service host

- [ ] Register `AddQuartz` + `AddQuartzHostedService` in `Program.cs`; call `LoadPersistedJobsAsync` at startup

#### Step 7.3 — Scheduler commands + Create Scheduled Job built-in

- [ ] `Commands/JobsCommand.cs` — lists active jobs with cron, next fire time
- [ ] `Commands/CancelJobCommand.cs` — deactivates in DB, removes from Quartz
- [ ] `BuiltIn/CreateScheduledJob/CreateScheduledJobExecutor.cs`:
  1. LLM extracts `{"cron": "...", "prompt": "..."}` from natural-language description
  2. Validate cron via `NCrontab.CronSchedule.TryParse`; ask clarifying question if ambiguous
  3. Present to user for confirmation
  4. On confirm: persist to SQLite, register with Quartz immediately

**M7 done when:** Jobs survive restart; fire on schedule; results sent via Telegram; `/jobs` and `/canceljob` work; natural-language creation works.

---

## Phase 4: Integrations (M8 + M9)

*Goal: Google services authorized and usable; onboarding wizard seeds context files.*

### M8 — Google OAuth & API Integration

#### Step 8.1 — OAuth flow (`ARIA.Google/Auth`)

- [ ] `GoogleAuthService.cs` — manages the full OAuth lifecycle; stores tokens via `ICredentialStore` (DPAPI-encrypted); `GetCredentialWithScopesAsync()` handles incremental consent; `RevokeAsync()` deletes tokens
- [ ] `GoogleTokenStore.cs` — custom `IDataStore` backed by `ICredentialStore`
- [ ] Loopback listener: when `/google_connect` fires, start a temporary `HttpListener` on a random localhost port; generate the authorization URL using that port as the redirect URI; send URL to user via Telegram. If the user opens it in the local browser, the callback is received automatically and tokens are stored — no further command needed
- [ ] Manual fallback: `/google_complete <code_or_url>` extracts the authorization code and completes the token exchange for users who cannot trigger the loopback (e.g. issuing the command remotely via Telegram on another device)

#### Step 8.2 — Gmail + Calendar skills (`ARIA.Google/Skills`)

- [ ] `GmailSkillExecutor.cs` — `search_gmail`, `get_email`, `send_email`; each call gets credential via `GetCredentialWithScopesAsync`; handles `RequestError` for expired tokens
- [ ] `CalendarSkillExecutor.cs` — `get_calendar_events`, `create_calendar_event`
- [ ] Register both as in-process skills in `SkillRegistry`

#### Step 8.3 — Bot commands (stubs already registered; implement here)

- [ ] `GoogleSetupCommand` — accept `client_secret.json` as a Telegram file attachment; parse JSON; extract `client_id` and `client_secret`; store in `ICredentialStore`
- [ ] `GoogleConnectCommand` — start loopback listener; send OAuth URL to user; confirm when callback received or instruct user to use `/google_complete`
- [ ] `GoogleCompleteCommand` — accept authorization code or full redirect URL; extract code; exchange for tokens; store via `GoogleAuthService`
- [ ] `GoogleDisconnectCommand` — call `RevokeAsync`; confirm to user

**M8 done when:** `/google_setup` stores credentials; `/google_connect` sends OAuth URL; auto-completes via loopback when opened locally; `/google_complete` works as remote fallback; Gmail and Calendar tools callable; token refresh transparent; `/google_disconnect` cleans up.

---

### M9 — Onboarding & Context Files

#### Step 9.1 — Context file store (`ARIA.Memory/Context`)

- [ ] Implement `MarkdownContextFileStore.cs` fully: reads file from `workspace/context/`; in-memory cache with `Dictionary<ContextFile, (string Content, DateTime LoadedAt)>`; `InvalidateCache(string fileName)`
- [ ] `ContextFileWatcher.cs` — `FileSystemWatcher` on `*.md`; calls `InvalidateCache` on `Changed` event

#### Step 9.2 — Onboarding flow (`ARIA.Agent/Onboarding`)

- [ ] `OnboardingFlow.cs` — state machine; detects first run by checking absence of `IDENTITY.md` or a `setup_complete` DB flag
- [ ] Steps: `ChooseAgentNameStep` → `ConfirmOllamaStep` → `GoogleCredentialsStep` → `ReviewIdentityStep` → `ReviewSoulStep` → `UserInterviewStep` → `CompleteStep`
- [ ] `UserInterviewStep` — LLM-driven Q&A (5–7 questions); writes collected facts to `USER.md` via `write_context_file` tool
- [ ] Seed `IDENTITY.md`, `SOUL.md`, `USER.md` with default content (with agent name substituted in `IDENTITY.md`)

#### Step 9.3 — Token budget enforcement

- [ ] In `SystemPromptBuilder.cs`: estimate combined token count; append warning message if over threshold (default 4,000 tokens)

**M9 done when:** Fresh run triggers wizard; agent name written to `IDENTITY.md`; `USER.md` populated via interview; enabled context files are injected correctly; file edits hot-reloaded.

---

## Phase 5: Distribution (M10 + M11)

*Goal: Installable on a fresh Windows machine; settings fully configurable from the UI.*

### M10 — Installer

- [ ] Create `installer/ARIA.Setup.iss` (Inno Setup) with:
  - Prerequisite check for .NET 10 runtime; offer download if missing
  - Prompt for workspace folder path and Telegram bot token during install
  - Register `ARIA.Service.exe` as a Windows service (`sc create`)
  - Register `ARIA.TrayHost.exe` in `HKCU\...\Run`
  - Create Start Menu shortcuts
- [ ] `[UninstallRun]` section: stop + remove service; remove registry key; prompt to keep/delete workspace data
- [ ] `[Run]` section on upgrade: `sc stop ARIAService` before file copy, `sc start ARIAService` after

**M10 done when:** Clean install works on Windows 10 (20H2+) and Windows 11; upgrade in-place preserves data; uninstall is clean.

---

### M11 — WPF Settings UI

#### Step 11.1 — Settings ViewModel + View (`ARIA.TrayHost`)

- [ ] `SettingsViewModel.cs` — `ObservableObject` (CommunityToolkit.Mvvm); `[ObservableProperty]` for all fields: `OllamaBaseUrl`, `OllamaModel`, `GoogleConnectionStatus`, `ScheduledJobs : ObservableCollection<ScheduledJobRow>`
- [ ] `[RelayCommand]` methods: `TestOllamaConnectionAsync`, `ConnectGoogleAsync`, `DisconnectGoogleAsync`, `CancelJobAsync`, `SaveAndRestart`
- [ ] `SettingsWindow.xaml` — tabs: General, Model, Telegram, Google, Context Files, Scheduled Jobs; clean modern WPF style
- [ ] `SaveAndRestart` — writes `config.json`, then `ServiceController.Stop()` + `ServiceController.Start()`

#### Step 11.2 — Named pipe IPC

- [ ] `ARIA.Service/Ipc/StatusPipeServer.cs` — `NamedPipeServerStream`; serializes `AgentStatusMessage` JSON on each client connect
- [ ] `AgentStatusMessage` record: `AgentStatus Status`, `string CurrentModel`, `bool GoogleConnected`, `int ActiveJobCount`, `DateTime LastActivity`, `bool IsPaused`
- [ ] `ARIA.TrayHost/Ipc/StatusPipeClient.cs` — polls every 10 seconds; updates `NotifyIcon.Icon` (green/amber/red) and tooltip
- [ ] **Replace file-based pause with pipe command:** Add a `PauseCommand` / `ResumeCommand` message type to the pipe protocol; `TrayApplication.TogglePause()` sends the command over the pipe instead of writing `PauseFlag`; `MessageRouter` reads the paused state from an in-memory flag set by the pipe server rather than checking `PauseFlag.IsSet`. Delete `ARIA.Core/Constants/PauseFlag.cs` once the pipe is live.

**M11 done when:** All settings editable; Google OAuth connect/disconnect works from UI; tray icon color reflects live status; scheduled jobs listed and cancellable; Disable/Enable routed through pipe rather than flag file.

---

## Phase 6: Hardening (M12)

*Goal: Robust enough to run unattended.*

### M12 — Polish & Error Recovery

#### Resilience

- [ ] **Ollama offline mid-conversation:** Catch `HttpRequestException` in `ConversationLoop`; notify user; retry with exponential backoff (1 s → 2 s → 4 s → … → 60 s cap)
- [ ] **Telegram drop:** Verify `TelegramWorker` backoff caps at 60 s; confirm log volume is acceptable
- [ ] **Skill store reload errors:** If a `SKILL.md` file is malformed or unreadable during reload, log a warning and skip it — never crash the service or clear previously loaded skills
- [ ] **Context file token overflow:** Confirm warning is surfaced in Telegram, not only logged
- [ ] **SQLite WAL mode:** Verify `PRAGMA journal_mode=WAL` is set in `DatabaseMigrator`
- [ ] **Service crash loop:** Track crash count + timestamps in the host; if > 3 crashes in 5 minutes, stop auto-restart and notify user via Telegram
- [ ] **Disk/permissions error in workspace:** File tools return structured error JSON to the model rather than throwing unhandled exceptions

#### Resource limits

- [ ] Profile idle RSS with `dotnet-counters`; target < 100 MB; investigate skill registry or context file store if over budget
- [ ] Verify idle CPU < 1% (Quartz thread pool idle; Telegram long-poll blocking on I/O)

#### Logging hygiene

- [ ] Audit all log call sites: confirm no tokens, OAuth values, or message content appear at any log level
- [ ] Verify log rotation and old-file cleanup
- [ ] Add `{SourceContext}` enricher to Serilog config for readable output

---

## Cross-Cutting Reminders (apply throughout all phases)

- Every `async` method that touches I/O must accept and forward a `CancellationToken`; create linked tokens with per-operation timeouts via `CancellationTokenSource.CreateLinkedTokenSource`
- Use Serilog message templates (not string interpolation) for all structured log properties
- All credential access goes through `ICredentialStore` using constants from `CredentialKeys`; never construct key strings inline
- All file operations from skill code go through `WorkspaceSandbox.ResolveSafe` — no exceptions
- Non-infrastructure code uses `IOptions<AriaOptions>` only; never inject `IConfiguration` directly
- `ARIA.Core` must remain free of all NuGet dependencies (BCL only)
