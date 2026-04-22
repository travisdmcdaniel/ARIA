# ARIA — Autonomous Resident Intelligence Agent

A Windows-native background service that acts as a personal AI agent. ARIA runs silently in the background, is controlled via Telegram, and uses a locally hosted LLM through [Ollama](https://ollama.com) — keeping all inference and data on your own hardware.

## What it does

- **Telegram interface** — send messages, images, and commands from any device; only whitelisted user IDs can interact with the bot
- **Local LLM** — powered by Ollama with any compatible model; supports streaming, tool calling, and vision (when the model supports it)
- **Workspace file access** — the agent can read, write, and manage files within a sandboxed workspace directory
- **Skill system** — extend the agent by adding `SKILL.md` Markdown files under `workspace/skills/`; each file has YAML front matter for discovery and body instructions the LLM can read on demand; the agent can write new skills itself
- **Scheduled tasks** — define recurring jobs as simple JSON files under `workspace/jobs/`, or ask the agent to create them; the agent runs them on schedule and sends results via Telegram
- **Google integration** — authorize access to Gmail and Google Calendar via OAuth; token refresh is automatic
- **Persistent memory** — conversation history stored in SQLite, survives restarts; sessions can be archived and resumed
- **Context files** — `IDENTITY.md`, `SOUL.md`, and `USER.md` define the agent's persona and knowledge about you; the agent reads and updates them during normal use
- **System tray UI** — tray icon shows live agent status (green/amber/red); right-click for controls and settings

## Prerequisites

- Windows 10 (20H2+) or Windows 11
- [.NET 10 runtime](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com) running locally with at least one model pulled (e.g. `ollama pull gemma4:e4b`)
- A Telegram bot token (create one via [@BotFather](https://t.me/BotFather))
- Your Telegram user ID (the bot will only respond to IDs you authorize)

## Configuration

Configuration lives at `%LOCALAPPDATA%\ARIA\config.json`. The file is created on first run with defaults. Key settings:

| Key | Default | Description |
|---|---|---|
| `workspace.rootPath` | `%USERPROFILE%\ARIAWorkspace` | Sandboxed workspace directory |
| `ollama.baseUrl` | `http://localhost:11434` | Ollama instance URL |
| `ollama.model` | `gemma4:e4b` | Model name to use |
| `telegram.authorizedUserIds` | `[]` | Telegram user IDs permitted to interact |
| `agent.maxConversationTurns` | `20` | Turns loaded from history per request |
| `retention.databaseDays` | `90` | Maximum age for SQLite conversation turns, job execution log rows, and stale job mirror rows |
| `personality.identity.enabled` | `true` | Include `IDENTITY.md` in the system prompt |
| `personality.soul.enabled` | `true` | Include `SOUL.md` in the system prompt |
| `personality.user.enabled` | `true` | Include `USER.md` in the system prompt |
| `personality.memory.enabled` | `true` | Include recent conversation history in LLM context |
| `skills.enabled` | `true` | Enable skill metadata loading and prompt injection |
| `skills.directory` | `%USERPROFILE%\ARIAWorkspace\skills` | Directory scanned for valid `SKILL.md` files |
| `scheduler.enabled` | `true` | Enable scheduled jobs loaded from `%USERPROFILE%\ARIAWorkspace\jobs` |
| `scheduler.runMissedJobsAsap` | `true` | Run missed scheduled jobs at the next opportunity after ARIA restarts or is re-enabled |

**Sensitive values** (bot token, Google client secret, OAuth tokens) are stored via Windows DPAPI — never in `config.json`.

## Bot commands

| Command | Description |
|---|---|
| `/new` | Start a fresh conversation session |
| `/sessions` | List recent sessions |
| `/resume [id]` | Resume a previous session |
| `/status` | Agent health, connected services, current model |
| `/onboarding` | Run the onboarding interview to populate IDENTITY.md, SOUL.md, and USER.md |
| `/connectgoogle` | Authorize Google access (opens browser) |
| `/disconnectgoogle` | Revoke stored Google tokens |
| `/jobs` | List scheduled jobs loaded from workspace job files |
| `/canceljob [id]` | Disable a scheduled job |

## Skills

Skills are Markdown instruction files that tell the agent how to perform a capability. Each skill is a `SKILL.md` file inside its own subdirectory under `workspace/skills/`.

```
workspace/skills/
└── my-skill/
    └── SKILL.md
```

Each `SKILL.md` starts with YAML front matter bracketed by `---` lines:

```markdown
---
name: My Skill
description: One or two sentences describing when to use this skill.
---
```

The system prompt includes each skill's name, description, and `SKILL.md` path, not the full instruction body. When the LLM decides a skill is relevant, it uses file tools to read that `SKILL.md` and then follows the instructions in the Markdown body. There is no executable code involved.

The agent ships with a `create_new_skill` skill whose `SKILL.md` teaches it how to author new skills: create a subdirectory, write a `SKILL.md` with required YAML front matter and body instructions, then trigger a reload. New skills can also be added manually by dropping a folder into the skills directory and running `/reloadskills`.

## Scheduled Jobs

Scheduled jobs live as JSON files in `workspace/jobs/`. Each file defines one job, and these files are the source of truth for what jobs exist, when they run, and whether they are enabled. SQLite may mirror loaded jobs and stores execution history, but ARIA rebuilds scheduler state from the job files on startup and after edits.

Job filenames use the job name in kebab case. For example, `Daily Briefing` is stored as `workspace/jobs/daily-briefing.json`:

```json
{
  "name": "Daily Briefing",
  "schedule": { "kind": "cron", "expr": "30 7 * * *", "tz": "America/New_York" },
  "payload": { "kind": "agentTurn", "message": "Run the daily briefing skill, and send the results to the user." },
  "sessionTarget": "isolated",
  "enabled": true
}
```

For v1, recurring jobs use `schedule.kind: "cron"`. `schedule.expr` is a cron expression, `schedule.tz` is an IANA time zone, and `payload.message` is the synthetic user message sent to the model with the normal system prompt. `sessionTarget` is either `isolated` for a separate scheduled turn or `main` for the active session. A job is disabled when `enabled` is `false`, when the filename starts with `_`, or when the filename starts with `disabled`.

If `scheduler.runMissedJobsAsap` is true, ARIA runs missed scheduled jobs at the next available opportunity after the service restarts or the agent is re-enabled from the tray icon. If several fire times were missed for the same job, ARIA runs that job only once. If different jobs were missed, ARIA queues them and runs them one at a time instead of sending concurrent scheduled turns to the agent.

## Data Retention

SQLite stores conversation turns, scheduler mirror rows, and job execution history for operational use. ARIA automatically prunes `conversation_turns`, `job_execution_log`, and stale `scheduled_jobs` mirror rows so they do not retain more than 90 days of data. After old turns are pruned, any `sessions` row with no remaining conversation turns is deleted. Job JSON files in `workspace/jobs/` are not deleted by database retention cleanup.

## Development

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run a specific test project
dotnet test ARIA.Agent.Tests

# Run the service locally
dotnet run --project ARIA.Service
```

See `CLAUDE.md` for architecture details and `Plan.md` for the implementation roadmap.

## Architecture

ARIA is a multi-project .NET solution. Projects are layered with strict dependency rules (no circular references):

```
ARIA.Core          — interfaces, models, options (no external NuGet dependencies)
ARIA.Memory        — SQLite conversation store, context file store
ARIA.LlmAdapter    — Ollama adapter (OllamaSharp)
ARIA.Skills        — file tools, external skill loader, Create Skill built-in
ARIA.Scheduler     — Quartz.NET cron scheduler, workspace job file loader, execution logging
ARIA.Google        — OAuth 2.0 flow, Gmail and Calendar API wrappers
ARIA.Telegram      — Telegram bot, command registry, message routing
ARIA.Agent         — conversation loop, tool dispatch, onboarding wizard
ARIA.Service       — Windows service host, composition root
ARIA.TrayHost      — WinForms tray icon + WPF settings window
```

## Privacy

All LLM inference is local via Ollama — no data is sent to cloud AI services. Google API calls are user-authorized and scoped to the minimum permissions required. Context files and conversation history remain on-device.
