# ARIA — Autonomous Resident Intelligence Agent
## Product Requirements Document
**Version 0.3 | DRAFT**
**April 2026**

---

## 1. Product Overview

ARIA (Autonomous Resident Intelligence Agent) is a Windows-native background service that acts as a personal AI agent for its owner. It runs silently in the background, is controllable via Telegram, and can interact with local files, Google services, and external tools — all powered by a locally hosted large language model through Ollama. ARIA is designed to be private-first: the user's data stays on-device by default, with no dependency on cloud AI APIs in version 1.

ARIA exposes its capabilities through a skill and tool system, allowing the agent to be extended over time. It ships as a self-contained Windows installer and includes a minimal system-tray UI for managing the service lifecycle.

ARIA's personality, identity, and user-specific knowledge are stored in a set of Markdown context files that the agent can read and update autonomously, giving it a persistent and evolving sense of self and user awareness. The agent's in-world name is chosen by the user during first-run onboarding and stored in IDENTITY.md; "ARIA" is the permanent project and product name.

> **Product Name:** ARIA — Autonomous Resident Intelligence Agent. The agent's in-world name is configurable by the user and set during the first-run onboarding wizard.

---

## 2. Goals & Non-Goals

### 2.1 Goals

- Ship a stable v1 of a Windows-native AI agent that runs as a background service
- Provide Telegram as the primary user interaction channel
- Use a locally hosted LLM via Ollama as the sole inference backend in v1
- Support a sandboxed file workspace with full read/write access inside that directory
- Implement a skill/tool invocation system so the agent can take real-world actions
- Support scheduled/proactive tasks in addition to reactive (user-initiated) interactions
- Maintain persistent conversation history via SQLite across service restarts, with session management
- Bundle a persona and memory system using Markdown context files (IDENTITY, SOUL, USER) that the agent can read and self-update
- Support image/vision inputs via Telegram in v1, conditioned on the active model's capabilities, using a capability flags system in the LLM adapter
- Bundle Google OAuth so the agent can access Gmail, Google Calendar, and other Google APIs, using per-user Google Cloud credentials
- Package everything as a standard Windows installer (.exe or .msi) with a system-tray WPF control UI and uninstaller
- Lay an architecture that can accommodate additional LLM providers (OpenAI-compatible) without core rewrites

### 2.2 Non-Goals (v1)

- Cloud-hosted AI inference — v1 is Ollama/local-only
- macOS or Linux support
- A full desktop GUI application (tray icon + minimal WPF controls only)
- A web-accessible interface (Telegram is the only remote interface in v1)
- Agent-to-agent communication or multi-agent orchestration
- Custom model fine-tuning or training
- Bundling a Google Cloud OAuth client — users supply their own Google Cloud project credentials
- A skill marketplace or remote skill registry

---

## 3. Target Users

v1 is a personal, multi-user-authorized product intended for a single machine. The intended primary user is a technically proficient individual who:

- Is comfortable installing and configuring desktop software
- Has an existing Telegram account
- Either already runs Ollama, or is willing to install it
- Has (or is willing to create) a Google Cloud project for OAuth credentials
- Wants a personal AI agent that respects their privacy and runs entirely on their own hardware

Additional Telegram users (e.g. household members) may be authorized, each interacting with the agent independently. Multi-user support is a first-class citizen in the authorization model, but context and memory are scoped per user for conversation history; the IDENTITY, SOUL, and USER context files are shared across all authorized users in v1.

---

## 4. System Architecture

ARIA is composed of seven major subsystems:

