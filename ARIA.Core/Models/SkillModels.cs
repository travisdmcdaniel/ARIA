namespace ARIA.Core.Models;

/// <summary>Represents metadata loaded from a SKILL.md file.</summary>
public sealed record SkillEntry(
    string Name,
    string Description,
    string Directory,
    string Path);
