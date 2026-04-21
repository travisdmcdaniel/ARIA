using ARIA.Core.Models;

namespace ARIA.Core.Interfaces;

public interface ISchedulerService
{
    Task ScheduleJobAsync(ScheduledJob job, CancellationToken ct = default);
    Task CancelJobAsync(string jobId, CancellationToken ct = default);
    Task LoadPersistedJobsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ScheduledJob>> GetActiveJobsAsync(CancellationToken ct = default);
}