| Subsystem | Responsibility | Key Interfaces |
|---|---|---|
| Agent Core | Orchestrates the conversation loop, tool dispatch, context file injection, and scheduled task execution | Telegram handler, Skill registry, LLM adapter, Scheduler |
| LLM Adapter | Abstracts communication with the local Ollama instance; exposes capability flags (e.g. supportsVision, supportsToolCalling); designed for future provider swap-in | Ollama REST API (OpenAI-compatible) |
| Skill & Tool Engine | Loads, validates, and executes skills; manages tool definitions passed to the LLM; hosts the Create Skill and Create Scheduled Job built-ins | File system (skill manifests), LLM function-calling |
| Conversation & Memory Store | Persists conversation history per user/session in SQLite; manages session lifecycle; stores and serves context (IDENTITY, SOUL, USER) files | SQLite, file system |
| Scheduler | Manages cron-style and interval-based jobs; triggers agent turns on schedule without user input; persists jobs in SQLite | Agent Core, in-process timer |
| Google Integration | Handles OAuth 2.0 flow, token storage, and API calls to Gmail, Calendar, etc. | Google OAuth 2.0, Google REST APIs |
| Tray Host & Service Wrapper | Manages Windows service lifecycle, exposes tray icon, and hosts the WPF control UI | Windows Service API, system tray, WPF |

---

## 5. Functional Requirements

### 5.1 Telegram Bot Interface

The Telegram bot is the primary user-facing interface for ARIA in v1.

- ARIA shall maintain a persistent Telegram bot connection using a long-poll or webhook strategy
- The bot shall accept only messages from a configurable whitelist of Telegram user IDs; all other messages shall be silently ignored
- Multiple Telegram user IDs may be authorized; each is treated as an independent user with their own conversation history and session state
- Messages sent to the bot are routed to the Agent Core as user turns in a conversation
- The agent shall respond with text; image/file responses shall be supported where the Telegram API permits
- The Telegram message handler shall accept image attachments from users and pass them to the LLM adapter; if the active model's `supportsVision` flag is false, the agent shall notify the user that the current model does not support image input
- Bot token and authorized user IDs shall be stored in the application configuration (sensitive values in the Credential Store)
- If the bot is offline or unreachable, ARIA shall log the error and attempt reconnection with exponential backoff
- The following bot commands shall be supported as a minimum:

| Command | Description |
|---|---|
| `/new` | Start a fresh conversation session (previous session is archived, not deleted) |
| `/sessions` | List recent sessions for the current user |
| `/resume [id]` | Resume a previous session by ID |
| `/status` | Report agent health, connected services, and current model |
| `/connectgoogle` | Initiate Google OAuth flow |
| `/disconnectgoogle` | Revoke stored Google tokens |
| `/jobs` | List active scheduled jobs |
| `/canceljob [id]` | Cancel a scheduled job by ID |

### 5.2 LLM Adapter (Ollama / OpenAI-compatible)

- ARIA shall communicate with a locally running Ollama instance via its OpenAI-compatible REST API
- The model name and Ollama base URL shall be user-configurable
- The adapter shall support streaming responses and surface them progressively via Telegram where the API permits
- The adapter shall support function/tool calling in the OpenAI tool-use format; if the selected model does not natively support this, the adapter shall provide a prompt-engineering fallback
- The adapter interface shall be defined as an abstraction (interface/protocol) so that additional providers (OpenAI, Anthropic, LM Studio, etc.) can be swapped in without changes to the Agent Core
- The adapter shall expose a capability flags mechanism (e.g. `supportsVision`, `supportsToolCalling`) so the Agent Core can conditionally enable features based on the active model's capabilities; vision/image support shall be implemented in v1 using this mechanism
- Connection failure to Ollama shall produce a clear, user-visible error message via Telegram

### 5.3 Workspace & File Access

- ARIA shall operate with a designated root workspace folder, configurable at install time (default: `%USERPROFILE%\ARIAWorkspace`)
- All file read and write operations performed by the agent shall be sandboxed within this folder; attempts to traverse above the workspace root shall be rejected
- The agent shall expose file tools including: read file, write/overwrite file, append to file, list directory, create directory, delete file, and move/rename file
- File paths provided to tools shall be validated and normalized before execution; path traversal sequences (e.g. `../`) and symlinks pointing outside the workspace shall be blocked

