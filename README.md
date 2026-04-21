# ARIA — Autonomous Resident Intelligence Agent

A Windows-native background service that acts as a personal AI agent. ARIA runs silently in the background, is controlled via Telegram, and uses a locally hosted LLM through [Ollama](https://ollama.com) — keeping all inference and data on your own hardware.

## What it does

- **Telegram interface** — send messages, images, and commands from any device; only whitelisted user IDs can interact with the bot
- **Local LLM** — powered by Ollama with any compatible model; supports streaming, tool calling, and vision (when the model supports it)
- **Workspace file access** — the agent can read, write, and manage files within a sandboxed workspace directory
- **Skill system** — extend the agent by adding `SKILL.md` Markdown files under `workspace/skills/`; each file teaches the LLM how to perform a capability; the agent can write new skills itself
- **Scheduled tasks** — define recurring jobs in natural language; the agent runs them on schedule and sends results via Telegram
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
| `skills.directory` | `%USERPROFILE%\ARIAWorkspace\skills` | Directory scanned for `SKILL.md` files |

**Sensitive values** (bot token, Google client secret, OAuth tokens) are stored via Windows DPAPI — never in `config.json`.

## Bot commands

| Command | Description |
|---|---|
| `/new` | Start a fresh conversation session |
| `/sessions` | List recent sessions |
| `/resume [id]` | Resume a previous session |
| `/status` | Agent health, connected services, current model |
| `/connectgoogle` | Authorize Google access (opens browser) |
| `/disconnectgoogle` | Revoke stored Google tokens |
| `/jobs` | List active scheduled jobs |
| `/canceljob [id]` | Cancel a scheduled job |

## Skills

Skills are Markdown instruction files that tell the agent how to perform a capability. Each skill is a `SKILL.md` file inside its own subdirectory under `workspace/skills/`.

```
workspace/skills/
└── my-skill/
    └── SKILL.md
```

The `SKILL.md` contains natural-language instructions the LLM reads to understand how to carry out the capability — there is no executable code involved. Skills are injected into the agent's system prompt so it knows what it can do and how to do it.

The agent ships with a `create_new_skill` skill whose `SKILL.md` teaches it how to author new skills: create a subdirectory, write a `SKILL.md` describing the capability, then trigger a reload. New skills can also be added manually by dropping a folder into the skills directory and running `/reloadskills`.

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
ARIA.Scheduler     — Quartz.NET cron scheduler, job persistence
ARIA.Google        — OAuth 2.0 flow, Gmail and Calendar API wrappers
ARIA.Telegram      — Telegram bot, command registry, message routing
ARIA.Agent         — conversation loop, tool dispatch, onboarding wizard
ARIA.Service       — Windows service host, composition root
ARIA.TrayHost      — WinForms tray icon + WPF settings window
```

## Privacy

All LLM inference is local via Ollama — no data is sent to cloud AI services. Google API calls are user-authorized and scoped to the minimum permissions required. Context files and conversation history remain on-device.
