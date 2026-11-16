using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// Data access for <see cref="User"/> and <see cref="UserTenantRole"/>. User lookups are
/// not tenant-filtered; tenant scoping for listings is applied via the optional tenant id.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Which of these emails already belong to a user, in any tenant.</summary>
    Task<IReadOnlyList<string>> ListEmailsInUseAsync(IReadOnlyCollection<string> emails, CancellationToken cancellationToken = default);

    /// <summary>Which of these Maconomy numbers are already on a user assigned to the tenant.</summary>
    Task<IReadOnlyList<string>> ListMaconomyNumbersInUseAsync(
        Guid tenantId, IReadOnlyCollection<string> numbers, CancellationToken cancellationToken = default);

    /// <summary>Resolves user ids to display full names (FirstName + LastName), for Created/Updated By columns.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated user list. When <paramref name="tenantId"/> is set, only that tenant's users;
    /// optional <paramref name="search"/> (email/name), <paramref name="isActive"/>, and per-column
    /// <paramref name="name"/>/<paramref name="email"/>/<paramref name="phone"/>/<paramref name="role"/>
    /// (role name) filters are applied server-side so pagination/totals reflect the filtered set.
    /// <para>
    /// <paramref name="sort"/> orders the whole filtered set before it is paged — which is the only place
    /// it CAN be ordered, since page 1 is only page 1 once the order is decided.
    /// </para>
    /// </summary>
    Task<(IReadOnlyList<User> Items, int Total)> ListAsync(
        Guid? tenantId, string? search, bool? isActive,
        string? name, string? email, string? phone, string? role, string? group,
        SortRequest sort, int page, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Active users assigned to <paramref name="tenantId"/> holding ANY of <paramref name="roleNames"/>
    /// (exact role-name match). Tenant-specific: the role assignment must be IN this tenant, so users
    /// whose only assignments are in other tenants are excluded. Distinct.
    /// </summary>
    Task<IReadOnlyList<User>> ListByTenantRolesAsync(Guid tenantId, IReadOnlyCollection<string> roleNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// Everyone holding one specific role in a tenant, by role id — the membership behind the role page's
    /// user panel. Inactive users are included: an admin managing who holds a role has to see the ones
    /// whose accounts are switched off, or removing them is impossible from here. Ordered by display name.
    /// </summary>
    Task<IReadOnlyList<User>> ListByTenantRoleAsync(Guid tenantId, Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every active user assigned to <paramref name="tenantId"/>, whatever role they hold there. The
    /// role-blind sibling of <see cref="ListByTenantRolesAsync"/>, for the pickers that offer the whole
    /// tenant rather than one seat's holders. Distinct, ordered by display name, unpaged.
    /// </summary>
    Task<IReadOnlyList<User>> ListActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);

    void Update(User user);

    /// <summary>All active (non-deleted) role assignments a user holds in a tenant (multi-role).</summary>
    Task<IReadOnlyList<UserTenantRole>> GetAssignmentsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);

    Task AddAssignmentAsync(UserTenantRole assignment, CancellationToken cancellationToken = default);

    void RemoveAssignment(UserTenantRole assignment);
}
