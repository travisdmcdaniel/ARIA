using System.Text;
using ARIA.Core.Interfaces;
using ARIA.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ARIA.Agent.Prompts;

public sealed class SystemPromptBuilder
{
    private readonly IContextFileStore _contextFileStore;
    private readonly ISkillStore _skillStore;
    private readonly AgentOptions _agentOptions;
    private readonly PersonalityOptions _personalityOptions;
    private readonly ILogger<SystemPromptBuilder> _logger;

    public SystemPromptBuilder(
        IContextFileStore contextFileStore,
        ISkillStore skillStore,
        IOptions<AriaOptions> options,
        ILogger<SystemPromptBuilder> logger)
    {
        _contextFileStore = contextFileStore;
        _skillStore = skillStore;
        _agentOptions = options.Value.Agent;
        _personalityOptions = options.Value.Personality;
        _logger = logger;
    }

    public async Task<string> BuildAsync(string sessionId, CancellationToken ct = default)
    {
        var sb = new StringBuilder();

        if (_personalityOptions.Identity.Enabled)
            await AppendContextFileAsync(sb, ContextFile.Identity, ct);

        if (_personalityOptions.Soul.Enabled)
            await AppendContextFileAsync(sb, ContextFile.Soul, ct);

        if (_personalityOptions.User.Enabled)
            await AppendContextFileAsync(sb, ContextFile.User, ct);

        var skills = _skillStore.GetAll();
        if (skills.Count > 0)
        {
            sb.AppendLine("## Available Skills");
            sb.AppendLine();
            foreach (var skill in skills)
            {
                sb.AppendLine($"### {skill.Name}");
                sb.AppendLine(skill.Content);
                sb.AppendLine();
            }
        }

        sb.AppendLine($"Current UTC time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Session ID: {sessionId}");

        var prompt = sb.ToString();

        var estimatedTokens = prompt.Length / 4;
        if (estimatedTokens > _agentOptions.ContextTokenBudget)
        {
            _logger.LogWarning(
                "System prompt estimated at {EstimatedTokens} tokens, exceeding budget of {Budget}",
                estimatedTokens,
                _agentOptions.ContextTokenBudget);
        }

        return prompt;
    }

    private async Task AppendContextFileAsync(StringBuilder sb, ContextFile file, CancellationToken ct)
    {
        try
        {
            var content = await _contextFileStore.ReadAsync(file, ct);
            if (!string.IsNullOrWhiteSpace(content))
            {
                sb.AppendLine(content);
                sb.AppendLine();
            }
        }
        catch (FileNotFoundException)
        {
            // Not yet seeded; skip silently
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read context file {ContextFile}", file);
        }
    }
}
