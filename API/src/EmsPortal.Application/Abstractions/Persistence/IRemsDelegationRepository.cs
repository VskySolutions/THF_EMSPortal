using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>Data access for REMS delegations — who may work whose requests.</summary>
public interface IRemsDelegationRepository
{
    /// <summary>The delegates a principal has named, whether or not they are currently in force.</summary>
    Task<IReadOnlyList<REMSDelegation>> ListForPrincipalAsync(
        Guid principalUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The principals a delegate may act for, filtered to those in force on <paramref name="on"/>. Backs
    /// the acting-as picker, so an expired or not-yet-started grant simply is not offered.
    /// </summary>
    Task<IReadOnlyList<REMSDelegation>> ListActiveForDelegateAsync(
        Guid delegateUserId, DateOnly on, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the principal has ANY delegation in force on <paramref name="on"/> — "has this person
    /// arranged cover for their REMS work?" — answered without loading the grants to find out.
    /// <para>
    /// Deliberately not keyed on WHO the delegate is. What it establishes is that the principal has
    /// handed their REMS work out at all, which is what lets a request in rework sit with somebody other
    /// than them.
    /// </para>
    /// </summary>
    Task<bool> HasActiveDelegateAsync(
        Guid principalUserId, DateOnly on, CancellationToken cancellationToken = default);

    /// <summary>One pair's grant, or null.</summary>
    Task<REMSDelegation?> GetAsync(
        Guid principalUserId, Guid delegateUserId, CancellationToken cancellationToken = default);

    Task<REMSDelegation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(REMSDelegation delegation, CancellationToken cancellationToken = default);

    void Update(REMSDelegation delegation);

    void Remove(REMSDelegation delegation);
}