### 5.4 Skill System

Skills are Markdown instruction files that extend what the agent knows how to do. Each skill is a `SKILL.md` file inside its own subdirectory under the workspace skills directory. Skills contain natural-language instructions the LLM reads to understand how to carry out a capability — there is no executable code, subprocess, or manifest involved.

- Each skill shall be a `SKILL.md` file stored at `workspace/skills/<skill-name>/SKILL.md`
- ARIA shall scan the skills directory at startup and load all `SKILL.md` files into an in-memory skill store
- Subdirectories that do not contain a `SKILL.md` shall be logged as a warning and skipped; they shall not crash the service
- The loaded skill instructions shall be injected into the system prompt on every LLM request, under an `## Available Skills` section, so the agent is always aware of its capabilities
- Skills shall support hot-reload: editing or adding a `SKILL.md` file shall be picked up automatically via a `FileSystemWatcher`, or manually via the `/reloadskills` bot command
- Skills shall be installable in two ways:
  - **Manually** — the user creates a subdirectory and writes a `SKILL.md` to it, then triggers `/reloadskills`
  - **Via agent** — the agent uses its built-in file tools (`create_directory`, `write_file`) to write a new `SKILL.md` based on instructions from the user (guided by the `create_new_skill` skill — see §5.4.1)

#### 5.4.1 Built-in: Create New Skill

ARIA shall ship with a `create_new_skill` skill — a `SKILL.md` that instructs the LLM how to author new skills autonomously.

- The `create_new_skill/SKILL.md` shall be seeded into the skills directory on first run
- When a user asks the agent to create a new skill, the agent follows the instructions in this file: it creates a new subdirectory under `workspace/skills/`, writes a `SKILL.md` describing the requested capability, and informs the user when done
- No confirmation step or manifest validation is required — the agent writes Markdown, not executable code
- The new skill is available immediately after the skill store reloads (automatic via file watcher)

### 5.5 Conversation History & Session Management

ARIA shall maintain persistent conversation history across service restarts using SQLite stored within the workspace.

- All conversations shall be stored in a SQLite database at a configurable path (default: `workspace/aria.db`)
- Each conversation record shall include at minimum: session ID, Telegram user ID, timestamp, role (user/assistant/tool), and message content
- Sessions are the unit of contextual continuity. A session begins when the user starts a conversation (or issues `/new`) and persists until the user explicitly starts a new one
- When building the LLM context window, ARIA shall load the most recent N turns from the active session (N = `agent.maxConversationTurns`, default 20), plus the injected context files (see §5.6)
- Previous sessions are archived and retrievable via `/sessions` and `/resume`; they are never deleted automatically
- Session data shall be associated with the Telegram user ID, so multiple authorized users maintain separate, isolated histories
- A configurable retention policy shall allow older sessions to be excluded from the `/sessions` listing after a set number of days (they remain in the database but are not surfaced unless searched)

### 5.6 Agent Identity & Context Files

ARIA's persona, behavioral traits, and user-specific knowledge are stored as Markdown files in a reserved directory within the workspace (default: `workspace/context/`). These files are injected into the system prompt on every LLM request and can be updated by the agent itself during normal operation and onboarding.

| File | Purpose & Contents |
|---|---|
| `IDENTITY.md` | Defines who the agent is: its name (chosen by the user at onboarding), role, backstory, and any fixed behavioral constraints. Seeded during install; the agent may append or revise as it evolves. |
| `SOUL.md` | Captures the agent's personality: communication style, tone, values, quirks, and preferences. The agent may edit this file to reflect how it has come to understand its own character through interaction. |
| `USER.md` | Stores what the agent knows about the owner: name, preferences, occupation, interests, recurring tasks, and any other facts gathered through conversation. The agent updates this file proactively as it learns more. |

