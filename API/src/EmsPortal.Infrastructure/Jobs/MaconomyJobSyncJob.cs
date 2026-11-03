using global::Hangfire;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;

namespace EmsPortal.Infrastructure.Jobs;

/// <summary>
/// Hangfire job that posts one approved request's engagement to Maconomy as a job. One attempt, like the
/// customer sync's: the service records every outcome on the row and never throws for a failed post, and
/// Hangfire's own retry is switched off so a failure waits for Sync job now rather than being tried again
/// behind somebody's back. Resolved from DI by the Hangfire server.
/// </summary>
public sealed class MaconomyJobSyncJob
{
    private readonly IMaconomyJobSyncService _sync;

    public MaconomyJobSyncJob(IMaconomyJobSyncService sync) => _sync = sync;

    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid remsId, Guid tenantId, CancellationToken cancellationToken)
        => _sync.RunAsync(remsId, tenantId, cancellationToken);
}

/// <summary>Hangfire-backed <see cref="IMaconomyJobSyncQueue"/>: the enqueue the approval, or the customer sync, makes.</summary>
internal sealed class MaconomyJobSyncQueue : IMaconomyJobSyncQueue
{
    private readonly IBackgroundJobClient _backgroundJobs;

    public MaconomyJobSyncQueue(IBackgroundJobClient backgroundJobs) => _backgroundJobs = backgroundJobs;

    public void Enqueue(Guid remsId, Guid tenantId)
        => _backgroundJobs.Enqueue<MaconomyJobSyncJob>(job => job.RunAsync(remsId, tenantId, CancellationToken.None));
}
