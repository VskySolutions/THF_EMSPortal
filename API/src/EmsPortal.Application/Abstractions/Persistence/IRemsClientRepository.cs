using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// Data access for the REMS client aggregate (WO-110): the client materialised from a submission, its
/// entities, and each entity's addresses and contacts.
/// </summary>
public interface IRemsClientRepository
{
    /// <summary>The client with its entities, their addresses and contacts loaded.</summary>
    Task<REMSClient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The active client for a REMS request, with its entity graph loaded.</summary>
    Task<REMSClient?> GetByRemsIdAsync(Guid remsId, CancellationToken cancellationToken = default);

    Task AddAsync(REMSClient client, CancellationToken cancellationToken = default);

    void Update(REMSClient client);

    void Remove(REMSClient client);

    /// <summary>
    /// A single entity with its addresses (and shared Address) and contacts (and Person) loaded
    /// (WO-114).
    /// </summary>
    Task<REMSEntity?> GetEntityAsync(Guid entityId, CancellationToken cancellationToken = default);

    Task AddEntityAsync(REMSEntity entity, CancellationToken cancellationToken = default);

    void RemoveEntity(REMSEntity entity);

    Task AddEntityAddressAsync(REMSEntityAddress address, CancellationToken cancellationToken = default);

    void RemoveEntityAddress(REMSEntityAddress address);

    Task AddEntityContactAsync(REMSEntityContact contact, CancellationToken cancellationToken = default);

    void RemoveEntityContact(REMSEntityContact contact);
}
