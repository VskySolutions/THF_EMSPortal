using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class RemsApprovalRepository : IRemsApprovalRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public RemsApprovalRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<REMSApprovalRound?> GetRoundByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.RemsApprovalRounds
            .Include(r => r.Tasks).ThenInclude(t => t.ChecklistItems)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<REMSApprovalRound>> GetRoundsByEngagementAsync(Guid engagementId, CancellationToken cancellationToken = default)
        => await _dbContext.RemsApprovalRounds
            .Include(r => r.Tasks).ThenInclude(t => t.ChecklistItems)
            .Where(r => r.REMSEngagementId == engagementId)
            .OrderByDescending(r => r.RoundNumber)
            .ToListAsync(cancellationToken);

    public async Task<int> GetNextRoundNumberAsync(Guid engagementId, CancellationToken cancellationToken = default)
    {
        // Round numbers are immutable history and must never be reused, so include soft-deleted rows.
        var max = await _dbContext.RemsApprovalRounds
            .IgnoreQueryFilters()
            .Where(r => r.REMSEngagementId == engagementId)
            .Select(r => (int?)r.RoundNumber)
            .MaxAsync(cancellationToken);
        return (max ?? 0) + 1;
    }

    public Task<REMSApprovalTask?> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.RemsApprovalTasks
            .Include(t => t.ChecklistItems)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<REMSApprovalTask?> GetTaskWithContextAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.RemsApprovalTasks
            .Include(t => t.ChecklistItems)
            .Include(t => t.Round).ThenInclude(r => r!.Tasks)
            .Include(t => t.Round).ThenInclude(r => r!.Engagement).ThenInclude(e => e!.CommissionSplits)
            .Include(t => t.Round).ThenInclude(r => r!.Engagement).ThenInclude(e => e!.MarketingMethods)
            .Include(t => t.Round).ThenInclude(r => r!.Engagement).ThenInclude(e => e!.Rems)
                .ThenInclude(r => r!.Clients).ThenInclude(c => c.Entities)
            // The engagement's own option references, and the request's status -- the packet renders the
            // codes behind all of them.
            .Include(t => t.Round).ThenInclude(r => r!.Engagement).ThenInclude(e => e!.Department)
            .Include(t => t.Round).ThenInclude(r => r!.Engagement).ThenInclude(e => e!.ServiceLine)
            .Include(t => t.Round).ThenInclude(r => r!.Engagement).ThenInclude(e => e!.Industry)
            .Include(t => t.Round).ThenInclude(r => r!.Engagement).ThenInclude(e => e!.BillingPeriod)
            .Include(t => t.Round).ThenInclude(r => r!.Engagement).ThenInclude(e => e!.Rems).ThenInclude(r => r!.Status)
            .Include(t => t.Round).ThenInclude(r => r!.Engagement).ThenInclude(e => e!.Rems).ThenInclude(r => r!.Type)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    // The round's sibling tasks come along so the inbox can show how far the round has got (n of m
    // approved). The caller's checklist is deliberately NOT loaded: the inbox lists requests, not
    // checkboxes, and a second collection include would multiply the result set for nothing.
    public async Task<(IReadOnlyList<REMSApprovalTask> Items, int Total)> ListTasksByApproverAsync(
        RemsApprovalTaskQuery query, CancellationToken cancellationToken = default)
    {
        var mine = _dbContext.RemsApprovalTasks.Where(t => t.ApproverId == query.ApproverId);

        // ONE row per request, not one per round. A request re-routed after a rejection opens a new round
        // with a new task, so an approver asked three times held three tasks and the inbox listed all
        // three — burying the round that actually wanted them between two that were finished. Only their
        // task on the newest round survives; the rounds before it are read on the task detail.
        //
        // Newest of THEIR OWN tasks, and deliberately not the request's newest round: an approver dropped
        // from a later round — a commission recipient taken off the split — holds no task on it, and
        // measuring against the request's newest round would drop that request out of their inbox
        // altogether instead of leaving them the last round they were actually on.
        //
        // Written as "no task of mine on this request outranks this one" so it translates to a correlated
        // NOT EXISTS rather than a grouped projection. The Id tie-break is insurance only: the approver
        // list is de-duplicated per user before a round is built, so no round hands one person two tasks.
        var tasks = mine.Where(t => !mine.Any(other =>
            other.Round!.Engagement!.REMSId == t.Round!.Engagement!.REMSId
            && (other.Round!.RoundNumber > t.Round!.RoundNumber
                || (other.Round!.RoundNumber == t.Round!.RoundNumber && other.Id > t.Id))));

        // Filtered AFTER the collapse, so role and status narrow on what the caller is to the request now
        // rather than resurrecting a superseded round that happens to match.
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var t = query.Search.Trim();
            // The entity's own name is deliberately not searched: an approval is about a request and its
            // one engagement, and the request already carries the client name it was raised under.
            //
            // Against the client's name as the row SHOWS it — "Smith John Jr." — and against each half of
            // it on its own, like the other REMS lists: surname first is how it reads, but nobody hunting
            // for John Smith types "Smith John".
            tasks = tasks.Where(x =>
                x.Round!.Engagement!.Rems!.REMSNumber.Contains(t) ||
                x.Round!.Engagement!.Rems!.ClientPerson!.ClientDisplayName.Contains(t) ||
                x.Round!.Engagement!.Rems!.ClientPerson!.FirstName.Contains(t) ||
                x.Round!.Engagement!.Rems!.ClientPerson!.LastName.Contains(t));
        }
        if (query.Role is { } role)
        {
            tasks = tasks.Where(t => t.ApproverRole == role);
        }
        if (query.Status is { } status)
        {
            tasks = tasks.Where(t => t.Status == status);
        }

        // Counted AFTER the filters so the pager reflects the filtered set — and, since the collapse above
        // is part of the same query, it counts REQUESTS rather than every round of every one of them.
        var total = await tasks.CountAsync(cancellationToken);
        // The default is the task's own last touch — a checklist tick or a decision floats it up — with
        // the round's send date as the tie-break for tasks created together and never since touched.
        // Approval Status and CSE are absent: both are assembled by the controller after this query.
        var withGraph = tasks
            .Include(t => t.Round).ThenInclude(r => r!.Tasks)
            .Include(t => t.Round).ThenInclude(r => r!.Engagement).ThenInclude(e => e!.Rems);
        var sorts = SortMap.For(withGraph, "updatedOnUtc")
            .Add("remsNumber", t => t.Round!.Engagement!.Rems!.REMSNumber)
            .Add("client", t => t.Round!.Engagement!.Rems!.ClientPerson!.ClientDisplayName, t => t.UpdatedOnUtc)
            .Add("status", t => t.Status, t => t.UpdatedOnUtc)
            .Add("sentOnUtc", t => t.Round!.SentOnUtc, t => t.UpdatedOnUtc)
            .Add("decidedOnUtc", t => t.DecidedOnUtc, t => t.UpdatedOnUtc)
            .Add("createdOnUtc", t => t.CreatedOnUtc)
            .Add("updatedOnUtc", t => t.UpdatedOnUtc, t => t.Round!.SentOnUtc);

        var items = await sorts.Apply(withGraph, query.Sort.SortBy, query.Sort.Descending)
            .Skip((query.Page - 1) * query.Limit)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    // Every round on every engagement of the request, so a superseded or historical task counts as much as
    // a live one. Tenant scope and soft-deletes come from the ambient query filters on all three entities.
    public Task<bool> IsApproverOnRequestAsync(Guid remsId, Guid userId, CancellationToken cancellationToken = default)
        => _dbContext.RemsApprovalTasks
            .AnyAsync(t => t.ApproverId == userId && t.Round!.Engagement!.REMSId == remsId, cancellationToken);

    // The same task the inbox row for this request opens, picked by the same rule the inbox collapse uses:
    // highest round number of THEIR OWN tasks, id as the tie-break. Projected to the id alone — the caller
    // is deciding where to navigate, and the task detail endpoint loads the packet.
    public Task<Guid?> GetCurrentTaskIdOnRequestAsync(Guid remsId, Guid userId, CancellationToken cancellationToken = default)
        => _dbContext.RemsApprovalTasks
            .Where(t => t.ApproverId == userId && t.Round!.Engagement!.REMSId == remsId)
            .OrderByDescending(t => t.Round!.RoundNumber)
            .ThenByDescending(t => t.Id)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddRoundAsync(REMSApprovalRound round, CancellationToken cancellationToken = default)
        => await _dbContext.RemsApprovalRounds.AddAsync(round, cancellationToken);

    public void UpdateRound(REMSApprovalRound round) => _dbContext.RemsApprovalRounds.Update(round);

    public async Task AddTaskAsync(REMSApprovalTask task, CancellationToken cancellationToken = default)
        => await _dbContext.RemsApprovalTasks.AddAsync(task, cancellationToken);

    public void UpdateTask(REMSApprovalTask task) => _dbContext.RemsApprovalTasks.Update(task);

    public async Task AddChecklistItemAsync(REMSApprovalChecklistItem item, CancellationToken cancellationToken = default)
        => await _dbContext.RemsApprovalChecklistItems.AddAsync(item, cancellationToken);

    public void UpdateChecklistItem(REMSApprovalChecklistItem item) => _dbContext.RemsApprovalChecklistItems.Update(item);
}
