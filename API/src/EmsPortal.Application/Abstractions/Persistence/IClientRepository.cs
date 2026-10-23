using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// The client master (Client and its Client_* rows). A client outlives any one request; the intake form
/// writes these rows directly and staff edit the same rows afterwards.
/// </summary>
public interface IClientRepository
{
    /// <summary>The client with its person, option items, addresses, contacts, relationships and contracts.</summary>
    Task<Client?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The same graph, outside the tenant filter, for jobs that run without a request context.</summary>
    Task<Client?> GetByIdUnscopedAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);

    Task<Client?> GetByPersonIdAsync(Guid personId, CancellationToken cancellationToken = default);

    Task<Client?> FindByMaconomyNumberAsync(string customerNumber, CancellationToken cancellationToken = default);

    /// <summary>Clients whose email matches, except the one named; an email belongs to one client.</summary>
    Task<IReadOnlyList<Client>> FindByEmailAsync(string email, Guid? excludingClientId, CancellationToken cancellationToken = default);

    /// <summary>Clients with exactly this display or legal name, of the given party kind.</summary>
    Task<IReadOnlyList<Client>> FindByExactNameAsync(string name, bool organisation, Guid? excludingClientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The picker's search over clients on file: not Maconomy-linked, matched on names, email and phone,
    /// optionally narrowed to one entity type.
    /// </summary>
    Task<IReadOnlyList<Client>> LookupAsync(string term, string? entityTypeCode, int limit, CancellationToken cancellationToken = default);

    Task<bool> MaconomyNumberHeldByAnotherAsync(Guid tenantId, string customerNumber, Guid exceptClientId, CancellationToken cancellationToken = default);

    Task AddAsync(Client client, CancellationToken cancellationToken = default);
    void Update(Client client);

    Task AddAddressAsync(ClientAddress address, CancellationToken cancellationToken = default);
    void RemoveAddress(ClientAddress address);

    Task AddContactAsync(ClientContact contact, CancellationToken cancellationToken = default);
    void RemoveContact(ClientContact contact);

    Task<ClientRelationship?> GetRelationshipAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddRelationshipAsync(ClientRelationship relationship, CancellationToken cancellationToken = default);
    void UpdateRelationship(ClientRelationship relationship);
    void RemoveRelationship(ClientRelationship relationship);

    Task AddContractAsync(ClientContract contract, CancellationToken cancellationToken = default);
    void UpdateContract(ClientContract contract);

    // ---- Maconomy customer sync, now three columns on the client ----

    /// <summary>Clients still to post: failed, never synced, or stuck in Syncing since before the cut-off. Outside the tenant filter.</summary>
    Task<IReadOnlyList<Client>> ListUnsyncedAsync(Guid tenantId, DateTime staleBeforeUtc, CancellationToken cancellationToken = default);

    Task<RemsMaconomyCustomerSyncCounts> CountSyncAsync(Guid tenantId, DateTime staleBeforeUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clients an approved brand-new-client request made eligible for posting that have neither a Maconomy
    /// number nor a sync under way.
    /// </summary>
    Task<IReadOnlyList<Client>> ListApprovedUnrecordedAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>The client a request is for, with the option items the Maconomy payload needs. Outside the tenant filter.</summary>
    Task<Client?> GetSyncSubjectAsync(Guid remsId, Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Takes the next client number from the sequence; a deleted client never frees its number.</summary>
public interface IClientNumberGenerator
{
    Task<string> GenerateAsync(CancellationToken cancellationToken = default);
}
