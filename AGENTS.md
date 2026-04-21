# Repository Guidelines

## Project Structure & Module Organization

ARIA is a multi-project .NET solution rooted at `ARIA.slnx`. Production code lives in `ARIA.*` projects, with matching test projects named `ARIA.*.Tests`.

- `ARIA.Core`: shared interfaces, models, constants, options, and exceptions.
- `ARIA.Service`: Windows service host and dependency-injection composition root.
- `ARIA.Telegram`: Telegram command registry, handlers, helpers, and bot commands.
- `ARIA.Agent`, `ARIA.LlmAdapter`, `ARIA.Memory`, `ARIA.Skills`, `ARIA.Scheduler`, `ARIA.Google`: feature modules.
- `ARIA.TrayHost`: Windows tray UI and settings surfaces.

Keep module dependencies layered and avoid circular project references. Put new tests beside the corresponding module in its `.Tests` project.

## Build, Test, and Development Commands

The Codex shell for this workspace runs under WSL, but this project targets Windows .NET and the local WSL environment cannot run `dotnet` reliably. Do **not** run `dotnet`, `dotnet.exe`, or .NET test/build/run commands from Codex. Instead, when verification is needed, ask the user to run the appropriate command from Windows PowerShell at the repository root:

```powershell
dotnet build
dotnet test
dotnet test ARIA.Agent.Tests
dotnet run --project ARIA.Service
```

`dotnet build` compiles the full solution. `dotnet test` runs all test projects. Target a single test project when iterating on one module. `dotnet run --project ARIA.Service` starts the service locally for integration checks. In Codex responses, report the exact PowerShell command the user should run and clearly state that Codex did not run it locally.

## Coding Style & Naming Conventions

Projects target `net10.0-windows` with nullable reference types and implicit usings enabled. Use standard C# conventions: four-space indentation, PascalCase for public types and members, camelCase for locals and parameters, and `_camelCase` for private fields when fields are needed.

Prefer small, focused classes aligned to the existing folder layout. Register cross-module services in `ARIA.Service/Program.cs`; keep module implementation details inside their owning project. Do not store secrets in source files or config examples.

## Testing Guidelines

Tests use xUnit, FluentAssertions, NSubstitute where needed, and `coverlet.collector`. Add tests for new behavior and regressions, especially around command routing, storage, scheduling, and configuration parsing.

Name test files after the subject under test, for example `CommandRegistryTests.cs`. Prefer descriptive test method names such as `TryHandleAsync_ReturnsFalse_ForUnknownCommand`. Ask the user to run `dotnet test` from Windows PowerShell before submitting changes; Codex should not attempt to run it from WSL.

## Commit & Pull Request Guidelines

Existing commits use short imperative summaries, for example `Add pause flag, heartbeat and tray controls`. Keep commits focused and explain behavior changes in the body when the summary is not enough.

Pull requests should include a brief description, testing performed, configuration or migration notes, and linked issues when applicable. Include screenshots only for `ARIA.TrayHost` UI changes.

## Security & Configuration Tips

Runtime configuration lives under `%LOCALAPPDATA%\ARIA\config.json`; sensitive values should use the Windows DPAPI credential store. Logs are written under `%LOCALAPPDATA%\ARIA\logs`. Avoid committing local workspace data, Visual Studio state, credentials, database files, or generated `bin`/`obj` artifacts.
