using EmsPortal.Domain.Entities;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// The approvals-inbox query (WO-117): quick search over the REMS number and client name, optional
/// narrowing by the role they act in and their decision state.
/// </summary>
public sealed record RemsApprovalTaskQuery(
    Guid ApproverId,
    string? Search,
    RemsApproverRole? Role,
    RemsApprovalTaskStatus? Status,
    SortRequest Sort,
    int Page,
    int Limit);

/// <summary>
/// Data access for the REMS approval chain (WO-110): immutable rounds, per-approver tasks and their
/// checklist items.
/// </summary>
public interface IRemsApprovalRepository
{
    /// <summary>The round with its tasks and their checklist items loaded.</summary>
    Task<REMSApprovalRound?> GetRoundByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>All rounds (history) for an engagement, newest round first, with tasks/checklists loaded.</summary>
    Task<IReadOnlyList<REMSApprovalRound>> GetRoundsByEngagementAsync(Guid engagementId, CancellationToken cancellationToken = default);

    /// <summary>The next 1-based round number for an engagement (max existing round number + 1).</summary>
    Task<int> GetNextRoundNumberAsync(Guid engagementId, CancellationToken cancellationToken = default);

    /// <summary>The task with its checklist items loaded.</summary>
    Task<REMSApprovalTask?> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The task with its full decision context (WO-114): its checklist, its round (and all sibling
    /// tasks in the round).
    /// </summary>
    Task<REMSApprovalTask?> GetTaskWithContextAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A page of the caller's approvals inbox — ONE task per request.</summary>
    Task<(IReadOnlyList<REMSApprovalTask> Items, int Total)> ListTasksByApproverAsync(
        RemsApprovalTaskQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this user has ever been routed an approval task on any engagement of this request —
    /// in any round, whatever they decided or whether they decided at all.
    /// </summary>
    Task<bool> IsApproverOnRequestAsync(Guid remsId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The id of the caller's CURRENT task on a request — their own task on the latest round they
    /// were routed — or null if they were never an approver on it.
    /// </summary>
    Task<Guid?> GetCurrentTaskIdOnRequestAsync(Guid remsId, Guid userId, CancellationToken cancellationToken = default);

    Task AddRoundAsync(REMSApprovalRound round, CancellationToken cancellationToken = default);

    void UpdateRound(REMSApprovalRound round);

    Task AddTaskAsync(REMSApprovalTask task, CancellationToken cancellationToken = default);

    void UpdateTask(REMSApprovalTask task);

    Task AddChecklistItemAsync(REMSApprovalChecklistItem item, CancellationToken cancellationToken = default);

    void UpdateChecklistItem(REMSApprovalChecklistItem item);
}
