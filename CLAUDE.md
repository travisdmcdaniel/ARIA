# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ARIA (Autonomous Resident Intelligence Agent) is a Windows-native background service personal AI agent. It runs as a Windows service, is controlled via Telegram, uses a locally hosted LLM through Ollama, and is extensible via a skill/tool system. The tray UI is a separate WinForms executable that hosts a WPF settings window.

Target framework: **net10.0-windows**. All projects require Windows.

## Commands

```bash
# Build entire solution
dotnet build

# Run all tests
dotnet test

# Run tests for a specific project
dotnet test ARIA.Memory.Tests
dotnet test ARIA.Agent.Tests

# Run the service locally (development)
dotnet run --project ARIA.Service

# Publish for deployment
dotnet publish ARIA.Service -c Release
```

## Solution Structure & Dependency Rules

The dependency graph is a strict DAG — lower layers must not reference higher ones:

```
ARIA.Core          (no ARIA project dependencies — interfaces, models, constants, options only)
    ↑
ARIA.Memory        (Core) — SQLite store via Dapper; folders: Context/, Migrations/, Sqlite/
ARIA.LlmAdapter    (Core) — OllamaSharp; folders: Abstractions/, Ollama/
ARIA.Skills        (Core, Memory) — folders: BuiltIn/FileTools/, BuiltIn/CreateScheduledJob/, Loader/, Sandbox/
ARIA.Scheduler     (Core, Memory) — Quartz.NET + NCrontab; folders: Jobs/, Store/
ARIA.Google        (Core) — Google.Apis.*; folders: Auth/, Skills/
ARIA.Telegram      (Core) — Telegram.Bot; folders: Commands/, Handlers/
    ↑
ARIA.Agent         (all above) — conversation loop, tool dispatch, onboarding; folders: Conversation/, Prompts/, Onboarding/
    ↑
ARIA.Service       (Agent) — .NET Worker Service composition root; config, Serilog
ARIA.TrayHost      (Core only) — WinForms tray icon + WPF settings window; no agent logic
```

Test projects use xUnit + NSubstitute and mirror the source project they test.

## Architecture: Key Design Decisions

**Credentials:** The Telegram bot token, Google client secret, and OAuth tokens are stored via Windows DPAPI (`ProtectedData`) or the Windows Credential Locker — **never** in `config.json`. `config.json` holds only non-sensitive settings; sensitive values use `"USE_CREDENTIAL_STORE"` as a placeholder. Credential key constants live in `ARIA.Core/Constants/CredentialKeys.cs`.

**Workspace sandbox:** Every file operation performed by any skill must go through `WorkspaceSandbox.ResolveSafe()` in `ARIA.Skills/Sandbox/`. This normalizes the path and verifies it stays within the configured workspace root. Symlinks pointing outside the workspace are also blocked. Never pass raw LLM-provided paths to `File.*` methods directly.

**Context files:** Three Markdown files (`IDENTITY.md`, `SOUL.md`, `USER.md`) live in `workspace/context/` and are prepended to the system prompt on every LLM request. The agent can read and write them via built-in tools. A `FileSystemWatcher` invalidates the cache when any file changes. Monitor combined token count; warn the user if they approach the limit.

**Conversation loop (ARIA.Agent):** The `ConversationLoop` runs a tool-dispatch loop capped at `MaxToolCallIterations` (default 10) to prevent infinite cycles. Flow: inject context files → load recent history (N turns from SQLite) → call LLM → if tool calls returned, execute each via `ISkillRegistry`, append results, loop → persist assistant turn → return response text.

**LLM adapter:** `ILlmAdapter` in `ARIA.Core` abstracts all LLM calls. The Ollama implementation (`OllamaSharp`) queries model metadata to populate `LlmCapabilities` (supportsVision, supportsToolCalling, supportsStreaming). If `supportsVision` is false and the user sends an image, the agent must reply with an explanatory message rather than calling the adapter.

**Skills vs tools:** These are two distinct concepts. *Tools* are built-in callable capabilities registered with the LLM as function-calling functions (file operations, Google APIs, etc.) and dispatched by `IToolRegistry` / `IToolExecutor` in `ARIA.Skills`. *Skills* are plain Markdown files (`SKILL.md`) that live under `workspace/skills/<skill-name>/SKILL.md`. The agent reads these files to understand how to perform complex tasks — they are instructions for the LLM, not executable code. At startup, `SkillLoader` scans the skills directory, reads every `SKILL.md`, and makes the content available for injection into the system prompt. Creating a new skill means creating a new subdirectory and writing a `SKILL.md` to it; the `create_new_skill` skill is itself a `SKILL.md` that instructs the LLM how to do this.

