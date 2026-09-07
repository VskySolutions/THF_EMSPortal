using EmsPortal.Api.Models;
using EmsPortal.Api.Models.Tenants;
using EmsPortal.Api.Security;
using EmsPortal.Application.Abstractions.Auditing;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Application.OptionSets;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Contracts;
using EmsPortal.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace EmsPortal.Api.Controllers;

/// <summary>
/// Tenant management (WO-40): tenant lifecycle — create, update, status, and archive (Super Admin)
/// — plus tenant detail reads.
/// </summary>
[ApiController]
[Route("/api/admin/tenants")]
[Produces("application/json")]
[Tags("Tenants")]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiErrorResponse>(StatusCodes.Status500InternalServerError)]
public sealed class TenantsController : ControllerBase
{
    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly IOptionSetRepository _optionSets;
    private readonly IAuditTrailService _audit;
    private readonly IUnitOfWork _unitOfWork;

    public TenantsController(
        ITenantRepository tenants,
        IUserRepository users,
        IOptionSetRepository optionSets,
        IAuditTrailService audit,
        IUnitOfWork unitOfWork)
    {
        _tenants = tenants;
        _users = users;
        _optionSets = optionSets;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    // ---- Tenant lifecycle (Super Admin) ----

    [HttpPost]
    [RequirePermission(Permissions.TenantsWrite)]
    [ProducesResponseType<ApiResponse<TenantResponse>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest request, CancellationToken cancellationToken)
    {
        if (await _tenants.IdentifierExistsAsync(request.Identifier, cancellationToken))
        {
            return Conflict(ApiResponseFactory.Error(ApiErrorCodes.DuplicateIdentifier, "Identifier already in use.", request.Identifier));
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Identifier = request.Identifier,
            TimeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId) ? "UTC" : request.TimeZoneId,
            Status = TenantStatus.Active,
            CreatedDate = DateTime.UtcNow,
        };
        await _tenants.AddAsync(tenant, cancellationToken);

        // The new tenant gets its OWN copy of the platform's default option lists, so its admins can manage
        // the values (add / rename / delete / re-order) without touching the shared originals.
        await TenantOptionSetSeeder.EnsureDefaultsAsync(_optionSets, tenant.Id, cancellationToken);

        await _audit.AddAsync(nameof(Tenant), tenant.Id.ToString(), "Created", details: tenant.Identifier, cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created,
            ApiResponseFactory.Success(new TenantResponse(tenant.Id, tenant.Identifier, tenant.Status.ToString()), "Tenant created."));
    }

    /// <summary>What the Tenants list may be ordered by.</summary>
    private static readonly SortMap<Tenant> Sorts = new SortMap<Tenant>("updatedOnUtc")
        .Add("name", t => t.Name)
        .Add("identifier", t => t.Identifier)
        .Add("status", t => t.Status, t => t.UpdatedOnUtc)
        .Add("timeZoneId", t => t.TimeZoneId)
        .Add("createdOnUtc", t => t.CreatedOnUtc)
        .Add("updatedOnUtc", t => t.UpdatedOnUtc);

