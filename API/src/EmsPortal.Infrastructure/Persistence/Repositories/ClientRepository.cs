using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class ClientRepository : IClientRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public ClientRepository(EmsPortalDbContext dbContext) => _dbContext = dbContext;

    public Task<Client?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.Clients.FullGraph().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Client?> GetByIdUnscopedAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
        => _dbContext.Clients.IgnoreQueryFilters().FullGraph()
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId && !c.Deleted, cancellationToken);

    public Task<Client?> GetByPersonIdAsync(Guid personId, CancellationToken cancellationToken = default)
        => _dbContext.Clients.FullGraph().FirstOrDefaultAsync(c => c.PersonId == personId, cancellationToken);

    public Task<Client?> FindByMaconomyNumberAsync(string customerNumber, CancellationToken cancellationToken = default)
        => _dbContext.Clients.FullGraph().FirstOrDefaultAsync(c => c.MaconomyCustomerNumber == customerNumber, cancellationToken);

    public async Task<IReadOnlyList<Client>> FindByEmailAsync(string email, Guid? excludingClientId, CancellationToken cancellationToken = default)
    {
        var wanted = email.Trim();
        return await _dbContext.Clients
            .Include(c => c.Person)
            .Where(c => c.PrimaryEmail == wanted && (excludingClientId == null || c.Id != excludingClientId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Client>> FindByExactNameAsync(string name, bool organisation, Guid? excludingClientId, CancellationToken cancellationToken = default)
    {
        var wanted = name.Trim();
        var query = _dbContext.Clients.Include(c => c.Person).Include(c => c.EntityType)
            .Where(c => excludingClientId == null || c.Id != excludingClientId);
        query = organisation
            ? query.Where(c => c.PersonId == null && c.LegalName == wanted)
            : query.Where(c => c.Person != null && (c.Person.DisplayName == wanted || c.Person.ClientDisplayName == wanted));
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Client>> LookupAsync(string term, string? entityTypeCode, int limit, CancellationToken cancellationToken = default)
    {
        var search = term.Trim();
        var query = _dbContext.Clients
            .Include(c => c.Person)
            .Include(c => c.EntityType)
            .Where(c => c.MaconomyCustomerNumber == null && c.Status != ClientStatus.Inactive);
        if (!string.IsNullOrWhiteSpace(entityTypeCode))
        {
            var code = entityTypeCode.Trim();
            query = query.Where(c => c.EntityType != null && c.EntityType.Value == code);
        }
        return await query
            .Where(c =>
                (c.Person != null
                    && (c.Person.FirstName.Contains(search)
                        || c.Person.LastName.Contains(search)
                        || c.Person.DisplayName.Contains(search)
                        || c.Person.ClientDisplayName.Contains(search)))
                || (c.LegalName != null && c.LegalName.Contains(search))
                || (c.PrimaryEmail != null && c.PrimaryEmail.Contains(search))
                || (c.PrimaryPhone != null && c.PrimaryPhone.Contains(search)))
            .OrderBy(c => c.Person != null ? c.Person.ClientDisplayName : c.LegalName)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> MaconomyNumberHeldByAnotherAsync(Guid tenantId, string customerNumber, Guid exceptClientId, CancellationToken cancellationToken = default)
        => _dbContext.Clients.IgnoreQueryFilters()
            .AnyAsync(c => c.TenantId == tenantId && !c.Deleted && c.Id != exceptClientId && c.MaconomyCustomerNumber == customerNumber, cancellationToken);

    public async Task AddAsync(Client client, CancellationToken cancellationToken = default)
        => await _dbContext.Clients.AddAsync(client, cancellationToken);

    public void Update(Client client) => _dbContext.Clients.Update(client);

    public async Task AddAddressAsync(ClientAddress address, CancellationToken cancellationToken = default)
        => await _dbContext.ClientAddresses.AddAsync(address, cancellationToken);

    public void RemoveAddress(ClientAddress address) => _dbContext.ClientAddresses.Remove(address);

    public async Task AddContactAsync(ClientContact contact, CancellationToken cancellationToken = default)
        => await _dbContext.ClientContacts.AddAsync(contact, cancellationToken);

    public void RemoveContact(ClientContact contact) => _dbContext.ClientContacts.Remove(contact);

    public Task<ClientRelationship?> GetRelationshipAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.ClientRelationships
            .Include(x => x.Client).ThenInclude(c => c!.Person)
            .Include(x => x.RelatedClient).ThenInclude(c => c!.Person)
            .Include(x => x.RelationType)
            .Include(x => x.FilingType)
            .Include(x => x.BillingPreference)
            .Include(x => x.RelatedStatus)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task AddRelationshipAsync(ClientRelationship relationship, CancellationToken cancellationToken = default)
        => await _dbContext.ClientRelationships.AddAsync(relationship, cancellationToken);

    public void UpdateRelationship(ClientRelationship relationship) => _dbContext.ClientRelationships.Update(relationship);

    public void RemoveRelationship(ClientRelationship relationship) => _dbContext.ClientRelationships.Remove(relationship);

    public async Task AddContractAsync(ClientContract contract, CancellationToken cancellationToken = default)
        => await _dbContext.ClientContracts.AddAsync(contract, cancellationToken);

    public void UpdateContract(ClientContract contract) => _dbContext.ClientContracts.Update(contract);

    // ---- Maconomy customer sync ----

    private IQueryable<Client> InTenant(Guid tenantId)
        => _dbContext.Clients.IgnoreQueryFilters().Where(c => c.TenantId == tenantId && !c.Deleted);

    public async Task<IReadOnlyList<Client>> ListUnsyncedAsync(Guid tenantId, DateTime staleBeforeUtc, CancellationToken cancellationToken = default)
        => await InTenant(tenantId)
            .Include(c => c.Person)
            .Where(c => c.MaconomySyncStatus == RemsMaconomyCustomerSyncStatus.Failed
                || c.MaconomySyncStatus == RemsMaconomyCustomerSyncStatus.Pending
                || (c.MaconomySyncStatus == RemsMaconomyCustomerSyncStatus.Syncing && c.UpdatedOnUtc < staleBeforeUtc))
            .OrderByDescending(c => c.UpdatedOnUtc)
            .ToListAsync(cancellationToken);

    public async Task<RemsMaconomyCustomerSyncCounts> CountSyncAsync(Guid tenantId, DateTime staleBeforeUtc, CancellationToken cancellationToken = default)
    {
        var groups = await InTenant(tenantId)
            .Where(c => c.MaconomySyncStatus != RemsMaconomyCustomerSyncStatus.NotSynced)
            .GroupBy(c => new
            {
                Status = c.MaconomySyncStatus,
                Stale = c.MaconomySyncStatus == RemsMaconomyCustomerSyncStatus.Syncing && c.UpdatedOnUtc < staleBeforeUtc,
            })
            .Select(g => new { g.Key.Status, g.Key.Stale, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var notSynced = await ApprovedUnrecorded(tenantId).CountAsync(cancellationToken);
        int Of(RemsMaconomyCustomerSyncStatus status, bool? stale = null)
            => groups.Where(g => g.Status == status && (stale is null || g.Stale == stale)).Sum(g => g.Count);
        return new RemsMaconomyCustomerSyncCounts(
            Synced: Of(RemsMaconomyCustomerSyncStatus.Synced),
            Linked: Of(RemsMaconomyCustomerSyncStatus.Linked),
            Failed: Of(RemsMaconomyCustomerSyncStatus.Failed),
            NotSynced: notSynced,
            Pending: Of(RemsMaconomyCustomerSyncStatus.Pending),
            Syncing: Of(RemsMaconomyCustomerSyncStatus.Syncing, stale: false),
            Stale: Of(RemsMaconomyCustomerSyncStatus.Syncing, stale: true));
    }

    // A client an approved brand-new-client request made postable, with no Maconomy number and nothing recorded yet.
    private IQueryable<Client> ApprovedUnrecorded(Guid tenantId)
        => InTenant(tenantId)
            .Where(c => c.MaconomyCustomerNumber == null
                && c.MaconomySyncStatus == RemsMaconomyCustomerSyncStatus.NotSynced
                && _dbContext.RemsRequests.IgnoreQueryFilters().Any(r => r.ClientId == c.Id && !r.Deleted
                    && r.Type != null && r.Type.Value == RemsRequestTypes.BrandNewClient
                    && r.Status != null && r.Status.Value == RemsRequestStatuses.Approved));

    public async Task<IReadOnlyList<Client>> ListApprovedUnrecordedAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => await ApprovedUnrecorded(tenantId)
            .Include(c => c.Person)
            .OrderByDescending(c => c.UpdatedOnUtc)
            .ToListAsync(cancellationToken);

    public Task<Client?> GetSyncSubjectAsync(Guid remsId, Guid tenantId, CancellationToken cancellationToken = default)
        => _dbContext.RemsRequests.IgnoreQueryFilters()
            .Where(r => r.Id == remsId && r.TenantId == tenantId && !r.Deleted)
            .Select(r => r.Client!)
            .Include(c => c.Person)
            .Include(c => c.EntityType)
            .Include(c => c.Industry)
            .FirstOrDefaultAsync(cancellationToken);
}