**Scheduler:** Quartz.NET manages job scheduling. Persisted jobs (in SQLite) are loaded and registered with Quartz at startup via `ISchedulerService.LoadPersistedJobsAsync()`. When a job fires, it injects a synthetic user turn into `ConversationLoop` and sends the result via Telegram.

**Tray host pattern:** `ARIA.TrayHost` is a separate executable using WinForms `NotifyIcon` for the tray icon (WPF doesn't support NotifyIcon natively). The WPF `SettingsWindow` is opened on demand on the same STA thread. IPC between the tray host and the service uses a named pipe carrying `AgentStatusMessage` JSON; the tray host polls every 10 seconds to update the icon color.

**Options pattern:** All configuration is read via strongly-typed `AriaOptions` (and its nested sub-option classes) in `ARIA.Core/Options/`. Non-infrastructure code must use `IOptions<AriaOptions>` — never inject `IConfiguration` directly.

**Heartbeat:** When `heartbeat.enabled` is `true`, `HeartbeatWorker` fires every `heartbeat.intervalMinutes` minutes. It reads `workspace/context/HEARTBEAT.md`, passes it as a synthetic user turn into `ConversationLoop`, and sends the result to all authorized Telegram users. If `HEARTBEAT.md` does not exist the tick is silently skipped. The file is seeded during M9 onboarding. The heartbeat is distinct from the user-created scheduler jobs (M7) — it is a built-in, always-present background cycle controlled solely by config.

**Logging:** Use Serilog structured logging with message templates (not string interpolation). Never log message content, file contents, API tokens, OAuth tokens, or user IDs in privacy-sensitive contexts.

## Configuration

`config.json` lives at `%LOCALAPPDATA%\ARIA\config.json` (production) or `ARIA.Service/config.json` (development). Key paths use `%USERPROFILE%\ARIAWorkspace` as the workspace root by default. The `logging.level` field accepts Serilog level names (`Debug`, `Information`, etc.).

## Telegram Formatting

All messages sent to users via Telegram must use **MarkdownV2** (`ParseMode.MarkdownV2`). Never call `bot.SendMessage` directly — use the extension methods in `ARIA.Telegram/Helpers/BotClientExtensions.cs`:

- `bot.SendMarkdownAsync(chatId, text, ct)` — MarkdownV2 formatted
- `bot.SendTextAsync(chatId, text, ct)` — plain text, no formatting

All literal text and runtime values embedded in a MarkdownV2 string must be escaped via `Markdown.Escape()` from `ARIA.Telegram/Helpers/Markdown.cs`. Use the helper methods for intentional formatting — they escape the inner text automatically:

```csharp
// Runtime values always go through Escape()
$"Loaded {Markdown.Escape(skillName)} successfully\."

// Formatting helpers escape inner text for you
$"{Markdown.Bold("Error")}: {Markdown.Escape(ex.Message)}"

// Literal punctuation in the template must be hand-escaped
$"Status\: {Markdown.Escape(statusValue)}"
```

Available helpers: `Escape()`, `Bold()`, `Italic()`, `Code()`, `CodeBlock()`, `Link()`, `Strike()`.

MarkdownV2 special characters that require escaping in plain text: `_ * [ ] ( ) ~ \` > # + - = | { } . !`

## Configuration

`config.json` lives at `%LOCALAPPDATA%\ARIA\config.json` (production) or `ARIA.Service/config.json` (development). `config.development.json` in the same directory is layered on top and is gitignored — use it for local secrets (bot token, user IDs). Path values in config are resolved via `WorkspaceOptions` helpers: relative paths are combined with `rootPath`; absolute paths and `%ENV_VAR%` paths are used as-is after environment variable expansion.

## Current State

Phase 1 (M1 + M2) is complete. The service runs, connects to Telegram, enforces the user ID whitelist, persists the SQLite schema on first run, and handles `/new` and `/status`. Implementation follows the 12-milestone plan in `Plan.md`.
