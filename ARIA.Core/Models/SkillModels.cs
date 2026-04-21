namespace ARIA.Core.Models;

/// <summary>Represents a loaded SKILL.md file from the skills directory.</summary>
public sealed record SkillEntry(
    string Name,
    string Directory,
    string Content);
