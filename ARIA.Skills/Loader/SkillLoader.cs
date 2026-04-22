using ARIA.Core.Models;
using ARIA.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ARIA.Skills.Loader;

public sealed class SkillLoader
{
    private readonly AriaOptions _options;
    private readonly ILogger<SkillLoader> _logger;

    public SkillLoader(IOptions<AriaOptions> options, ILogger<SkillLoader> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyList<SkillEntry> Load()
    {
        if (!_options.Skills.Enabled)
            return [];

        var workspaceRoot = _options.Workspace.GetResolvedRootPath();
        var skillsDirectory = _options.Skills.GetResolvedDirectory(workspaceRoot);

        if (!Directory.Exists(skillsDirectory))
            return [];

        var entries = new List<SkillEntry>();
        foreach (var directory in Directory.EnumerateDirectories(skillsDirectory))
        {
            var skillFile = Path.Combine(directory, "SKILL.md");
            if (!File.Exists(skillFile))
            {
                _logger.LogWarning("Skill directory {Directory} has no SKILL.md, skipping", directory);
                continue;
            }

            try
            {
                var content = File.ReadAllText(skillFile);
                var metadata = SkillFrontMatterParser.Parse(content);
                if (string.IsNullOrWhiteSpace(metadata.Name) ||
                    string.IsNullOrWhiteSpace(metadata.Description))
                {
                    _logger.LogWarning(
                        "Skill file {SkillFile} is missing required YAML front matter name or description, skipping",
                        skillFile);
                    continue;
                }

                entries.Add(new SkillEntry(
                    metadata.Name,
                    metadata.Description,
                    ToWorkspaceRelativePath(workspaceRoot, directory),
                    ToWorkspaceRelativePath(workspaceRoot, skillFile)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to load skill file {SkillFile}, skipping", skillFile);
            }
        }

        return entries
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ToWorkspaceRelativePath(string workspaceRoot, string path) =>
        Path.GetRelativePath(workspaceRoot, path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
}
