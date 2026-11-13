using global::Hangfire;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;

namespace EmsPortal.Infrastructure.Jobs;

/// <summary>
/// Hangfire job that uploads one approved request's files to Maconomy. One attempt, like the two syncs
/// it follows: the service records every outcome on the file and never throws for a failed upload, and
/// Hangfire's own retry is switched off so a failure waits for somebody to upload the documents again.
/// </summary>
public sealed class MaconomyDocumentSyncJob
{
    private readonly IMaconomyDocumentSyncService _sync;

    public MaconomyDocumentSyncJob(IMaconomyDocumentSyncService sync) => _sync = sync;

    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid remsId, Guid tenantId, CancellationToken cancellationToken)
        => _sync.RunAsync(remsId, tenantId, cancellationToken);
}

/// <summary>Hangfire-backed <see cref="IMaconomyDocumentSyncQueue"/>: the enqueue the two syncs, or the approval, make.</summary>
internal sealed class MaconomyDocumentSyncQueue : IMaconomyDocumentSyncQueue
{
    private readonly IBackgroundJobClient _backgroundJobs;

    public MaconomyDocumentSyncQueue(IBackgroundJobClient backgroundJobs) => _backgroundJobs = backgroundJobs;

    public void Enqueue(Guid remsId, Guid tenantId)
        => _backgroundJobs.Enqueue<MaconomyDocumentSyncJob>(job => job.RunAsync(remsId, tenantId, CancellationToken.None));
}
