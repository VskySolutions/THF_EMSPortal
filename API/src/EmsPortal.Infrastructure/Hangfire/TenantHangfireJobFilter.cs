using global::Hangfire;
using global::Hangfire.Client;
using global::Hangfire.Server;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Infrastructure.Logging;
using Serilog.Context;

namespace EmsPortal.Infrastructure.Hangfire;

/// <summary>A point-in-time snapshot of the active tenant captured at job-enqueue time.</summary>
public readonly record struct TenantSnapshot(Guid TenantId, string Identifier);

/// <summary>
/// Propagates the active tenant across the API → Background Worker boundary (Multi-Tenancy
/// ADR-002). On enqueue (client side, API) it captures the current tenant —
/// supplied by <c>clientTenantProvider</c> so Infrastructure stays free of any HTTP
/// dependency — into the job payload. On execution (server side, Worker) it reconstructs
/// <see cref="ITenantContext"/> from the payload before the job method runs, so all
/// downstream data access is tenant-scoped.
/// </summary>
public sealed class TenantHangfireJobFilter : IClientFilter, IServerFilter
{
    public const string TenantIdParameter = "TenantId";
    public const string TenantIdentifierParameter = "TenantIdentifier";
    private const string LogScopeItem = "TenantLogScope";

    private readonly Func<TenantSnapshot?>? _clientTenantProvider;

    /// <param name="clientTenantProvider">
    /// Returns the current tenant at enqueue time, or null when none is resolved. Pass
    /// null on the Worker (server-only) side.
    /// </param>
    public TenantHangfireJobFilter(Func<TenantSnapshot?>? clientTenantProvider = null)
    {
        _clientTenantProvider = clientTenantProvider;
    }

    public void OnCreating(CreatingContext context)
    {
        var snapshot = _clientTenantProvider?.Invoke();
        if (snapshot is { TenantId: var tenantId } snap && tenantId != Guid.Empty)
        {
            context.SetJobParameter(TenantIdParameter, snap.TenantId);
            context.SetJobParameter(TenantIdentifierParameter, snap.Identifier);
        }
    }

    public void OnCreated(CreatedContext context)
    {
    }

    public void OnPerforming(PerformingContext context)
    {
        var tenantId = context.GetJobParameter<Guid>(TenantIdParameter);
        if (tenantId == Guid.Empty)
        {
            return;
        }

        var identifier = context.GetJobParameter<string>(TenantIdentifierParameter) ?? string.Empty;

        // Resolve the tenant context from the job's activation scope and populate it
        // before the handler executes.
        if (JobActivatorScope.Current?.Resolve(typeof(ITenantContext)) is ITenantContext tenantContext)
        {
            tenantContext.Set(tenantId, identifier);
        }

        // What the job logs is filed under its tenant.
        context.Items[LogScopeItem] = LogContext.PushProperty(SerilogConfigurator.TenantIdProperty, tenantId);
    }

    public void OnPerformed(PerformedContext context)
    {
        if (context.Items.TryGetValue(LogScopeItem, out var scope))
        {
            (scope as IDisposable)?.Dispose();
        }
    }
}
