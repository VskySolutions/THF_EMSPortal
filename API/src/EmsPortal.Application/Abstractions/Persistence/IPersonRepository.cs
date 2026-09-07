using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>Data access for the CRM <see cref="Person"/> master record (WO-61).</summary>
public interface IPersonRepository
{
    /// <summary>Loads a person with its primary address and profile media, scoped to the active tenant.</summary>
    Task<Person?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Loads a person ignoring the tenant filter — for cross-tenant Super Admin access.</summary>
    Task<Person?> GetByIdUnscopedAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Person?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> PersonCodeExistsAsync(string personCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated list with optional free-text search (name, email, person code) and optional
    /// structured filters (owning tenant, whether the person is a user, active state.
    /// </summary>
    /// <param name="partyType"> Narrows to people or to organisations. The REMS client picker's whole use for it: a request for an Individual must offer individuals, and a request for any other entity type must offer companies — offering the wrong kind is offering somebody a client they cannot file the request under. </param>
    Task<(IReadOnlyList<Person> Items, int Total)> ListAsync(
        string? search, Guid? tenantId, bool? isUser, bool? isActive, SortRequest sort, int page, int limit,
        EntityType? sourceEntityType = null, PartyType? partyType = null,
        CancellationToken cancellationToken = default);

    /// <summary>The client already holding this email address, if any.</summary>
    Task<Person?> FindClientByEmailAsync(
        string email, Guid? excludingPersonId, CancellationToken cancellationToken = default);

    /// <summary>Lightweight projection for the user-create Person dropdown (id, name, email, user-link flag).</summary>
    Task<IReadOnlyList<(Person Person, bool IsUser)>> ListSelectableAsync(
        Guid? tenantId = null, CancellationToken cancellationToken = default);

    Task AddAsync(Person person, CancellationToken cancellationToken = default);

    void Update(Person person);

    /// <summary>Soft-deletes the person (the DbContext converts the delete to a <c>Deleted</c> flag).</summary>
    void Remove(Person person);
}
