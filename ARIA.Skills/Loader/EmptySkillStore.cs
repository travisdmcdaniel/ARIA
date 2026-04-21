using ARIA.Core.Interfaces;
using ARIA.Core.Models;

namespace ARIA.Skills.Loader;

/// <summary>No-op skill store used until M6 wires the real SKILL.md loader.</summary>
public sealed class EmptySkillStore : ISkillStore
{
    public IReadOnlyList<SkillEntry> GetAll() => [];
    public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;
}