- All three files shall be created (with default starter content) during the first-run setup wizard
- The agent's in-world name shall be the first thing collected during the onboarding wizard; it is written into IDENTITY.md and used by the agent in all subsequent interactions
- The agent shall be given explicit tool access to read and write these files as part of its built-in toolset
- During the first session, the agent shall conduct a lightweight onboarding conversation to populate USER.md with basic information about the owner
- The agent shall use information in USER.md to personalize responses without being prompted
- Context files shall be injected into the system prompt before conversation history; their combined token footprint shall be monitored and a warning surfaced if they approach the model's context limit
- Users may directly edit context files; the agent shall re-read them on the next conversation turn after a modification is detected (via file watcher)
- Context files are shared across all authorized Telegram users in v1

### 5.7 Scheduler & Proactive Tasks

ARIA shall support scheduled and trigger-based tasks that allow the agent to initiate actions without a user message.

- The scheduler shall support cron-style expressions and plain-English interval definitions (e.g. "every morning at 8am") for defining job timing
- Scheduled jobs shall be stored persistently in the SQLite database so they survive service restarts
- When a scheduled job fires, the Scheduler shall inject a synthetic user turn into the Agent Core with the job's defined prompt and route the response back to the appropriate Telegram user
- Jobs may be created by any authorized Telegram user; job creation is handled by the built-in Create Scheduled Job skill (see §5.7.1)
- Jobs shall be listable and cancellable via the `/jobs` and `/canceljob` bot commands
- A job execution log shall be maintained in SQLite, recording start time, completion time, success/failure, and any output
- If a job fails, the agent shall notify the originating Telegram user with a brief error summary

#### 5.7.1 Built-in: Create Scheduled Job

ARIA shall ship with a built-in "Create Scheduled Job" skill that parses natural-language job descriptions from the user into persisted, executable scheduled jobs.

- The skill shall accept a natural-language job description (e.g. "Every weekday at 9am, summarize my unread email and send it to me")
- The skill shall use the LLM to extract the schedule (converted to a cron expression) and the task prompt from the description
- The extracted schedule and prompt shall be presented to the user for confirmation before the job is persisted
- On confirmation, the job shall be written to the SQLite jobs table and registered with the Scheduler immediately (no restart required)
- The skill shall handle ambiguous timing descriptions by asking the user a clarifying question before proceeding

### 5.8 Google Integration & OAuth

ARIA bundles Google OAuth 2.0 support so the user can authorize the agent to act on their behalf. Each user must supply their own Google Cloud project credentials.

- ARIA shall implement the Google OAuth 2.0 Authorization Code flow with PKCE, using a loopback redirect URI (localhost)
- The initial authorization flow is triggered via the `/connectgoogle` bot command and opens the system default browser to the Google consent screen
- Users must supply their own Google Cloud project OAuth client ID and client secret; these are entered during first-run setup or via the settings UI and stored in the Windows Credential Store
- OAuth tokens (access token + refresh token) shall be stored securely using DPAPI or the Windows Credential Locker — never in plain text
- ARIA shall automatically refresh the access token before expiry; if refresh fails, the user is prompted via Telegram to re-authenticate
- Scopes shall be requested incrementally — only when a skill that requires them is first invoked — to minimize the initial permission footprint
- v1 shall ship with built-in support for: Gmail read and send, Google Calendar read and write
- The integration module shall be structured so that additional Google API scopes (Drive, Tasks, Contacts, etc.) can be added without architectural changes
- The user may revoke Google access via `/disconnectgoogle`, which deletes all stored tokens

### 5.9 Windows Service & Background Operation

- ARIA shall install and run as a Windows service (or user-mode autostart background process), starting automatically on user login
- The service shall be startable and stoppable via the tray control UI and via standard Windows service management (services.msc)
- All application logs shall be written to a rotating log file (default: `%LOCALAPPDATA%\ARIA\logs`) with configurable minimum log level
- The service shall handle unhandled exceptions gracefully, log them, and attempt a self-restart with exponential backoff delay

### 5.10 System Tray Icon & WPF Control UI

