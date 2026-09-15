using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class PersonRepository : IPersonRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public PersonRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Person?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.Persons
            .Include(p => p.Address)
            .Include(p => p.ProfileMedia)
            .Include(p => p.Tenant)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    /// <summary>Super Admin (cross-tenant) read of a single person, bypassing the ambient tenant filter.</summary>
    public Task<Person?> GetByIdUnscopedAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.Persons
            .IgnoreQueryFilters()
            .Include(p => p.Address)
            .Include(p => p.ProfileMedia)
            .Include(p => p.Tenant)
            .FirstOrDefaultAsync(p => p.Id == id && !p.Deleted, cancellationToken);

    // Self-profile lookup is keyed by the authenticated user's own id, so it bypasses the tenant
    // filter: a user's person may be stamped to a tenant other than the one currently active.
    public Task<Person?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => _dbContext.Persons
            .IgnoreQueryFilters()
            .Include(p => p.Address)
            .Include(p => p.ProfileMedia)
            .FirstOrDefaultAsync(p => p.UserId == userId && !p.Deleted, cancellationToken);

    public Task<bool> PersonCodeExistsAsync(string personCode, CancellationToken cancellationToken = default)
        => _dbContext.Persons.AnyAsync(p => p.PersonCode == personCode, cancellationToken);

    // What the People list may be ordered by. Created By / Updated By are ids the controller resolves to
    // names after the query; they order by the ActorNames subquery, which is why the map is built per call.
    private SortMap<Person> BuildSorts()
    {
        var actors = ActorNames.Of(_dbContext);
        return new SortMap<Person>("updatedOnUtc")
            .Add("tenantName", p => p.Tenant!.Name, p => p.UpdatedOnUtc)
            .Add("personCode", p => p.PersonCode)
            .Add("fullName", p => p.FirstName, p => p.LastName)
            .Add("primaryEmail", p => p.PrimaryEmail)
            .Add("mobileNumber", p => p.MobileNumber)
            // "Account" is whether the person has been promoted to a login — a null UserId or not.
            .Add("isUser", p => p.UserId == null, p => p.UpdatedOnUtc)
            .Add("isActive", p => p.IsActive, p => p.UpdatedOnUtc)
            .Add("sourceEntityType", p => p.SourceEntityType, p => p.UpdatedOnUtc)
            .Add("createdBy", p => actors.Where(a => a.Id == p.CreatedById).Select(a => a.Name).FirstOrDefault(), p => p.UpdatedOnUtc)
            .Add("updatedBy", p => actors.Where(a => a.Id == p.UpdatedById).Select(a => a.Name).FirstOrDefault(), p => p.UpdatedOnUtc)
            .Add("createdOnUtc", p => p.CreatedOnUtc)
            .Add("updatedOnUtc", p => p.UpdatedOnUtc);
    }

    public async Task<(IReadOnlyList<Person> Items, int Total)> ListAsync(
        string? search, Guid? tenantId, bool? isUser, bool? isActive, SortRequest sort, int page, int limit,
        EntityType? sourceEntityType = null, PartyType? partyType = null,
        CancellationToken cancellationToken = default)
    {
        // Cross-tenant (Super Admin) reads pass an explicit tenant id and bypass the ambient filter;
        // everyone else gets the ambient-filtered set, pinned to their active tenant.
        var query = (tenantId is { } tid
            ? _dbContext.Persons.IgnoreQueryFilters().Where(p => p.TenantId == tid && !p.Deleted)
            : _dbContext.Persons.AsQueryable())
            .Include(p => p.Tenant)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                p.FirstName.Contains(term) ||
                p.LastName.Contains(term) ||
                p.DisplayName.Contains(term) ||
                // The organisation's legal name, and the client name as it READS — "Smith John Jr." —
                // so a picker showing that string finds a row when somebody types what they can see.
                // Without the first of these a company could only be found by whatever the first/last
                // split had guessed at, which for an organisation is nothing at all.
                (p.CorporateName != null && p.CorporateName.Contains(term)) ||
                p.ClientDisplayName.Contains(term) ||
                p.PersonCode.Contains(term) ||
                (p.PrimaryEmail != null && p.PrimaryEmail.Contains(term)) ||
                (p.MobileNumber != null && p.MobileNumber.Contains(term)));
        }

        if (isUser is { } user)
        {
            query = user ? query.Where(p => p.UserId != null) : query.Where(p => p.UserId == null);
        }
        if (isActive is { } active)
        {
            query = query.Where(p => p.IsActive == active);
        }
        if (sourceEntityType is { } source)
        {
            query = query.Where(p => p.SourceEntityType == source);
        }
        if (partyType is { } party)
        {
            query = query.Where(p => p.PartyType == party);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await BuildSorts().Apply(query, sort.SortBy, sort.Descending)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    // Tenant-scoped and soft-delete-filtered by the ambient query filter. Equality (not ToLower) so the
    // column's case-insensitive collation does the comparing and the index stays usable.
    public Task<Person?> FindClientByEmailAsync(
        string email, Guid? excludingPersonId, CancellationToken cancellationToken = default)
        => _dbContext.Persons
            .FirstOrDefaultAsync(
                p => p.SourceEntityType == EntityType.Client
                    && p.PrimaryEmail == email
                    && (excludingPersonId == null || p.Id != excludingPersonId),
                cancellationToken);

    public async Task<IReadOnlyList<(Person Person, bool IsUser)>> ListSelectableAsync(
        Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        // Naming a tenant means reading OUTSIDE the ambient one, so the filters come off — and with them
        // the soft-delete predicate they carry, which is why `Deleted` is then stated in full.
        var query = tenantId is { } scope
            ? _dbContext.Persons.IgnoreQueryFilters().Where(p => !p.Deleted && p.TenantId == scope)
            : _dbContext.Persons.AsQueryable();

        var items = await query
            .OrderBy(p => p.FirstName).ThenBy(p => p.LastName)
            .Select(p => new { Person = p, IsUser = p.UserId != null })
            .ToListAsync(cancellationToken);

        return items.Select(x => (x.Person, x.IsUser)).ToList();
    }

    public async Task AddAsync(Person person, CancellationToken cancellationToken = default)
        => await _dbContext.Persons.AddAsync(person, cancellationToken);

    public void Update(Person person) => _dbContext.Persons.Update(person);

    public void Remove(Person person) => _dbContext.Persons.Remove(person);
}
