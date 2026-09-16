using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository : IUserRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public UserRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Group memberships are tenant-filtered, so they naturally scope to the active tenant here.
        var user = await _dbContext.Users.Include(u => u.TenantRoles).ThenInclude(r => r.RoleEntity)
            .Include(u => u.GroupMemberships).ThenInclude(m => m.UserGroup)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        await LoadPersonAsync(user, cancellationToken);
        return user;
    }

    // Person carries a tenant query filter, which EF would also apply to an Include — hiding a user's
    // linked profile (and blanking their name) whenever the person's tenant differs from the active
    // one or is unset. Load it explicitly with the filter ignored; user-level tenant scoping is
    // enforced by the controller, so this only ever surfaces the user's own profile.
    private async Task LoadPersonAsync(User? user, CancellationToken cancellationToken)
    {
        if (user?.PersonId is { } personId)
        {
            // ProfileMedia comes with it: the admin surfaces show the same face the person set on their
            // own profile, and without it every avatar there falls back to initials.
            user.Person = await _dbContext.Persons.IgnoreQueryFilters()
                .Include(p => p.ProfileMedia)
                .FirstOrDefaultAsync(p => p.Id == personId && !p.Deleted, cancellationToken);
        }
    }

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => _dbContext.Users.Include(u => u.TenantRoles).ThenInclude(r => r.RoleEntity)
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
        => _dbContext.Users.AnyAsync(u => u.Email == email, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        // Ignore filters so soft-deleted creators still resolve to a name. Names now live on
        // the associated Person; fall back to the user's display name, then email.
        var users = await _dbContext.Users.IgnoreQueryFilters()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.Email,
                FirstName = u.Person != null ? u.Person.FirstName : null,
                LastName = u.Person != null ? u.Person.LastName : null
            })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(
            u => u.Id,
            u =>
            {
                var name = string.Join(" ", new[] { u.FirstName, u.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
                return string.IsNullOrWhiteSpace(name)
                    ? (string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName)
                    : name;
            });
    }

    // What the Users list may be ordered by. Roles and Groups are per-tenant collections assembled after
    // the query, not columns, so neither is here. The department is a placement row in the list's tenant,
    // and Created By / Updated By are ids resolved to names afterwards: each orders by a subquery that
    // reads the same value the cell shows, which is why the map is built per call.
    //
    // The name sorts on the two columns a person is FILED under, in that order, rather than on
    // DisplayName — which is what the Name cell shows and is free text.
    private SortMap<User> SortsFor(Guid? tenantId)
    {
        var actors = ActorNames.Of(_dbContext);
        // Query filters are off on the list query, so the placement's own scope is spelled out here.
        var placements = _dbContext.UserDepartments
            .Where(d => !d.Deleted && (tenantId == null || d.TenantId == tenantId));
        return new SortMap<User>("updatedOnUtc")
            .Add("fullName", u => u.Person!.FirstName, u => u.Person!.LastName)
            .Add("email", u => u.Email)
            .Add("phoneNumber", u => u.Person!.MobileNumber)
            .Add("department", u => placements.Where(d => d.UserId == u.Id).Select(d => d.Department).FirstOrDefault(), u => u.UpdatedOnUtc)
            .Add("isActive", u => u.IsActive, u => u.UpdatedOnUtc)
            .Add("createdBy", u => actors.Where(a => a.Id == u.CreatedById).Select(a => a.Name).FirstOrDefault(), u => u.UpdatedOnUtc)
            .Add("updatedBy", u => actors.Where(a => a.Id == u.UpdatedById).Select(a => a.Name).FirstOrDefault(), u => u.UpdatedOnUtc)
            .Add("createdOnUtc", u => u.CreatedOnUtc)
            .Add("updatedOnUtc", u => u.UpdatedOnUtc);
    }

    public async Task<(IReadOnlyList<User> Items, int Total)> ListAsync(
        Guid? tenantId, string? search, bool? isActive,
        string? name, string? email, string? phone, string? role, string? group,
        SortRequest sort, int page, int limit, CancellationToken cancellationToken = default)
    {
        // Ignore query filters (the Person tenant filter would otherwise blank the name / drop the row
        // for users whose person tenant differs or is unset) and re-apply the soft-delete predicates.
        // Group memberships are scoped to the list tenant manually (the ambient filter is off here).
        var query = _dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => !u.Deleted)
            .Include(u => u.TenantRoles.Where(r => !r.Deleted)).ThenInclude(r => r.RoleEntity)
            .Include(u => u.GroupMemberships.Where(m => !m.Deleted && (tenantId == null || m.TenantId == tenantId))).ThenInclude(m => m.UserGroup)
            .Include(u => u.Person)
            .AsQueryable();
        if (tenantId is { } id)
        {
            query = query.Where(u => u.TenantRoles.Any(r => r.TenantId == id && !r.Deleted));
        }
        if (!string.IsNullOrWhiteSpace(group))
        {
            var gt = group.Trim();
            query = query.Where(u => u.GroupMemberships.Any(m =>
                !m.Deleted && (tenantId == null || m.TenantId == tenantId) && m.UserGroup != null && m.UserGroup.Name.Contains(gt)));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u =>
                u.Email.Contains(term) ||
                (u.Person != null && (u.Person.FirstName.Contains(term) || u.Person.LastName.Contains(term))));
        }
        if (isActive is { } active)
        {
            query = query.Where(u => u.IsActive == active);
        }
        // Per-column "contains" filters (paired with the list's per-column filter drawer).
        if (!string.IsNullOrWhiteSpace(name))
        {
            var t = name.Trim();
            query = query.Where(u => u.Person != null &&
                (u.Person.FirstName.Contains(t) || u.Person.LastName.Contains(t) || u.Person.DisplayName.Contains(t)));
        }
        if (!string.IsNullOrWhiteSpace(email))
        {
            var t = email.Trim();
            query = query.Where(u => u.Email.Contains(t));
        }
        if (!string.IsNullOrWhiteSpace(phone))
        {
            var t = phone.Trim();
            query = query.Where(u => u.Person != null && u.Person.MobileNumber != null && u.Person.MobileNumber.Contains(t));
        }
        if (!string.IsNullOrWhiteSpace(role))
        {
            var t = role.Trim();
            query = query.Where(u => u.TenantRoles.Any(r => !r.Deleted && r.RoleEntity != null && r.RoleEntity.Name.Contains(t)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await SortsFor(tenantId).Apply(query, sort.SortBy, sort.Descending)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<User>> ListByTenantRolesAsync(
        Guid tenantId, IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken = default)
        // Tenant-specific: the role assignment must be IN this tenant. Distinct active users.
        => await _dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => !u.Deleted && u.IsActive)
            .Include(u => u.Person)
            .Where(u => u.TenantRoles.Any(r =>
                !r.Deleted && r.TenantId == tenantId && r.RoleEntity != null && roleNames.Contains(r.RoleEntity.Name)))
            .OrderBy(u => u.DisplayName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<User>> ListByTenantRoleAsync(
        Guid tenantId, Guid roleId, CancellationToken cancellationToken = default)
        // One role rather than a set of names, and inactive users included: this is the membership an
        // admin manages, not a picker of people who can be given work. The tenant's own assignments are
        // loaded with it so the caller can say what else each holder has here.
        => await _dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => !u.Deleted)
            .Include(u => u.Person)
            .Include(u => u.TenantRoles.Where(r => !r.Deleted && r.TenantId == tenantId)).ThenInclude(r => r.RoleEntity)
            .Where(u => u.TenantRoles.Any(r => !r.Deleted && r.TenantId == tenantId && r.RoleId == roleId))
            .OrderBy(u => u.DisplayName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<User>> ListActiveByTenantAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
        // The same shape as ListByTenantRolesAsync without the role filter — holding ANY role in the
        // tenant is what puts a user in it. Unpaged on purpose: its caller is a picker that has to offer
        // the whole tenant at once, and a firm's staff list is that size.
        //
        // The assignments come with them, filtered to THIS tenant, so a picker can say what each person is
        // to the firm without a query per row — and cannot show a role they hold somewhere else.
        => await _dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => !u.Deleted && u.IsActive)
            .Include(u => u.Person)
            .Include(u => u.TenantRoles.Where(r => !r.Deleted && r.TenantId == tenantId)).ThenInclude(r => r.RoleEntity)
            .Where(u => u.TenantRoles.Any(r => !r.Deleted && r.TenantId == tenantId))
            .OrderBy(u => u.DisplayName)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
        => await _dbContext.Users.AddAsync(user, cancellationToken);

    public void Update(User user) => _dbContext.Users.Update(user);

    public async Task<IReadOnlyList<UserTenantRole>> GetAssignmentsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
        // The soft-delete query filter excludes removed rows, so this returns only the active set.
        => await _dbContext.UserTenantRoles.Include(r => r.RoleEntity)
            .Where(r => r.UserId == userId && r.TenantId == tenantId)
            .ToListAsync(cancellationToken);

    public async Task AddAssignmentAsync(UserTenantRole assignment, CancellationToken cancellationToken = default)
        => await _dbContext.UserTenantRoles.AddAsync(assignment, cancellationToken);

    public void RemoveAssignment(UserTenantRole assignment) => _dbContext.UserTenantRoles.Remove(assignment);
}