- When ARIA is running, a system-tray icon shall be visible in the Windows notification area
- The tray icon shall indicate agent status visually (green = running, amber = degraded/warning, red = stopped/error)
- Right-clicking the tray icon shall present a context menu with: current status, Disable (pause message processing), Enable (resume), Restart, Open Logs Folder, Settings, and Exit
- A minimal WPF settings window shall be accessible from the tray menu, providing:
  - Workspace path
  - Ollama base URL and model name
  - Telegram bot token and authorized user ID list
  - Google OAuth status (connected/disconnected) and client ID/secret entry
  - Current agent context file status (last modified dates for IDENTITY.md, SOUL.md, USER.md)
  - Scheduled jobs overview
- The WPF settings window shall use a clean, modern visual style; it need not match the OS native appearance but should feel professional and lightweight
- Note on implementation: WPF does not natively support `NotifyIcon`; the standard pattern is a hidden WinForms application that owns the tray icon and spawns the WPF window for settings
- Changes to settings shall require a service restart to take effect; the UI shall prompt the user accordingly

### 5.11 Installer & Uninstaller

- ARIA shall be distributed as a single Windows installer in `.exe` (Inno Setup) or `.msi` (WiX Toolset v4) format
- The installer shall: check for and optionally install prerequisites (.NET 8 runtime); prompt for workspace folder location; prompt for Telegram bot token and initial authorized user ID; register ARIA as a Windows service or user-mode autostart; install the tray host application; and create Start Menu shortcuts
- A first-run setup wizard (delivered via Telegram bot interaction after install) shall guide the user through:
  1. Choosing a name for the agent (written into IDENTITY.md)
  2. Confirming Ollama connectivity and selecting a model
  3. Entering Google Cloud OAuth credentials (optional, can be skipped)
  4. Reviewing and customizing IDENTITY.md and SOUL.md
  5. Completing USER.md onboarding via a short conversational interview
- Updates shall be applied by running a newer version of the installer over the existing installation; the installer shall detect the existing install, stop the service, upgrade files, and restart the service without requiring uninstallation first
- A full uninstaller shall be included, accessible via Windows Settings > Apps and the Start Menu; it shall remove the service, all installed files, and Start Menu entries; workspace data and configuration shall be preserved by default with an opt-in deletion prompt

---

## 6. Non-Functional Requirements

| Category | Requirement |
|---|---|
| **Performance** | Response latency is bounded by local model inference speed. ARIA itself (excluding LLM) should add no more than 200ms overhead per turn. |
| **Security** | No credentials or tokens stored in plain text. Workspace sandbox strictly enforced. Telegram bot accepts messages only from whitelisted user IDs. |
| **Reliability** | Service auto-restarts on crash with backoff. Telegram reconnects automatically. Scheduled jobs are durable across restarts. Errors surfaced to user via Telegram when actionable. |
| **Privacy** | All inference is local; no user data is transmitted to third-party AI services in v1. Google API calls are user-authorized and scoped to minimum required permissions. Context files remain on-device. |
| **Extensibility** | LLM adapter, skill system, and Google scopes all designed for extension without core rewrites. |
| **Installability** | Installer must work on Windows 10 (20H2+) and Windows 11 without requiring admin rights beyond service registration. |
| **Resource Usage** | The ARIA process itself (excluding Ollama) should idle at under 100MB RAM and negligible CPU when not processing a request or scheduled job. |
| **Multimodal (Vision)** | The Telegram message handler shall accept image attachments and pass them to the LLM adapter. The adapter shall include them in the request payload when the active model's `supportsVision` flag is true; if false, the agent shall notify the user that the current model does not support image input. |

---

## 7. Recommended Technology Stack

### 7.1 Primary Recommendation: C# / .NET 8

C# with .NET 8 is the strongest fit for this project:

