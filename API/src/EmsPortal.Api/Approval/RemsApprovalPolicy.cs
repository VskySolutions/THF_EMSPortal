using EmsPortal.Api.Models.Rems;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Configuration;
using EmsPortal.Shared.Security;
using Microsoft.Extensions.Options;

namespace EmsPortal.Api.Approval;

// STATIC-APPROVAL-POLICY. Everything in this file exists for the fixed approver rules THF asked for;
// deleting the file and the marker-commented lines elsewhere puts the platform back on its own list.

/// <summary>The policy resolved against one tenant's people: who holds the Shareholder role, which CSEs are the tax exception.</summary>
public sealed record RemsApprovalPolicySnapshot(
    bool StaticRouting,
    decimal TaxFeeCeilingWithoutShareholder,
    IReadOnlyList<User> Shareholders,
    IReadOnlyList<User> TaxExceptionCses)
{
    public bool IsTaxExceptionCse(Guid? userId) => userId is { } id && TaxExceptionCses.Any(u => u.Id == id);
}

public interface IRemsApprovalPolicy
{
    Task<RemsApprovalPolicySnapshot> ForTenantAsync(Guid? tenantId, CancellationToken cancellationToken);
}

/// <summary>Resolves the policy against the tenant: the Shareholder role's holders, and the named CSEs by the person's "First Last".</summary>
internal sealed class RemsApprovalPolicy : IRemsApprovalPolicy
{
    private readonly IUserRepository _users;
    private readonly RemsApprovalPolicyOptions _options;

    public RemsApprovalPolicy(IUserRepository users, IOptions<RemsApprovalPolicyOptions> options)
    {
        _users = users;
        _options = options.Value;
    }

    public async Task<RemsApprovalPolicySnapshot> ForTenantAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        if (!_options.StaticRouting || tenantId is not { } tid)
        {
            return new RemsApprovalPolicySnapshot(false, _options.TaxFeeCeilingWithoutShareholder, Array.Empty<User>(), Array.Empty<User>());
        }

        var shareholders = await _users.ListByTenantRolesAsync(tid, new[] { Roles.Shareholder }, cancellationToken);
        var people = await _users.ListActiveByTenantAsync(tid, cancellationToken);
        var exceptions = _options.TaxExceptionCses
            .Select(name => Find(people, name))
            .Where(u => u is not null)
            .Select(u => u!)
            .DistinctBy(u => u.Id)
            .ToList();

        return new RemsApprovalPolicySnapshot(
            true, _options.TaxFeeCeilingWithoutShareholder, shareholders.DistinctBy(u => u.Id).ToList(), exceptions);
    }

    /// <summary>The one user whose name reads as the configured "First Last"; null when nobody or more than one does.</summary>
    public static User? Find(IEnumerable<User> people, string? fullName)
    {
        var wanted = Normalize(fullName);
        if (wanted.Length == 0)
        {
            return null;
        }

        var matches = people.Where(u => Matches(u, wanted)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool Matches(User user, string wanted)
    {
        var candidates = new[]
        {
            $"{user.Person?.FirstName} {user.Person?.LastName}",
            user.Person?.DisplayName,
            user.DisplayName,
        };
        return candidates.Any(c => Normalize(c) == wanted);
    }

    private static string Normalize(string? name)
        => string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToUpperInvariant();
}

/// <summary>The approvers an engagement routes to, all asked together, or the reason it cannot be sent yet.</summary>
public sealed record RemsApprovalRoute(IReadOnlyList<(Guid UserId, RemsApproverRole Role)> Approvers, string? BlockedReason);

/// <summary>
/// The fixed approver list: every commission recipient, the CSE, the department director (unless the
/// CSE is the tax exception), anyone added by hand, and the Shareholder role's holders (unless the
/// engagement is a small tax one). Everyone is asked at the same time; a person in two seats is asked once.
/// </summary>
public static class RemsStaticApprovalRoute
{
    public static RemsApprovalRoute Build(
        REMSEngagement engagement, IReadOnlyList<Guid> pickedApproverIds, RemsApprovalPolicySnapshot policy)
    {
        var cse = engagement.Rems?.CSEId;
        var director = engagement.DepartmentDirectorId;
        var shareholders = policy.Shareholders.Select(u => u.Id).ToList();
        var recipients = engagement.CommissionSplits.Where(s => !s.Deleted).Select(s => s.EmployeeId).Distinct().ToList();
        // The exception is the TAX department's: one of its named CSEs on an engagement placed anywhere
        // else is routed like everybody.
        var cseIsException = policy.IsTaxExceptionCse(cse) && RemsEngagementCodes.IsTax(engagement.Department?.Value);

        if (cse is null)
        {
            return Blocked("Name a CSE on the request first — every engagement needs the CSE's approval.");
        }
        if (recipients.Any(r => r == cse || r == director || shareholders.Contains(r)))
        {
            return Blocked("A commission recipient cannot also be the CSE, the Department Director or a Shareholder on this request.");
        }
        if (director is null && !cseIsException)
        {
            return Blocked("Pick a department that has a director — this engagement needs the Department Director's approval.");
        }

        var approvers = new List<(Guid UserId, RemsApproverRole Role)>();
        var asked = new HashSet<Guid>();

        void Add(IEnumerable<(Guid UserId, RemsApproverRole Role)> people)
            => approvers.AddRange(people.Where(a => asked.Add(a.UserId)));

        Add(recipients.Select(r => (r, RemsApproverRole.CommissionRecipient)));
        Add(new[] { (cse.Value, RemsApproverRole.CSE) });
        if (director is { } d && !cseIsException)
        {
            Add(new[] { (d, RemsApproverRole.DepartmentDirector) });
        }
        Add(pickedApproverIds.Distinct().Select(p => (p, RemsApproverRole.Approver)));

        if (NeedsShareholder(engagement, policy, cseIsException))
        {
            if (shareholders.Count == 0)
            {
                return Blocked("Nobody holds the Shareholder role in this tenant, so this engagement cannot be routed.");
            }
            Add(shareholders.Select(s => (s, RemsApproverRole.Shareholder)));
        }

        return new RemsApprovalRoute(approvers, null);
    }

    /// <summary>Required for every engagement except a tax engagement priced at or under the ceiling, unless the CSE is the tax exception.</summary>
    private static bool NeedsShareholder(REMSEngagement engagement, RemsApprovalPolicySnapshot policy, bool cseIsException)
    {
        if (cseIsException)
        {
            return true;
        }
        if (!RemsEngagementCodes.IsTax(engagement.Department?.Value))
        {
            return true;
        }
        // No fee entered reads as "not known to be small", so the shareholders are asked.
        return engagement.FirstYearFeeEstimate is not { } fee || fee > policy.TaxFeeCeilingWithoutShareholder;
    }

    /// <summary>The users an engagement's seats reserve: nobody in them may also receive commission or be picked as an extra approver.</summary>
    public static IReadOnlyList<Guid> ReservedUserIds(REMSEngagement engagement, RemsApprovalPolicySnapshot policy)
        => new[] { engagement.Rems?.CSEId, engagement.DepartmentDirectorId }
            .Where(id => id.HasValue).Select(id => id!.Value)
            .Concat(policy.Shareholders.Select(u => u.Id))
            .Distinct().ToList();

    private static RemsApprovalRoute Blocked(string reason) => new(Array.Empty<(Guid, RemsApproverRole)>(), reason);
}
