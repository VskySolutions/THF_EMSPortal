using System.Text.RegularExpressions;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Auditing;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using global::Hangfire;
using global::Hangfire.Common;
using global::Hangfire.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// The Background Jobs screen: what the job queue holds, with retry and clean-up. A Super Admin works on
/// every job; a Tenant Admin on the jobs queued for their own tenant. Reads Hangfire's own storage.
/// </summary>
[ApiController]
[Produces("application/json")]
[Tags("Background Jobs")]
[Authorize]
[RequirePermission(Permissions.JobsManage)]
public sealed class BackgroundJobsController : ControllerBase
{
    // How many jobs of one state a tenant-scoped read looks through; the queue is shared, so a Tenant
    // Admin's rows are picked out of it rather than paged in storage.
    private const int ScanCap = 2000;
    private const int ScanPage = 500;

    // Hangfire's state names, as constants so they can be matched on.
    private const string InQueued = "Enqueued";
    private const string InScheduled = "Scheduled";
    private const string InProcessing = "Processing";
    private const string InSucceeded = "Succeeded";
    private const string InFailed = "Failed";
    private const string InDeleted = "Deleted";

    private static readonly string[] States =
    {
        InQueued, InScheduled, InProcessing,
        InSucceeded, InFailed, InDeleted,
    };

    private readonly ITenantRepository _tenants;
    private readonly IAuditTrailService _audit;
    private readonly IUnitOfWork _unitOfWork;