- Windows service hosting is first-class via the .NET Generic Host and Worker Service model
- WPF is natively supported in .NET 8 on Windows and produces a clean, modern settings UI with relatively low effort
- DPAPI / Windows Credential Store access is built into the .NET BCL
- Excellent HTTP client support for Ollama REST, Telegram Bot API, and Google APIs
- Google APIs .NET client library is mature and well-maintained
- SQLite is well-supported via `Microsoft.Data.Sqlite` or Dapper
- Installers can be built with WiX Toolset v4 (MSI) or Inno Setup

### 7.2 Key Library Recommendations

| Concern | Library / Approach |
|---|---|
| Windows Service | .NET 8 Worker Service + `IHostedService` |
| System Tray | WinForms `NotifyIcon` (tray icon only) — hosts the tray and spawns the WPF settings window |
| Settings UI | WPF (XAML) with `CommunityToolkit.Mvvm` for MVVM bindings |
| Telegram Bot | `Telegram.Bot` NuGet package |
| Ollama / LLM | Direct `HttpClient` calls to Ollama OpenAI-compatible endpoint, or `OllamaSharp` |
| Google OAuth & APIs | `Google.Apis.Auth` + `Google.Apis.Gmail.v1` + `Google.Apis.Calendar.v3` |
| Credential Storage | `Windows.Security.Credentials` (Credential Locker) or `System.Security.Cryptography.ProtectedData` (DPAPI) |
| SQLite | `Microsoft.Data.Sqlite` + Dapper |
| Scheduler | Quartz.NET (full-featured cron) or NCrontab with a hosted timer |
| Installer | WiX Toolset v4 (MSI) or Inno Setup (.exe) |
| Logging | Serilog with rolling file sink |
| Configuration | `Microsoft.Extensions.Configuration` with JSON file provider |
| Skill Files | `System.IO` file reading + `FileSystemWatcher` for hot-reload |

---

## 8. Configuration Schema

ARIA's configuration lives in a JSON file at `%LOCALAPPDATA%\ARIA\config.json`. Sensitive values (tokens, secrets) are stored separately in the Windows Credential Store and referenced by key name only in `config.json`.

| Key | Description |
|---|---|
| `workspace.rootPath` | Absolute path to the agent's sandboxed workspace folder |
| `workspace.contextDirectory` | Path to context files directory (default: `{workspace}/context`) |
| `workspace.databasePath` | Path to SQLite database file (default: `{workspace}/aria.db`) |
| `ollama.baseUrl` | Base URL of the Ollama instance (default: `http://localhost:11434`) |
| `ollama.model` | Model name to use for inference (e.g. `llama3`, `mistral`) |
| `telegram.botToken` | Telegram bot token — stored in Credential Store, placeholder here |
| `telegram.authorizedUserIds` | Array of Telegram user ID integers permitted to interact with the agent |
| `google.clientId` | Google OAuth client ID — supplied by the user from their own Google Cloud project |
| `google.clientSecret` | Google OAuth client secret — stored in Credential Store |
| `agent.maxConversationTurns` | Maximum number of turns to load from the active session into context (default: 20) |
| `agent.contextFileWatchEnabled` | Whether to watch context files for changes and hot-reload them (default: `true`) |
| `skills.directory` | Path to skills folder (default: `{workspace}/skills`); each subdirectory containing a `SKILL.md` is loaded as a skill |
| `scheduler.enabled` | Whether the scheduler subsystem is active (default: `true`) |
| `logging.level` | Minimum log level: `Verbose`, `Debug`, `Information`, `Warning`, `Error` |

---

## 9. Skill File Specification

Each skill lives in its own subdirectory under the skills folder and contains a single `SKILL.md` file. There are no binaries, scripts, or manifest files — skills are pure Markdown.

```
workspace/skills/
├── create_new_skill/
│   └── SKILL.md
├── morning_briefing/
│   └── SKILL.md
└── draft_email/
    └── SKILL.md
```

### 9.1 SKILL.md Structure

A `SKILL.md` file should contain enough information for the LLM to carry out the capability without additional context. Recommended sections:

