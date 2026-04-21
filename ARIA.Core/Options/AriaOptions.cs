namespace ARIA.Core.Options;

public sealed class AriaOptions
{
    public WorkspaceOptions Workspace { get; set; } = new();
    public OllamaOptions Ollama { get; set; } = new();
    public TelegramOptions Telegram { get; set; } = new();
    public GoogleOptions Google { get; set; } = new();
    public AgentOptions Agent { get; set; } = new();
    public SkillsOptions Skills { get; set; } = new();
    public SchedulerOptions Scheduler { get; set; } = new();
    public HeartbeatOptions Heartbeat { get; set; } = new();
    public PersonalityOptions Personality { get; set; } = new();
    public LoggingOptions Logging { get; set; } = new();
}

public sealed class WorkspaceOptions
{
    public string RootPath { get; set; } = string.Empty;
    public string ContextDirectory { get; set; } = "context";
    public string DatabasePath { get; set; } = "data/aria.db";

    /// <summary>
    /// Returns RootPath if set; otherwise the default %USERPROFILE%\ARIAWorkspace.
    /// Also expands environment variables.
    /// </summary>
    public string GetResolvedRootPath() =>
        string.IsNullOrWhiteSpace(RootPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "ARIAWorkspace")
            : Environment.ExpandEnvironmentVariables(RootPath);

    public string GetResolvedContextDirectory()
    {
        var expanded = Environment.ExpandEnvironmentVariables(ContextDirectory);
        return Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(GetResolvedRootPath(), expanded);
    }

    public string GetResolvedDatabasePath()
    {
        var expanded = Environment.ExpandEnvironmentVariables(DatabasePath);
        return Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(GetResolvedRootPath(), expanded);
    }
}

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "gemma4:e4b";

    /// <summary>
    /// Ordered list of model names to try if the primary Model is unavailable.
    /// Used by the Ollama adapter in M3.
    /// </summary>
    public string[] FallbackModels { get; set; } = [];
}

public sealed class TelegramOptions
{
    public string BotToken { get; set; } = "USE_CREDENTIAL_STORE";
    public long[] AuthorizedUserIds { get; set; } = [];
}

public sealed class GoogleOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = "USE_CREDENTIAL_STORE";
}

public sealed class AgentOptions
{
    public int MaxConversationTurns { get; set; } = 20;
    public int SessionRetentionDays { get; set; } = 30;
    public bool ContextFileWatchEnabled { get; set; } = true;
    public int MaxToolCallIterations { get; set; } = 10;
    public int ContextTokenBudget { get; set; } = 4000;
}

public sealed class SkillsOptions
{
    public bool Enabled { get; set; } = true;
    public string Directory { get; set; } = "skills";

    public string GetResolvedDirectory(string workspaceRoot)
    {
        var expanded = Environment.ExpandEnvironmentVariables(Directory);
        return Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(workspaceRoot, expanded);
    }
}

public sealed class SchedulerOptions
{
    public bool Enabled { get; set; } = true;
}

public sealed class HeartbeatOptions
{
    /// <summary>
    /// Whether the heartbeat cycle is active. When enabled, the agent reads
    /// HEARTBEAT.md from the context directory and runs it through the LLM
    /// at the configured interval, sending the result to all authorized users.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>How often the heartbeat fires, in minutes. Default: 30.</summary>
    public int IntervalMinutes { get; set; } = 30;
}

public sealed class PersonalityOptions
{
    public PersonalityFileOptions Soul { get; set; } = new();
    public PersonalityFileOptions Identity { get; set; } = new();
    public PersonalityFileOptions User { get; set; } = new();
    public PersonalityMemoryOptions Memory { get; set; } = new();
}

public sealed class PersonalityFileOptions
{
    public bool Enabled { get; set; } = true;
}

public sealed class PersonalityMemoryOptions
{
    public bool Enabled { get; set; } = true;
}

public sealed class LoggingOptions
{
    public string Level { get; set; } = "Information";
}
