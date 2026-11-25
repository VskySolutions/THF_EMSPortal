namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>One row of the application log (dbo.Logs, written by the logger: errors only).</summary>
public sealed class ApplicationLogEntry
{
    public int Id { get; set; }
    public Guid? TenantId { get; set; }
    public string? Level { get; set; }
    public DateTime? TimeStamp { get; set; }
    public string? Message { get; set; }
    public string? Exception { get; set; }
    public string? Properties { get; set; }
}

/// <summary>What the Error Log list is narrowed by; every part optional.</summary>
public sealed record ApplicationLogFilter(
    string? Search = null,
    string? Level = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

/// <summary>Read access to the application log, always scoped to one tenant.</summary>
public interface IApplicationLogRepository
{
    /// <summary>
    /// Newest first, messages cut short and without the exception or properties.
    /// <paramref name="includeSystem"/> adds the entries that belong to no tenant.
    /// </summary>
    Task<(IReadOnlyList<ApplicationLogEntry> Rows, int Total)> ListAsync(
        Guid tenantId, bool includeSystem, ApplicationLogFilter filter, int page, int limit, CancellationToken cancellationToken = default);

    Task<ApplicationLogEntry?> GetAsync(Guid tenantId, bool includeSystem, int id, CancellationToken cancellationToken = default);
}