```markdown
# <Skill Name>

## Purpose
One or two sentences describing what this skill does.

## Instructions
Step-by-step guidance for how the agent should carry out this capability.
Reference specific tools by name where relevant (e.g. `write_file`, `search_gmail`).

## Example
An example of a user request that would invoke this skill, and how the agent should respond.
```

There is no enforced schema — the format is a convention to make skills readable and effective. The agent loads the entire file content and includes it in the system prompt.

### 9.2 create_new_skill

The `create_new_skill` skill is seeded on first run and teaches the agent how to author new skills:

```markdown
# Create New Skill

## Purpose
Create a new skill by writing a SKILL.md file into the skills directory.

## Instructions
1. Ask the user what capability they want to add, if not already clear.
2. Use `create_directory` to create `workspace/skills/<skill-name>/`.
3. Use `write_file` to write `workspace/skills/<skill-name>/SKILL.md` with:
   - A # heading with the skill name
   - A ## Purpose section
   - A ## Instructions section with enough detail for future use
   - A ## Example section if helpful
4. Inform the user the skill has been created and will be available immediately.

## Example
User: "Create a skill for drafting professional emails."
Agent: Creates `workspace/skills/draft_email/SKILL.md` with instructions on
tone, structure, and how to use write_file or send_email when ready.
```

---

## 10. Context File Specification

Context files are Markdown documents stored in `workspace/context/`. They are prepended to the system prompt on every LLM request and are read/write accessible to the agent via built-in tools.

### 10.1 IDENTITY.md — Who the Agent Is

Seeded during install. The agent's in-world name is the first thing collected during the onboarding wizard and written here. Contains the name, role description, origin story, and any fixed behavioral constraints. Example starter content (name is replaced during onboarding):

```markdown
# Identity

My name is [name]. I am an Autonomous Resident Intelligence Agent running on
this machine. I exist to help my owner with tasks, answer questions, manage
their digital life, and act autonomously on their behalf when instructed.

I am private-first: I run entirely on local hardware and do not share
information with external AI services.
```

### 10.2 SOUL.md — Personality & Style

Defines the agent's tone, communication style, values, and character traits. The agent may refine this file over time as it develops a more consistent voice. Example:

```markdown
# Soul

I am direct, thoughtful, and concise. I do not pad my responses with
unnecessary pleasantries. I am honest about uncertainty. I have a dry
sense of humour that I deploy sparingly. I prefer to act rather than ask
for permission for low-risk tasks.
```

### 10.3 USER.md — What the Agent Knows About the Owner

Populated during onboarding and continuously updated as the agent learns more. Contains factual information about the user: name, occupation, preferences, recurring tasks, and anything else that helps the agent be more useful. Example:

```markdown
# User Profile

**Name:** Alex
**Occupation:** Software developer
**Preferred language:** English
**Timezone:** America/New_York

## Preferences
- Prefers concise responses unless detail is explicitly requested
- Dislikes unsolicited suggestions to consult a professional
- Morning briefing preferred at 8:30am on weekdays

## Recurring Tasks
- Weekly team meeting every Monday at 10am
- Review inbox summary each morning
```

Context files shall not exceed a configurable combined size limit (default: 4,000 tokens). If the limit is approached, the agent shall notify the user and suggest summarisation or archiving of older content.

---

## 11. Security Model

### 11.1 Telegram Authorization

Only messages from Telegram user IDs in the authorized list shall be processed. All other messages shall be silently ignored. This prevents the bot from being usable by anyone who discovers the bot name or token.

### 11.2 Workspace Sandbox

Before executing any file operation, ARIA shall resolve the target path to a canonical absolute path and verify it starts with the workspace root. This check must occur after full path normalization to prevent bypass via symlinks, `../` sequences, or Windows device paths.

### 11.3 Credential Storage

The Telegram bot token, Google OAuth client secret, OAuth access and refresh tokens, and any future API keys shall be stored using Windows DPAPI (`System.Security.Cryptography.ProtectedData`) or the Windows Credential Locker. They shall never appear in `config.json`, log files, or the SQLite database in clear text.

