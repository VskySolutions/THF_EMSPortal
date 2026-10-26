using global::Hangfire;
using EmsPortal.Infrastructure.Jobs;
using Microsoft.Extensions.Hosting;

namespace EmsPortal.Infrastructure.Hangfire;

/// <summary>
/// Registers the Universal Features recurring background jobs on startup with fixed cron schedules:
/// platform-fixed sweeps for reminder dispatch (every minute) and sticky-note expiry (hourly).
/// </summary>
public sealed class UniversalFeaturesRecurringJobs : IHostedService
{
    private readonly IRecurringJobManager _recurringJobs;

    public UniversalFeaturesRecurringJobs(IRecurringJobManager recurringJobs) => _recurringJobs = recurringJobs;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Reminder dispatch: every minute.
        _recurringJobs.AddOrUpdate<ReminderDispatchJob>(
            ReminderDispatchJob.Name, job => job.RunAsync(CancellationToken.None), "* * * * *");

        // Sticky-note expiry: hourly (soft-deletes expired tenant notes).
        _recurringJobs.AddOrUpdate<StickyNoteExpiryJob>(
            StickyNoteExpiryJob.Name, job => job.RunAsync(CancellationToken.None), "0 * * * *");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