    public BackgroundJobsController(ITenantRepository tenants, IAuditTrailService audit, IUnitOfWork unitOfWork)
    {
        _tenants = tenants;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    // Registered only where job storage is configured, so they are looked up rather than injected.
    private JobStorage? Storage => HttpContext.RequestServices.GetService<JobStorage>();
    private IBackgroundJobClient Jobs => HttpContext.RequestServices.GetRequiredService<IBackgroundJobClient>();

    [HttpGet("/api/admin/jobs")]
    [ProducesResponseType<ApiResponse<IEnumerable<BackgroundJobRow>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? state,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (Scope() is not { } scope)
        {
            return NoTenant();
        }
        if (Storage is not { } storage)
        {
            return NotSetUp();
        }

        var wanted = States.FirstOrDefault(s => string.Equals(s, state, StringComparison.OrdinalIgnoreCase)) ?? InQueued;
        page = Math.Max(page, 1);
        limit = Math.Clamp(limit, 1, 100);
        var monitor = storage.GetMonitoringApi();

        List<BackgroundJobRow> rows;
        long total;
        if (scope.All)
        {
            rows = Read(monitor, wanted, (page - 1) * limit, limit);
            total = Count(monitor, wanted);
        }
        else
        {
            var mine = Scan(monitor, wanted, scope);
            total = mine.Count;
            rows = mine.Skip((page - 1) * limit).Take(limit).ToList();
        }

        var names = (await _tenants.ListAsync(cancellationToken)).ToDictionary(t => t.Id, t => t.Name);
        rows = rows.Select(r => r with { TenantName = r.TenantId is { } id && names.TryGetValue(id, out var name) ? name : null }).ToList();
        return Ok(ApiResponseFactory.Paginated(rows, "Background jobs retrieved.", page, limit, (int)Math.Min(total, int.MaxValue)));
    }

    /// <summary>How many jobs sit in each state, for the tabs.</summary>
    [HttpGet("/api/admin/jobs/counts")]
    [ProducesResponseType<ApiResponse<BackgroundJobCounts>>(StatusCodes.Status200OK)]
    public IActionResult Counts()
    {
        if (Scope() is not { } scope)
        {
            return NoTenant();
        }
        if (Storage is not { } storage)
        {
            return NotSetUp();
        }

        var monitor = storage.GetMonitoringApi();
        long Of(string state) => scope.All ? Count(monitor, state) : Scan(monitor, state, scope).Count;
        return Ok(ApiResponseFactory.Success(new BackgroundJobCounts(
            Of(InQueued), Of(InScheduled), Of(InProcessing),
            Of(InSucceeded), Of(InFailed), Of(InDeleted)), "Job counts retrieved."));
    }

    /// <summary>Puts a failed or deleted job back in the queue.</summary>
    [HttpPost("/api/admin/jobs/{id}/retry")]
    public async Task<IActionResult> Retry(string id, CancellationToken cancellationToken)
    {
        var found = Find(id, out var failure);
        if (found is null)
        {
            return failure!;
        }
        if (found.State is not (InFailed or InDeleted))
        {
            return Conflict(Refused("Only a failed or deleted job can be retried."));
        }

        Jobs.Requeue(id);
        await AuditAsync(id, "JobRetried", found.Name, cancellationToken);
        return Ok(ApiResponseFactory.Success(new { id }, "Job queued again."));
    }

    /// <summary>Removes one job that has not run to a finish.</summary>
    [HttpDelete("/api/admin/jobs/{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var found = Find(id, out var failure);
        if (found is null)
        {
            return failure!;
        }
        if (found.State is not (InQueued or InScheduled or InFailed))
        {
            return Conflict(Refused("Only a queued, scheduled or failed job can be deleted."));
        }

        Jobs.Delete(id);
        await AuditAsync(id, "JobDeleted", found.Name, cancellationToken);
        return Ok(ApiResponseFactory.Success(new { id }, "Job deleted."));
    }

    /// <summary>Queues every failed job again.</summary>
    [HttpPost("/api/admin/jobs/retry-failed")]
    public async Task<IActionResult> RetryFailed(CancellationToken cancellationToken)
    {
        if (Scope() is not { } scope)
        {
            return NoTenant();
        }
        if (Storage is not { } storage)
        {
            return NotSetUp();
        }

        var failed = Scan(storage.GetMonitoringApi(), InFailed, scope);
        var done = failed.Count(job => Jobs.Requeue(job.Id, InFailed));
        await AuditAsync("failed", "JobsRetried", $"count={done}", cancellationToken);
        return Ok(ApiResponseFactory.Success(new { count = done }, done == 1 ? "1 job queued again." : $"{done} jobs queued again."));
    }

    /// <summary>Deletes everything still waiting to run: the queued jobs and the scheduled ones.</summary>
    [HttpPost("/api/admin/jobs/clear-queue")]
    public async Task<IActionResult> ClearQueue(CancellationToken cancellationToken)
    {
        if (Scope() is not { } scope)
        {
            return NoTenant();
        }
        if (Storage is not { } storage)
        {
            return NotSetUp();
        }

        var monitor = storage.GetMonitoringApi();
        var done = 0;
        // The from-state guard leaves alone a job a worker picked up in the meantime.
        foreach (var state in new[] { InQueued, InScheduled })
        {
            done += Scan(monitor, state, scope).Count(job => Jobs.Delete(job.Id, state));
        }
        await AuditAsync("queue", "JobQueueCleared", $"count={done}", cancellationToken);
        return Ok(ApiResponseFactory.Success(new { count = done }, done == 1 ? "1 job removed from the queue." : $"{done} jobs removed from the queue."));
    }

    /// <summary>The jobs that run on a timer. They belong to no tenant, so both admins see the same list.</summary>
    [HttpGet("/api/admin/jobs/recurring")]
    [ProducesResponseType<ApiResponse<IEnumerable<RecurringJobRow>>>(StatusCodes.Status200OK)]
    public IActionResult Recurring()
    {
        if (Storage is not { } storage)
        {
            return NotSetUp();
        }

        using var connection = storage.GetConnection();
        var rows = connection.GetRecurringJobs().Select(job => new RecurringJobRow(
            job.Id, Describe(job.Job).Name, job.Cron, Utc(job.LastExecution), Utc(job.NextExecution), job.LastJobState, job.Error));
        return Ok(ApiResponseFactory.Success(rows, "Recurring jobs retrieved."));
    }

    [HttpPost("/api/admin/jobs/recurring/{id}/run")]
    public async Task<IActionResult> RunRecurring(string id, CancellationToken cancellationToken)
    {
        if (Storage is not { } storage)
        {
            return NotSetUp();
        }

        using (var connection = storage.GetConnection())
        {
            if (connection.GetRecurringJobs().All(job => job.Id != id))
            {
                return NotFound(ApiResponseFactory.NotFound("Recurring job not found."));
            }
        }

        HttpContext.RequestServices.GetRequiredService<IRecurringJobManager>().Trigger(id);
        await AuditAsync(id, "RecurringJobTriggered", null, cancellationToken);
        return Ok(ApiResponseFactory.Success(new { id }, "Job started."));
    }

    // -------------------- Reading the queue --------------------

    /// <summary>Whose jobs the caller works on: every job for a Super Admin, one tenant's otherwise.</summary>
    private sealed record JobScope(bool All, Guid TenantId)
    {
        public bool Sees(BackgroundJobRow row) => All || row.TenantId == TenantId;
    }

    private JobScope? Scope()
        => User.IsSuperAdmin() ? new JobScope(true, Guid.Empty)
            : User.GetActiveTenantId() is { } tenantId ? new JobScope(false, tenantId) : null;

    private static List<BackgroundJobRow> Scan(IMonitoringApi monitor, string state, JobScope scope)
    {
        var all = new List<BackgroundJobRow>();
        for (var from = 0; from < ScanCap; from += ScanPage)
        {
            var batch = Read(monitor, state, from, ScanPage);
            all.AddRange(batch.Where(scope.Sees));
            if (batch.Count < ScanPage)
            {
                break;
            }
        }
        return all;
    }

    private static List<BackgroundJobRow> Read(IMonitoringApi monitor, string state, int from, int count) => state switch
    {
        InScheduled => monitor.ScheduledJobs(from, count)
            .Where(j => j.Value is not null).Select(j => Row(j.Key, state, j.Value.Job, j.Value.EnqueueAt, null)).ToList(),
        InProcessing => monitor.ProcessingJobs(from, count)
            .Where(j => j.Value is not null).Select(j => Row(j.Key, state, j.Value.Job, j.Value.StartedAt, null)).ToList(),
        InSucceeded => monitor.SucceededJobs(from, count)
            .Where(j => j.Value is not null).Select(j => Row(j.Key, state, j.Value.Job, j.Value.SucceededAt, null)).ToList(),
        InFailed => monitor.FailedJobs(from, count)
            .Where(j => j.Value is not null).Select(j => Row(j.Key, state, j.Value.Job, j.Value.FailedAt, j.Value.ExceptionMessage)).ToList(),
        InDeleted => monitor.DeletedJobs(from, count)
            .Where(j => j.Value is not null).Select(j => Row(j.Key, state, j.Value.Job, j.Value.DeletedAt, null)).ToList(),
        _ => ReadQueued(monitor, from, count),
    };

    // Queued jobs are kept per queue; the queues are read one after another as a single list.
    private static List<BackgroundJobRow> ReadQueued(IMonitoringApi monitor, int from, int count)
    {
        var rows = new List<BackgroundJobRow>();
        foreach (var queue in monitor.Queues())
        {
            if (rows.Count >= count)
            {
                break;
            }
            if (from >= queue.Length)
            {
                from -= (int)queue.Length;
                continue;
            }
            rows.AddRange(monitor.EnqueuedJobs(queue.Name, from, count - rows.Count)
                // A deleted job stays in its queue until a worker drains it; only the live ones are shown.
                .Where(j => j.Value is { InEnqueuedState: true })
                .Select(j => Row(j.Key, InQueued, j.Value.Job, j.Value.EnqueuedAt, null)));
            from = 0;
        }
        return rows;
    }

    // The rows actually held, not the lifetime totals the statistics keep.
    private static long Count(IMonitoringApi monitor, string state) => state switch
    {
        InScheduled => monitor.ScheduledCount(),
        InProcessing => monitor.ProcessingCount(),
        InSucceeded => monitor.SucceededListCount(),
        InFailed => monitor.FailedCount(),
        InDeleted => monitor.DeletedListCount(),
        _ => monitor.Queues().Sum(queue => queue.Length),
    };

    private static BackgroundJobRow Row(string id, string state, Job? job, DateTime? at, string? reason)
    {
        var (name, detail, tenantId) = Describe(job);
        return new BackgroundJobRow(id, name, detail, state, tenantId, null, Utc(at), reason);
    }

    /// <summary>One job by id, if the caller may touch it; otherwise the response to give instead.</summary>
    private BackgroundJobRow? Find(string id, out IActionResult? failure)
    {
        failure = null;
        if (Scope() is not { } scope)
        {
            failure = NoTenant();
            return null;
        }
        if (Storage is not { } storage)
        {
            failure = NotSetUp();
            return null;
        }

        var details = storage.GetMonitoringApi().JobDetails(id);
        // History is newest first: its head is the state the job is in now.
        var row = details is null ? null : Row(id, details.History.FirstOrDefault()?.StateName ?? string.Empty, details.Job, null, null);
        if (row is null || !scope.Sees(row))
        {
            failure = NotFound(ApiResponseFactory.NotFound("Job not found."));
            return null;
        }
        return row;
    }

    // -------------------- Saying what a job is --------------------

    /// <summary>A job in plain words, and the tenant it was queued for (its own tenantId argument).</summary>
    private static (string Name, string? Detail, Guid? TenantId) Describe(Job? job)
    {
        if (job is null)
        {
            return ("Unknown job", "Its code is no longer in the application.", null);
        }

        var parameters = job.Method.GetParameters();
        object? Arg(string name)
        {
            var index = Array.FindIndex(parameters, p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index < job.Args.Count ? job.Args[index] : null;
        }

        var tenantId = Arg("tenantId") as Guid?;
        return job.Type.Name switch
        {
            "EmailSendJob" => ("Send email", $"{Words(Arg("key")?.ToString())} to {Arg("toEmail") ?? "nobody"}", tenantId),
            "MaconomyCustomerSyncJob" => ("Maconomy client sync", $"Client {Arg("clientId")}", tenantId),
            "MaconomyJobSyncJob" => ("Maconomy job sync", $"Request {Arg("remsId")}", tenantId),
            "MaconomyDocumentSyncJob" => ("Maconomy document upload", $"Request {Arg("remsId")}", tenantId),
            "ReminderDispatchJob" => ("Reminder dispatch", null, tenantId),
            "StickyNoteExpiryJob" => ("Sticky note expiry", null, tenantId),
            _ => (Words(job.Type.Name), job.Method.Name, tenantId),
        };
    }

    // "RemsFormLink" → "Rems Form Link".
    private static string Words(string? pascal)
        => Regex.Replace(pascal ?? string.Empty, "(?<=[a-z0-9])(?=[A-Z])", " ");

    private static DateTime? Utc(DateTime? at) => at is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : null;

    private async Task AuditAsync(string id, string action, string? details, CancellationToken cancellationToken)
    {
        await _audit.AddAsync("BackgroundJob", id, action, details, cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static ApiErrorResponse Refused(string details) => ApiResponseFactory.Error("CONFLICT", "The request could not be completed.", details);

    private ObjectResult NoTenant()
        => StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller."));

    private ObjectResult NotSetUp()
        => StatusCode(StatusCodes.Status503ServiceUnavailable, Refused("Background jobs are not set up on this server."));
}

public sealed record BackgroundJobRow(
    string Id, string Name, string? Detail, string State, Guid? TenantId, string? TenantName, DateTime? AtUtc, string? Reason);

public sealed record BackgroundJobCounts(long Queued, long Scheduled, long Processing, long Succeeded, long Failed, long Deleted);

public sealed record RecurringJobRow(
    string Id, string Name, string Cron, DateTime? LastRunUtc, DateTime? NextRunUtc, string? LastState, string? Error);