### 11.4 Subprocess Skills

Skills that spawn subprocesses shall be invoked with restricted process permissions. The subprocess shall have filesystem access limited to the workspace directory. A configurable allowlist of permitted skill entry-point file extensions shall be validated at skill load time; manifests referencing disallowed entry points shall be rejected.

### 11.5 Multi-User Isolation

Each authorized Telegram user has an isolated conversation history and session state. Context files (IDENTITY.md, SOUL.md, USER.md) are shared across all authorized users in v1. If per-user context files become a requirement in a future version, the file structure shall support per-user subdirectories without breaking the existing layout.

---

## 12. Suggested Development Milestones

| Milestone | Deliverable | Key Acceptance Criteria |
|---|---|---|
| **M1: Foundation** | Windows service skeleton + tray icon + WPF settings stub + config system + SQLite init | Service installs, starts, shows tray icon; reads config; logs to file; SQLite database created on first run |
| **M2: Telegram Loop** | Bot connects, receives messages, sends text replies; `/status` and `/new` commands work | Authorized user can send a message and get a static echo reply; `/new` starts a fresh session |
| **M3: LLM Integration** | Ollama adapter wired into conversation loop with context file injection and capability flags | Agent answers free-text questions using local model; context files injected; multi-turn context retained per session; image inputs passed through when model supports vision |
| **M4: Conversation Persistence** | SQLite-backed history; session archive and resume via `/sessions` and `/resume` | History survives service restart; `/new` archives session; `/resume` restores it |
| **M5: Workspace Tools** | File read/write/list/delete/move tools; sandbox enforcement | Agent can create, read, list, and delete files in workspace; path traversal attempts are rejected |
| **M6: Skill Engine + Create Skill** | Manifest loader, tool dispatch, subprocess execution, Create Skill built-in | Sample skill loaded from disk; LLM invokes it; Create Skill generates and installs a new skill on request |
| **M7: Scheduler + Create Scheduled Job** | Cron/interval job creation, persistence, firing, and Telegram notification; Create Scheduled Job built-in | User can define a job via natural language; job is confirmed, persisted, and fires on schedule; `/jobs` lists it; `/canceljob` removes it |
| **M8: Google OAuth** | OAuth flow, token storage, Gmail + Calendar skills | User authorizes via `/connectgoogle`; agent reads inbox and calendar events; token refresh works silently |
| **M9: Onboarding & Context Files** | First-run wizard, agent name collection, IDENTITY/SOUL/USER seeding, USER.md update tooling | Fresh install walks through onboarding; agent name set and written to IDENTITY.md; USER.md populated via conversational interview; context injected correctly; agent self-updates USER.md during conversation |
| **M10: Installer** | WiX/Inno Setup installer + upgrade support + uninstaller | Clean install on fresh Windows machine; re-running newer installer upgrades in-place; uninstall removes all traces |
| **M11: WPF Settings UI** | Full settings window with all configuration fields and OAuth status | All settings editable via UI; Google OAuth status shown; service restart prompted on change |
| **M12: Polish & Hardening** | Error handling, reconnect logic, context file watcher, documentation, resource limits | Agent recovers from Ollama restart and Telegram disconnect; context file edits hot-reloaded; idle memory under 100MB |

---

## 13. Open Questions

One question remains open for clarification before M9 development begins:

**Per-user context files** — IDENTITY.md, SOUL.md, and USER.md are currently shared across all authorized Telegram users. If a second authorized user interacts with the agent, they will share the same USER.md. This is acceptable for v1 (e.g. a sole primary user), but if secondary users should have at minimum their own USER.md, this should be resolved before the onboarding implementation in M9. The file structure is designed to accommodate per-user subdirectories in a future version without breaking the existing layout.

---

*End of Document — ARIA PRD v0.3 DRAFT*

*Confidential — Internal Use Only*
