using ARIA.Core.Models;

namespace ARIA.Core.Interfaces;

public interface ISchedulerService
{
    Task ScheduleJobAsync(ScheduledJob job, CancellationToken ct = default);
    Task DisableJobAsync(string jobIdOrFileName, CancellationToken ct = default);
    Task CancelJobAsync(string jobIdOrFileName, CancellationToken ct = default);
    Task LoadJobFilesAsync(CancellationToken ct = default);
    Task ReloadJobsAsync(CancellationToken ct = default);
    Task LoadPersistedJobsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ScheduledJob>> GetJobsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ScheduledJob>> GetActiveJobsAsync(CancellationToken ct = default);
}