    [HttpGet]
    [RequirePermission(Permissions.TenantsWrite)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] bool includeArchived = false,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var all = await _tenants.ListAsync(cancellationToken);
        IEnumerable<Tenant> filteredSet = includeArchived ? all : all.Where(t => t.Status != TenantStatus.Archived);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TenantStatus>(status, ignoreCase: true, out var statusFilter))
        {
            filteredSet = filteredSet.Where(t => t.Status == statusFilter);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filteredSet = filteredSet.Where(t =>
                t.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                t.Identifier.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        // Ordered before it is paged. This list is small enough to be read whole and filtered in memory,
        // but "page 1" still means the first rows OF AN ORDER, so the order has to be settled first.
        var filtered = Sorts.Apply(filteredSet, sortBy, descending).ToList();
        var pageTenants = filtered.Skip((page - 1) * limit).Take(limit).ToList();
        var names = await ResolveActorNamesAsync(pageTenants.SelectMany(t => new[] { t.CreatedById, t.UpdatedById }), cancellationToken);
        var pageItems = pageTenants.Select(t => new TenantSummary(
            t.Id, t.Name, t.Identifier, t.Status.ToString(), t.TimeZoneId,
            NameOf(names, t.CreatedById), NameOf(names, t.UpdatedById), t.CreatedOnUtc, t.UpdatedOnUtc));

        return Ok(ApiResponseFactory.Paginated(pageItems, "Tenants retrieved.", page, limit, filtered.Count));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.TenantsWrite)]
    [ProducesResponseType<ApiResponse<TenantDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var tenant = await _tenants.GetByIdAsync(id, cancellationToken);
        if (tenant is null)
        {
            return NotFound(ApiResponseFactory.Error(ApiErrorCodes.TenantNotFound, "Tenant not found.", id.ToString()));
        }

        var detail = new TenantDetail(
            tenant.Id, tenant.Name, tenant.Identifier, tenant.Status.ToString(), tenant.TimeZoneId,
            await RecordAudit.ForAsync(_users, tenant, cancellationToken));

        return Ok(ApiResponseFactory.Success(detail, "Tenant retrieved."));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.TenantsWrite)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenantRequest request, CancellationToken cancellationToken)
    {
        var tenant = await _tenants.GetByIdAsync(id, cancellationToken);
        if (tenant is null)
        {
            return NotFound(ApiResponseFactory.Error(ApiErrorCodes.TenantNotFound, "Tenant not found.", id.ToString()));
        }

        tenant.Name = request.Name; // identifier is immutable
        if (!string.IsNullOrWhiteSpace(request.TimeZoneId))
        {
            tenant.TimeZoneId = request.TimeZoneId;
        }
        _tenants.Update(tenant);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(
            new TenantResponse(tenant.Id, tenant.Identifier, tenant.Status.ToString()), "Tenant updated."));
    }

    [HttpPut("{id:guid}/status")]
    [RequirePermission(Permissions.TenantsWrite)]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] UpdateTenantStatusRequest request, CancellationToken cancellationToken)
    {
        var tenant = await _tenants.GetByIdAsync(id, cancellationToken);
        if (tenant is null)
        {
            return NotFound(ApiResponseFactory.Error(ApiErrorCodes.TenantNotFound, "Tenant not found.", id.ToString()));
        }

        tenant.Status = request.IsActive ? TenantStatus.Active : TenantStatus.Inactive;
        _tenants.Update(tenant);
        await _audit.AddAsync(nameof(Tenant), tenant.Id.ToString(), request.IsActive ? "Activated" : "Deactivated", cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(new { tenantId = tenant.Id, status = tenant.Status.ToString() }, "Status updated."));
    }

    [HttpPut("{id:guid}/archive")]
    [RequirePermission(Permissions.TenantsArchive)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        var tenant = await _tenants.GetByIdAsync(id, cancellationToken);
        if (tenant is null)
        {
            return NotFound(ApiResponseFactory.Error(ApiErrorCodes.TenantNotFound, "Tenant not found.", id.ToString()));
        }

        tenant.Status = TenantStatus.Archived;
        _tenants.Update(tenant);
        await _audit.AddAsync(nameof(Tenant), tenant.Id.ToString(), "Archived", cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponseFactory.Success(new { tenantId = tenant.Id, status = tenant.Status.ToString() }, "Tenant archived."));
    }

    // ---- helpers ----

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveActorNamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
        => await _users.GetFullNamesAsync(ids.Where(id => id.HasValue).Select(id => id!.Value), cancellationToken);

    private static string? NameOf(IReadOnlyDictionary<Guid, string> names, Guid? id)
        => id.HasValue && names.TryGetValue(id.Value, out var name) ? name : null;
}
