using ARIA.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ARIA.Skills.Loader;

public sealed class SkillSeeder(IOptions<AriaOptions> options, ILogger<SkillSeeder> logger)
{
    private readonly AriaOptions _options = options.Value;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_options.Skills.Enabled)
            return;

        var workspaceRoot = _options.Workspace.GetResolvedRootPath();
        var skillsDirectory = _options.Skills.GetResolvedDirectory(workspaceRoot);

        await SeedSkillAsync(
            skillsDirectory,
            "create_new_skill",
            CreateNewSkillContent,
            ct);

        await SeedSkillAsync(
            skillsDirectory,
            "onboarding",
            OnboardingSkillContent,
            ct);

        await SeedSkillAsync(
            skillsDirectory,
            "create_scheduled_job",
            CreateScheduledJobSkillContent,
            ct);
    }

    private async Task SeedSkillAsync(
        string skillsDirectory,
        string skillName,
        string content,
        CancellationToken ct)
    {
        var skillDirectory = Path.Combine(skillsDirectory, skillName);
        var skillFile = Path.Combine(skillDirectory, "SKILL.md");
        Directory.CreateDirectory(skillDirectory);

        if (File.Exists(skillFile))
        {
            var existing = await File.ReadAllTextAsync(skillFile, ct);
            if (string.Equals(existing, content, StringComparison.Ordinal))
                return;
        }

        await File.WriteAllTextAsync(skillFile, content, ct);
        logger.LogInformation("Seeded skill {SkillName} at {SkillFile}", skillName, skillFile);
    }

    private const string CreateNewSkillContent = """
        ---
        name: create_new_skill
        description: Create or update Markdown skills in the workspace skills directory.
        ---

        # Create New Skill

        ## Purpose
        Create or update a skill by writing a SKILL.md file into the workspace skills directory.

        ## Instructions
        1. Clarify the capability the user wants if their request is ambiguous.
        2. Choose a lowercase, filesystem-safe skill directory name under `skills/`.
        3. Use `create_directory` to create `skills/<skill-name>/`.
        4. Use `write_file` to write `skills/<skill-name>/SKILL.md`.
        5. The file must begin with YAML front matter bracketed by `---` lines and include `name` and `description`.
        6. After the front matter, write Markdown instructions detailed enough for future use. Include purpose, workflow, expected inputs, tool usage, and examples when useful.
        7. Ask the user to run `/reloadskills`, or run the reload command if command execution is available through the conversation.

        ## Example Front Matter
        ```yaml
        ---
        name: Draft Professional Email
        description: Draft polished professional emails from bullet points or rough notes.
        ---
        ```
        """;

    private const string OnboardingSkillContent = """
        ---
        name: onboarding
        description: Conduct the first-run interview and populate IDENTITY.md, SOUL.md, and USER.md.
        ---

        # Onboarding

        ## Purpose
        Guide the user through initial setup so ARIA has useful identity, communication, and user context.

        ## Instructions
        This is a multi-turn workflow. Continue using this skill whenever the recent conversation shows onboarding is in progress.

        Do not collect all answers and wait until the end to save them. Persist useful information as soon as the user provides it.

        1. If this is the first onboarding turn, greet the user and explain that onboarding will configure the agent's identity, communication style, and user profile.
        2. Ask one question at a time. Start by asking for the agent's in-world name.
        3. When the user answers with the agent's in-world name, immediately call `write_context_file` with:
           - `path`: `IDENTITY.md`
           - `content`: Markdown describing the agent's chosen name, role, and any identity details already known.
           Then ask the next onboarding question.
        4. Ask concise questions about the user: name, occupation or role, timezone, communication preferences, recurring tasks, and important constraints.
        5. After each user answer, immediately update `USER.md` with `write_context_file`. Preserve useful existing details when rewriting the file.
        6. Ask whether the user wants to adjust the agent's communication style.
        7. After the user answers about communication style, immediately update `SOUL.md` with `write_context_file`.
        8. Confirm completion only after `IDENTITY.md`, `USER.md`, and `SOUL.md` have been written or intentionally left unchanged with the user's consent.
        9. If a tool call fails, tell the user what failed and try the write again with corrected arguments.

        ## Tool Arguments
        Use `write_context_file` with JSON arguments like:

        ```json
        {
          "path": "USER.md",
          "content": "# User\n\n..."
        }
        ```

        ## Context File Guidance
        - `IDENTITY.md` should describe who the agent is, including its chosen name and role.
        - `SOUL.md` should describe communication style and behavioral preferences.
        - `USER.md` should contain factual information about the user and recurring needs.
        """;

    private const string CreateScheduledJobSkillContent = """
        ---
        name: create_scheduled_job
        description: Create or update scheduled job JSON files in the workspace jobs directory.
        ---

        # Create Scheduled Job

        ## Purpose
        Convert a user's natural-language recurring task request into a scheduled job JSON file.

        ## Instructions
        1. Clarify ambiguous timing before writing a job file. Ask for the timezone if it is not already clear from USER.md or the user's request.
        2. Convert the schedule to a cron expression. For M7, `schedule.kind` must always be `cron`.
        3. Choose a concise job name. The `create_scheduled_job` tool will derive the filename from it in kebab case under `jobs/`.
        4. Call the `create_scheduled_job` tool with exactly this shape:

        ```json
        {
          "name": "Daily Briefing",
          "schedule": { "kind": "cron", "expr": "30 7 * * *", "tz": "America/New_York" },
          "payload": { "kind": "agentTurn", "message": "Run the daily briefing skill, and send the results to the user." },
          "sessionTarget": "isolated",
          "enabled": true
        }
        ```

        5. `payload.message` is the message that will be sent to the model when the job fires. Make it explicit and self-contained.
        6. Use `sessionTarget: "isolated"` unless the user specifically asks for the job to run in the active conversation.
        7. Do not call `write_file` directly unless `create_scheduled_job` is unavailable. The tool writes the JSON file, and the scheduler will pick it up through its file watcher or periodic reload.
        8. Tell the user the filename that was written and ask them to review it if the task is important.

        ## Disable Rules
        A job is disabled if `enabled` is `false`, if the filename starts with `_`, or if the filename starts with `disabled`.
        """;
}
