using EmsPortal.Domain.Entities;

namespace EmsPortal.Application.Abstractions.Persistence;

/// <summary>
/// Approval tasks and their checklists. There is no round table: a round is the tasks that share a
/// <see cref="RemsApprovalTask.RoundNumber"/>, summarised by <see cref="RemsApprovalRounds"/>.
/// </summary>
public interface IRemsApprovalTaskRepository
{
    /// <summary>The task with its checklist.</summary>
    Task<RemsApprovalTask?> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The task with its checklist and its request's full workspace graph, including every task of the
    /// request so the round can be summarised.
    /// </summary>
    Task<RemsApprovalTask?> GetTaskWithContextAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every task on a request, with checklists, oldest round first.</summary>
    Task<IReadOnlyList<RemsApprovalTask>> ListByRequestAsync(Guid remsId, CancellationToken cancellationToken = default);

    Task<int> GetNextRoundNumberAsync(Guid remsId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The inbox's filter choices, read off the inbox itself: the caller's own tasks, or every approver's
    /// in the tenant when <paramref name="tenantId"/> is set (a Tenant Admin or Super Admin).
    /// </summary>
    Task<IReadOnlyList<Guid>> ListInboxCseIdsAsync(Guid approverId, Guid? tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemsClientChoice>> ListInboxClientsAsync(Guid approverId, Guid? tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListInboxApproverIdsAsync(Guid approverId, Guid? tenantId, CancellationToken cancellationToken = default);
    Task<RemsApprovalTaskQuickCounts> CountInboxQuickFiltersAsync(RemsApprovalTaskQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// The approver's current task per request — or, for a query scoped to the tenant, one row per request
    /// on its current round, carried by the round's last-updated task — with the request, its client and
    /// the sibling tasks of the round.
    /// </summary>
    Task<(IReadOnlyList<RemsApprovalTask> Items, int Total)> ListTasksByApproverAsync(RemsApprovalTaskQuery query, CancellationToken cancellationToken = default);

    Task<bool> IsApproverOnRequestAsync(Guid remsId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<Guid>> RequestsWithApproverAsync(IReadOnlyCollection<Guid> remsIds, Guid userId, CancellationToken cancellationToken = default);
    Task<Guid?> GetCurrentTaskIdOnRequestAsync(Guid remsId, Guid userId, CancellationToken cancellationToken = default);

    Task AddTaskAsync(RemsApprovalTask task, CancellationToken cancellationToken = default);
    void UpdateTask(RemsApprovalTask task);
    Task AddChecklistItemAsync(RemsApprovalChecklistItem item, CancellationToken cancellationToken = default);
    void UpdateChecklistItem(RemsApprovalChecklistItem item);
}
