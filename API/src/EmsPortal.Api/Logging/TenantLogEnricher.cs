using EmsPortal.Api.Security;
using EmsPortal.Infrastructure.Logging;
using Serilog.Core;
using Serilog.Events;

namespace EmsPortal.Api.Logging;

/// <summary>
/// Files every entry written during a signed-in request under the caller's active tenant. Read off the
/// principal, so it still holds in the exception handler, after the inner middleware has unwound.
/// </summary>
public sealed class TenantLogEnricher : ILogEventEnricher
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantLogEnricher(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (_httpContextAccessor.HttpContext?.User.GetActiveTenantId() is { } tenantId && tenantId != Guid.Empty)
        {
            logEvent.AddPropertyIfAbsent(new LogEventProperty(SerilogConfigurator.TenantIdProperty, new ScalarValue(tenantId)));
        }
    }
}
