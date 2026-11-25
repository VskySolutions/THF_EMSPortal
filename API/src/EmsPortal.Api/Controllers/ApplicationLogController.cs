using System.Xml.Linq;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// The Error Log: what the application logged as an error, read-only. A Tenant Admin reads their own
/// tenant's entries; a Super Admin reads the tenant they are working in plus the entries no tenant owns.
/// </summary>
[ApiController]
[Produces("application/json")]
[Tags("Error Log")]
[Authorize]
[RequirePermission(Permissions.LogsRead)]
public sealed class ApplicationLogController : ControllerBase
{
    private readonly IApplicationLogRepository _logs;

    public ApplicationLogController(IApplicationLogRepository logs)
    {
        _logs = logs;
    }

    [HttpGet("/api/admin/logs")]
    [ProducesResponseType<ApiResponse<IEnumerable<ApplicationLogRow>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? level,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (User.GetActiveTenantId() is not { } tenantId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponseFactory.Forbidden("No active tenant for the caller."));
        }

        // "to" is a day picked on a calendar: the whole of it is meant.
        var filter = new ApplicationLogFilter(search, level, from, to?.Date.AddDays(1));
        var (rows, total) = await _logs.ListAsync(tenantId, User.IsSuperAdmin(), filter, page, limit, cancellationToken);
        return Ok(ApiResponseFactory.Paginated(rows.Select(ToRow), "Error log retrieved.", page, limit, total));
    }

    [HttpGet("/api/admin/logs/{id:int}")]
    [ProducesResponseType<ApiResponse<ApplicationLogDetail>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var log = User.GetActiveTenantId() is { } tenantId
            ? await _logs.GetAsync(tenantId, User.IsSuperAdmin(), id, cancellationToken)
            : null;
        if (log is null)
        {
            return NotFound(ApiResponseFactory.NotFound("Log entry not found."));
        }
        return Ok(ApiResponseFactory.Success(
            new ApplicationLogDetail(ToRow(log), log.Exception, ReadProperties(log.Properties)), "Log entry retrieved."));
    }

    private static ApplicationLogRow ToRow(ApplicationLogEntry log) => new(
        log.Id,
        log.Level ?? string.Empty,
        log.Message ?? string.Empty,
        log.TimeStamp is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null,
        log.TenantId is null);

    // The logger writes <properties><property key='…'>value</property>…</properties>.
    private static IReadOnlyList<ApplicationLogProperty> ReadProperties(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }
        try
        {
            return XDocument.Parse(xml).Root?.Elements("property")
                .Select(p => new ApplicationLogProperty(p.Attribute("key")?.Value ?? string.Empty, p.Value))
                .Where(p => p.Name.Length > 0)
                .ToList() ?? [];
        }
        catch (System.Xml.XmlException)
        {
            return [new ApplicationLogProperty("Properties", xml)];
        }
    }
}

public sealed record ApplicationLogRow(int Id, string Level, string Message, DateTime? LoggedOnUtc, bool IsSystem);

public sealed record ApplicationLogProperty(string Name, string Value);

public sealed record ApplicationLogDetail(ApplicationLogRow Log, string? Exception, IReadOnlyList<ApplicationLogProperty> Properties);
