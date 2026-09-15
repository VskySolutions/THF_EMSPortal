using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class PermissionGroupRepository : IPermissionGroupRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public PermissionGroupRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // ---- Groups ----

    // What the Permission Groups list may be ordered by. Roles Using and Members are counted in separate
    // batched queries after this one (CountRolesUsingGroupAsync, CountActiveMembersForGroupsAsync); the
    // entries here count the same rows as a subquery, so the order agrees with the number shown. Created
    // By / Updated By order by the ActorNames subquery. Category is not a value on the row at all — the
    // page carries it only as a filter — so it is not here.
    private SortMap<PermissionGroup> BuildSorts()
    {
        var actors = ActorNames.Of(_dbContext);
        // Filters may be off on the list query (a Super Admin's spans tenants), so the membership scope is
        // spelled out here, as CountActiveMembersForGroupsAsync spells it.
        var memberships = _dbContext.UserTenantRoles.Where(m => !m.Deleted && m.User!.IsActive && !m.User.Deleted);
        return new SortMap<PermissionGroup>("updatedOnUtc")
            .Add("name", g => g.Name)
            .Add("description", g => g.Description, g => g.Name)
            .Add("permissionCount", g => g.Permissions.Count, g => g.Name)
            .Add("rolesUsingCount", g => g.RoleLinks.Count, g => g.Name)
            .Add(
                "members",
                g => memberships
                    .Where(m => m.TenantId == g.TenantId && g.RoleLinks.Any(l => l.RoleId == m.RoleId))
                    .Select(m => m.UserId).Distinct().Count(),
                g => g.Name)
            .Add("status", g => g.IsActive, g => g.Name)
            .Add("tenantName", g => g.Tenant!.Name, g => g.Name)
            .Add("createdBy", g => actors.Where(a => a.Id == g.CreatedById).Select(a => a.Name).FirstOrDefault(), g => g.Name)
            .Add("updatedBy", g => actors.Where(a => a.Id == g.UpdatedById).Select(a => a.Name).FirstOrDefault(), g => g.Name)
            .Add("createdOnUtc", g => g.CreatedOnUtc)
            .Add("updatedOnUtc", g => g.UpdatedOnUtc, g => g.Name);
    }

    public async Task<(IReadOnlyList<PermissionGroup> Items, int Total)> ListAsync(
        Guid? tenantId, string? search, bool? isActive, bool? usedByRoles, string? category,
        SortRequest sort, int page, int limit, CancellationToken cancellationToken = default)
    {
        var query = (tenantId is { } tid
            ? _dbContext.PermissionGroups.IgnoreQueryFilters().Where(g => g.TenantId == tid && !g.Deleted)
            : _dbContext.PermissionGroups.AsQueryable())
            .Include(g => g.Permissions)
            .Include(g => g.Tenant)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(g => g.Name.Contains(term) || (g.Description != null && g.Description.Contains(term)));
        }
        if (isActive is { } active)
        {
            query = query.Where(g => g.IsActive == active);
        }
        if (usedByRoles is { } used)
        {
            var linkedIds = _dbContext.RolePermissionGroups.Select(l => l.PermissionGroupId);
            query = used ? query.Where(g => linkedIds.Contains(g.Id)) : query.Where(g => !linkedIds.Contains(g.Id));
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            var prefix = category.Trim() + ".";
            query = query.Where(g => g.Permissions.Any(p => p.PermissionKey.StartsWith(prefix)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await BuildSorts().Apply(query, sort.SortBy, sort.Descending)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<PermissionGroup?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.PermissionGroups
            .Include(g => g.Permissions)
            .Include(g => g.Tenant)
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    public Task<PermissionGroup?> GetByIdUnscopedAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.PermissionGroups
            .IgnoreQueryFilters()
            .Include(g => g.Permissions)
            .Include(g => g.Tenant)
            .FirstOrDefaultAsync(g => g.Id == id && !g.Deleted, cancellationToken);

    public Task<bool> NameExistsAsync(Guid tenantId, string name, Guid excludeId, CancellationToken cancellationToken = default)
        => _dbContext.PermissionGroups
            .IgnoreQueryFilters()
            .AnyAsync(g => g.TenantId == tenantId && !g.Deleted && g.Id != excludeId && g.Name == name, cancellationToken);

    public async Task AddAsync(PermissionGroup group, CancellationToken cancellationToken = default)
        => await _dbContext.PermissionGroups.AddAsync(group, cancellationToken);

    public void Update(PermissionGroup group) => _dbContext.PermissionGroups.Update(group);

    public void Remove(PermissionGroup group) => _dbContext.PermissionGroups.Remove(group);

    // ---- Permission junction rows ----

    public void RemovePermissions(IEnumerable<PermissionGroupPermission> permissions)
        => _dbContext.PermissionGroupPermissions.RemoveRange(permissions);

    public async Task AddPermissionAsync(PermissionGroupPermission permission, CancellationToken cancellationToken = default)
        => await _dbContext.PermissionGroupPermissions.AddAsync(permission, cancellationToken);

    // ---- Role composition links ----

    public async Task<IReadOnlyList<PermissionGroup>> GetByRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        // Platform-level computation: ignore the tenant filter (a role spans tenants) but exclude soft-deleted groups.
        var groupIds = _dbContext.RolePermissionGroups.Where(l => l.RoleId == roleId).Select(l => l.PermissionGroupId);
        return await _dbContext.PermissionGroups
            .IgnoreQueryFilters()
            .Include(g => g.Permissions)
            .Where(g => groupIds.Contains(g.Id) && !g.Deleted)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RolePermissionGroup>> GetRoleLinksAsync(Guid roleId, CancellationToken cancellationToken = default)
        => await _dbContext.RolePermissionGroups.Where(l => l.RoleId == roleId).ToListAsync(cancellationToken);

    public Task<RolePermissionGroup?> GetRoleLinkAsync(Guid roleId, Guid groupId, CancellationToken cancellationToken = default)
        => _dbContext.RolePermissionGroups.FirstOrDefaultAsync(l => l.RoleId == roleId && l.PermissionGroupId == groupId, cancellationToken);

    public async Task<IReadOnlyList<(Guid RoleId, string RoleName)>> GetRolesUsingGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.RolePermissionGroups
            .Where(l => l.PermissionGroupId == groupId)
            .Join(_dbContext.Roles.IgnoreQueryFilters().Where(r => !r.Deleted), l => l.RoleId, r => r.Id, (l, r) => new { r.Id, r.Name })
            .ToListAsync(cancellationToken);
        return rows.Select(r => (r.Id, r.Name)).ToList();
    }

    public Task<int> CountRolesUsingGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
        => _dbContext.RolePermissionGroups.CountAsync(l => l.PermissionGroupId == groupId, cancellationToken);

    public async Task AddRoleLinkAsync(RolePermissionGroup link, CancellationToken cancellationToken = default)
        => await _dbContext.RolePermissionGroups.AddAsync(link, cancellationToken);

    public void RemoveRoleLink(RolePermissionGroup link) => _dbContext.RolePermissionGroups.Remove(link);

    public async Task<IReadOnlyList<PermissionGroup>> GetGroupsByRolesAsync(IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        var ids = roleIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Array.Empty<PermissionGroup>();
        }

        var groupIds = _dbContext.RolePermissionGroups.Where(l => ids.Contains(l.RoleId)).Select(l => l.PermissionGroupId);
        return await _dbContext.PermissionGroups
            .IgnoreQueryFilters()
            .Where(g => groupIds.Contains(g.Id) && !g.Deleted)
            .ToListAsync(cancellationToken);
    }

    // ---- Capacity / usage (WO-119) ----

    public async Task<int> CountActiveMembersAsync(
        Guid groupId, Guid tenantId, IEnumerable<Guid>? additionalRoleIds = null, CancellationToken cancellationToken = default)
    {
        // Roles that compose this group (optionally including roles about to be composed in).
        var composingRoleIds = _dbContext.RolePermissionGroups.Where(l => l.PermissionGroupId == groupId).Select(l => l.RoleId);
        var extra = additionalRoleIds?.Distinct().ToList() ?? new List<Guid>();

        // Distinct active users holding at least one such active role assignment in the group's tenant.
        var userIds =
            from utr in _dbContext.UserTenantRoles.IgnoreQueryFilters()
            join u in _dbContext.Users.IgnoreQueryFilters() on utr.UserId equals u.Id
            where !utr.Deleted
                && utr.TenantId == tenantId
                && (composingRoleIds.Contains(utr.RoleId) || extra.Contains(utr.RoleId))
                && !u.Deleted
                && u.IsActive
            select utr.UserId;

        return await userIds.Distinct().CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountActiveMembersForGroupsAsync(
        IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken = default)
    {
        if (groupIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        // One row per (group, active member); each group is scoped to its OWN tenant (a Super Admin's
        // list may span tenants). Count distinct users per group in a single round-trip (no N+1).
        var rows =
            from link in _dbContext.RolePermissionGroups.Where(l => groupIds.Contains(l.PermissionGroupId))
            join g in _dbContext.PermissionGroups.IgnoreQueryFilters() on link.PermissionGroupId equals g.Id
            join utr in _dbContext.UserTenantRoles.IgnoreQueryFilters() on link.RoleId equals utr.RoleId
            join u in _dbContext.Users.IgnoreQueryFilters() on utr.UserId equals u.Id
            where !g.Deleted && !utr.Deleted && utr.TenantId == g.TenantId && !u.Deleted && u.IsActive
            select new { link.PermissionGroupId, utr.UserId };

        var counts = await rows
            .GroupBy(x => x.PermissionGroupId)
            .Select(grp => new { GroupId = grp.Key, Count = grp.Select(x => x.UserId).Distinct().Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(x => x.GroupId, x => x.Count);
    }

    public Task<bool> IsUserActiveMemberAsync(Guid groupId, Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        var composingRoleIds = _dbContext.RolePermissionGroups.Where(l => l.PermissionGroupId == groupId).Select(l => l.RoleId);
        return _dbContext.UserTenantRoles
            .IgnoreQueryFilters()
            .AnyAsync(utr => !utr.Deleted && utr.TenantId == tenantId && utr.UserId == userId && composingRoleIds.Contains(utr.RoleId), cancellationToken);
    }

    // ---- Templates ----

    public async Task<IReadOnlyList<PermissionGroupTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default)
        => await _dbContext.PermissionGroupTemplates.OrderBy(t => t.Name).ToListAsync(cancellationToken);

    public Task<PermissionGroupTemplate?> GetTemplateByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.PermissionGroupTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task AddTemplateAsync(PermissionGroupTemplate template, CancellationToken cancellationToken = default)
        => await _dbContext.PermissionGroupTemplates.AddAsync(template, cancellationToken);

    public void UpdateTemplate(PermissionGroupTemplate template) => _dbContext.PermissionGroupTemplates.Update(template);

    public void RemoveTemplate(PermissionGroupTemplate template) => _dbContext.PermissionGroupTemplates.Remove(template);
}
