using EmsPortal.Domain.Entities;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// The approvals-inbox query (WO-117): quick search over the REMS number and client name, optional
/// narrowing by the role they act in and their decision state. Whose inbox it is comes from
/// <see cref="ApproverId"/> — or, with <see cref="TenantId"/> set, it is the whole tenant's.
/// </summary>
public sealed record RemsApprovalTaskQuery(
    Guid ApproverId,
    string? Search,
    RemsApproverRole? Role,
    RemsApprovalTaskStatus? Status,
    SortRequest Sort,
    int Page,
    int Limit,
    /// <summary>
    /// Where the whole ROUND stands, as the inbox badge says it: a <see cref="RemsApprovalRoundStatus"/>
    /// name, or <c>partially_approved</c> for a pending round some approvers have already signed.
    /// </summary>
    string? RoundStatus = null,
    /// <summary>The CSE named on the request.</summary>
    Guid? CseUserId = null,
    /// <summary>The clients the request is for — any of them. The Client column's dropdown.</summary>
    IReadOnlyList<Guid>? ClientPersonIds = null,
    DateTime? SentFromUtc = null,
    DateTime? SentToUtc = null,
    DateTime? DecidedFromUtc = null,
    DateTime? DecidedToUtc = null,
    DateTime? CreatedFromUtc = null,
    DateTime? CreatedToUtc = null,
    DateTime? UpdatedFromUtc = null,
    DateTime? UpdatedToUtc = null,
    /// <summary>
    /// Set for a Tenant Admin or Super Admin: the inbox is then every approver's task on the round each of
    /// the tenant's requests is on now, not the caller's own tasks.
    /// </summary>
    Guid? TenantId = null,
    /// <summary>One approver's tasks only — the Approver column's dropdown on the tenant-wide inbox.</summary>
    Guid? ApproverUserId = null);

/// <summary>
/// How many rows each quick-filter button on the Approvals inbox would list: the Approval Status group by
/// round standing (<c>partially_approved</c> included) and the Your Decision group by task status. Each
/// group is counted under every filter but its own, so a button's number is the rows clicking it produces.
/// </summary>
public sealed record RemsApprovalTaskQuickCounts(
    IReadOnlyDictionary<string, int> RoundStatus,
    IReadOnlyDictionary<string, int> Status);

