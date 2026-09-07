using EmsPortal.Api.Models.Rems;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace EmsPortal.Api.Approval;

// STATIC-APPROVAL-POLICY. Everything in this file exists for the fixed route THF asked for; deleting the
// file and the marker-commented lines elsewhere puts the platform back on its single parallel round.

/// <summary>The policy resolved against one tenant's people: who the managing shareholder is, which CSEs are the tax exception.</summary>
public sealed record RemsApprovalPolicySnapshot(
    bool StaticRouting,
    decimal TaxFeeCeilingWithoutManagingShareholder,
    User? ManagingShareholder,
    IReadOnlyList<User> TaxExceptionCses)
{
    public bool IsTaxExceptionCse(Guid? userId) => userId is { } id && TaxExceptionCses.Any(u => u.Id == id);
}

public interface IRemsApprovalPolicy
{
    Task<RemsApprovalPolicySnapshot> ForTenantAsync(Guid? tenantId, CancellationToken cancellationToken);
}

/// <summary>Resolves the configured names to the tenant's users. Names match on the person's "First Last".</summary>
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
            return new RemsApprovalPolicySnapshot(false, _options.TaxFeeCeilingWithoutManagingShareholder, null, Array.Empty<User>());
        }

        var people = await _users.ListActiveByTenantAsync(tid, cancellationToken);
        var managing = Find(people, _options.ManagingShareholder);
        var exceptions = _options.TaxExceptionCses
            .Select(name => Find(people, name))
            .Where(u => u is not null)
            .Select(u => u!)
            .DistinctBy(u => u.Id)
            .ToList();

        return new RemsApprovalPolicySnapshot(true, _options.TaxFeeCeilingWithoutManagingShareholder, managing, exceptions);
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

/// <summary>One stage of a staged round: the approvers asked together, in the order the stages run.</summary>
public sealed record RemsApprovalStage(int Number, string Name, IReadOnlyList<(Guid UserId, RemsApproverRole Role)> Approvers);

/// <summary>The route an engagement takes, or the reason it cannot be sent yet.</summary>
public sealed record RemsApprovalRoute(IReadOnlyList<RemsApprovalStage> Stages, string? BlockedReason)
{
    public IEnumerable<(Guid UserId, RemsApproverRole Role)> Approvers => Stages.SelectMany(s => s.Approvers);
}

/// <summary>
/// The fixed route: commission recipients together, then the CSE, then the department director (with
/// anyone added by hand), then the managing shareholder. Each stage is skipped by the rules below and a
/// person already asked at an earlier stage is not asked again.
/// </summary>
public static class RemsStaticApprovalRoute
{
    public const string StageCommission = "Commission";
    public const string StageCse = "CSE";
    public const string StageDepartmentDirector = "Department Director";
    public const string StageManagingShareholder = "Managing Shareholder";

    public static RemsApprovalRoute Build(
        REMSEngagement engagement, IReadOnlyList<Guid> pickedApproverIds, RemsApprovalPolicySnapshot policy)
    {
        var cse = engagement.Rems?.CSEId;
        var director = engagement.DepartmentDirectorId;
        var managing = policy.ManagingShareholder?.Id;
        var recipients = engagement.CommissionSplits.Where(s => !s.Deleted).Select(s => s.EmployeeId).Distinct().ToList();
        // The exception is the TAX department's: one of its named CSEs on an engagement placed anywhere
        // else is routed like everybody.
        var cseIsException = policy.IsTaxExceptionCse(cse) && RemsEngagementCodes.IsTax(engagement.Department?.Value);

        if (cse is null)
        {
            return Blocked("Name a CSE on the request first — every engagement needs the CSE's approval.");
        }
        if (recipients.Any(r => r == cse || r == director || r == managing))
        {
            return Blocked("A commission recipient cannot also be the CSE, the Department Director or the Managing Shareholder on this request.");
        }
        if (director is null && !cseIsException)
        {
            return Blocked("Pick a department that has a director — this engagement needs the Department Director's approval.");
        }

        var stages = new List<RemsApprovalStage>();
        var asked = new HashSet<Guid>();

        void Add(string name, IEnumerable<(Guid UserId, RemsApproverRole Role)> approvers)
        {
            var fresh = approvers.Where(a => asked.Add(a.UserId)).ToList();
            if (fresh.Count > 0)
            {
                stages.Add(new RemsApprovalStage(stages.Count + 1, name, fresh));
            }
        }

        Add(StageCommission, recipients.Select(r => (r, RemsApproverRole.CommissionRecipient)));
        Add(StageCse, new[] { (cse.Value, RemsApproverRole.CSE) });

        var directorStage = new List<(Guid, RemsApproverRole)>();
        if (director is { } d && !cseIsException)
        {
            directorStage.Add((d, RemsApproverRole.DepartmentDirector));
        }
        directorStage.AddRange(pickedApproverIds.Distinct().Select(p => (p, RemsApproverRole.Approver)));
        Add(StageDepartmentDirector, directorStage);

        if (NeedsManagingShareholder(engagement, policy, cseIsException))
        {
            if (managing is null)
            {
                return Blocked("The Managing Shareholder is not an active user of this tenant, so this engagement cannot be routed.");
            }
            Add(StageManagingShareholder, new[] { (managing.Value, RemsApproverRole.Shareholder) });
        }

        return new RemsApprovalRoute(stages, null);
    }

    /// <summary>Required for every engagement except a tax engagement priced at or under the ceiling, unless the CSE is the tax exception.</summary>
    private static bool NeedsManagingShareholder(REMSEngagement engagement, RemsApprovalPolicySnapshot policy, bool cseIsException)
    {
        if (cseIsException)
        {
            return true;
        }
        if (!RemsEngagementCodes.IsTax(engagement.Department?.Value))
        {
            return true;
        }
        // No fee entered reads as "not known to be small", so the shareholder is asked.
        return engagement.FirstYearFeeEstimate is not { } fee || fee > policy.TaxFeeCeilingWithoutManagingShareholder;
    }

    /// <summary>The users an engagement's seats reserve: nobody in them may also receive commission or be picked as an extra approver.</summary>
    public static IReadOnlyList<Guid> ReservedUserIds(REMSEngagement engagement, RemsApprovalPolicySnapshot policy)
        => new[] { engagement.Rems?.CSEId, engagement.DepartmentDirectorId, policy.ManagingShareholder?.Id }
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();

    /// <summary>The stage a set of roles reads as, for a round already on file.</summary>
    public static string StageNameOf(IEnumerable<RemsApproverRole> roles)
    {
        var set = roles.ToHashSet();
        if (set.Contains(RemsApproverRole.CommissionRecipient)) return StageCommission;
        if (set.Contains(RemsApproverRole.CSE)) return StageCse;
        if (set.Contains(RemsApproverRole.Shareholder) && set.Count == 1) return StageManagingShareholder;
        return StageDepartmentDirector;
    }

    private static RemsApprovalRoute Blocked(string reason) => new(Array.Empty<RemsApprovalStage>(), reason);
}
