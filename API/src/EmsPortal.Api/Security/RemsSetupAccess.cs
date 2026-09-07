using System.Security.Claims;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Security;

namespace EmsPortal.Api.Security;

/// <summary>
/// Who may read and who may WORK a request's engagement setup — the CSE/industry group on its form
/// and every field of the engagement itself.
/// </summary>
internal static class RemsSetupAccess
{
    /// <summary>
    /// A Super Admin or Tenant Admin, who are exempt from the stage rules so an assignment can be
    /// worked around in an emergency.
    /// </summary>
    public static bool IsElevated(ClaimsPrincipal user)
        => user.IsSuperAdmin() || user.GetRoles().Any(r => string.Equals(r, Roles.TenantAdmin, StringComparison.Ordinal));

    /// <summary>A REMS Admin (or a Super Admin): the operational role that runs the firm's pipeline.</summary>
    public static bool IsRemsAdmin(ClaimsPrincipal user)
        => user.IsSuperAdmin() || user.GetRoles().Any(r => string.Equals(r, Roles.Admin, StringComparison.Ordinal));

    /// <summary>Whose request this is: the person who raised it, or the principal they raised it for.</summary>
    public static bool IsInitiator(REMS rems, Guid me)
        => rems.CreatedById == me || rems.OnBehalfOfUserId == me;

    /// <summary>
    /// WHOSE work this request is, for the purpose of asking what they have delegated: the principal
    /// it was raised for where a delegate raised it, and the person who raised it otherwise.
    /// </summary>
    public static Guid? PrincipalOf(REMS rems) => rems.OnBehalfOfUserId ?? rems.CreatedById;

    /// <summary>Everyone named on the request: its initiator, the CSE on it, and the admin reviewing it.</summary>
    public static bool IsParticipant(REMS rems, Guid me)
        => IsInitiator(rems, me) || rems.CSEId == me || rems.AdminAssignedToId == me;

    /// <summary>May READ the setup.</summary>
    public static bool CanRead(ClaimsPrincipal user, REMS rems, Guid me)
        => IsElevated(user)
            || IsParticipant(rems, me)
            || user.HasPermission(Permissions.RemsEngagementsManage);

    /// <summary>May WRITE the setup: whoever the request is with at this stage.</summary>
    /// <param name="initiatorHasCover"> Whether <see cref="PrincipalOf"/> has any REMS delegation in force today (<c>IRemsDelegationRepository.HasActiveDelegateAsync</c>). Passed in rather than looked up here so this stays a pure rule; a caller with no CSE in play may pass false without querying. </param>
    public static bool CanWork(ClaimsPrincipal user, REMS rems, Guid me, bool initiatorHasCover)
    {
        if (IsElevated(user))
        {
            return true;
        }

        if (RemsRequestStatuses.IsWithInitiator(rems.Status!.Value))
        {
            return IsInitiator(rems, me)
                || (rems.CSEId == me && (initiatorHasCover || !RemsRequestStatuses.IsRework(rems.Status!.Value)))
                || (IsRemsAdmin(user) && (rems.Status!.Value == RemsRequestStatuses.Draft || RemsRequestStatuses.IsRework(rems.Status!.Value)));
        }

        return rems.AdminAssignedToId is { } admin && admin == me;
    }

    /// <summary>
    /// Whether this request's initiator has any REMS delegation in force today — "have they arranged
    /// cover?".
    /// </summary>
    public static async Task<bool> InitiatorHasCoverAsync(
        IRemsDelegationRepository delegations, REMS rems, CancellationToken cancellationToken)
        => PrincipalOf(rems) is { } principal
            && await delegations.HasActiveDelegateAsync(
                principal, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);

    /// <summary>
    /// The cover flag for a <see cref="CanWork"/> call, skipping the lookup wherever it cannot change
    /// the answer.
    /// </summary>
    public static Task<bool> CoverForWorkAsync(
        IRemsDelegationRepository delegations, REMS rems, Guid me, CancellationToken cancellationToken)
        => rems.CSEId == me && RemsRequestStatuses.IsRework(rems.Status!.Value)
            ? InitiatorHasCoverAsync(delegations, rems, cancellationToken)
            : Task.FromResult(false);

    /// <summary>The refusal that goes with a failed <see cref="CanWork"/>, worded for the stage it failed at.</summary>
    public static string WorkDeniedReason(REMS rems)
        => RemsRequestStatuses.IsRework(rems.Status!.Value)
            ? "This request has been returned to the person who raised it. Only they, or a REMS Admin, can work its engagement setup — the CSE can take it on only where the initiator has named a REMS delegate."
            : RemsRequestStatuses.IsWithInitiator(rems.Status!.Value)
            ? "This request is with the person who raised it; only they (or the CSE named on it), or a REMS Admin, can work its engagement setup."
            : rems.AdminAssignedToId is null
                ? "This request is waiting for pickup. Pick it up from EMS Review to work its engagement setup."
                : "This request is being reviewed by another admin; only they can work its engagement setup.";
}
