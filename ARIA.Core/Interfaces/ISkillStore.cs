using ARIA.Core.Models;

namespace ARIA.Core.Interfaces;

public interface ISkillStore
{
    IReadOnlyList<SkillEntry> GetAll();
    Task ReloadAsync(CancellationToken ct = default);
}
