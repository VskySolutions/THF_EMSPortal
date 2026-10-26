using global::Hangfire;
using EmsPortal.Application.Abstractions.Integrations.Maconomy;

namespace EmsPortal.Infrastructure.Jobs;

/// <summary>
/// Hangfire job that posts one request's brand-new client to Maconomy. One attempt: the service records
/// every outcome on the row and never throws for a failed post, and Hangfire's own retry is switched
/// off so a failure waits for an admin's Sync now rather than being tried again behind their back.
/// Resolved from DI by the Hangfire server.
/// </summary>
public sealed class MaconomyCustomerSyncJob
{
    private readonly IMaconomyCustomerSyncService _sync;

    public MaconomyCustomerSyncJob(IMaconomyCustomerSyncService sync) => _sync = sync;

    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid syncId, Guid tenantId, CancellationToken cancellationToken)
        => _sync.RunAsync(syncId, tenantId, cancellationToken);
}

/// <summary>Hangfire-backed <see cref="IMaconomyCustomerSyncQueue"/>: the enqueue the approval makes.</summary>
internal sealed class MaconomyCustomerSyncQueue : IMaconomyCustomerSyncQueue
{
    private readonly IBackgroundJobClient _backgroundJobs;

    public MaconomyCustomerSyncQueue(IBackgroundJobClient backgroundJobs) => _backgroundJobs = backgroundJobs;

    public void Enqueue(Guid syncId, Guid tenantId)
        => _backgroundJobs.Enqueue<MaconomyCustomerSyncJob>(job => job.RunAsync(syncId, tenantId, CancellationToken.None));
}
