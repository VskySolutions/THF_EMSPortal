using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class RemsApprovalTaskRepository : IRemsApprovalTaskRepository
{
    private const string PartiallyApproved = "partially_approved";

    private readonly EmsPortalDbContext _dbContext;
    private readonly MaconomyClientReader _maconomy;

    public RemsApprovalTaskRepository(EmsPortalDbContext dbContext, MaconomyClientReader maconomy)
    {
        _dbContext = dbContext;
        _maconomy = maconomy;
    }

    private IQueryable<RemsApprovalTask> Tasks => _dbContext.RemsApprovalTasks;

    public Task<RemsApprovalTask?> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Tasks.Include(t => t.ChecklistItems.Where(i => !i.Deleted))
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<RemsApprovalTask?> GetTaskWithContextAsync(Guid id, CancellationToken cancellationToken = default)
        => Tasks
            .Include(t => t.ChecklistItems.Where(i => !i.Deleted))
            .Include(t => t.Approver)
            .Include(t => t.Request).ThenInclude(r => r!.ApprovalTasks.Where(o => !o.Deleted)).ThenInclude(o => o.ChecklistItems.Where(i => !i.Deleted))
            .Include(t => t.Request).ThenInclude(r => r!.Type)
            .Include(t => t.Request).ThenInclude(r => r!.Status)
            .Include(t => t.Request).ThenInclude(r => r!.Department)
            .Include(t => t.Request).ThenInclude(r => r!.ServiceLine)
            .Include(t => t.Request).ThenInclude(r => r!.JobTemplate)
            .Include(t => t.Request).ThenInclude(r => r!.BillingPeriod)
            .Include(t => t.Request).ThenInclude(r => r!.Contract)
            .Include(t => t.Request).ThenInclude(r => r!.Files.Where(f => !f.Deleted)).ThenInclude(f => f.Media)
            .Include(t => t.Request).ThenInclude(r => r!.PersonnelRates.Where(x => !x.Deleted)).ThenInclude(x => x.PersonnelLevel)
            .Include(t => t.Request).ThenInclude(r => r!.MarketingMethods.Where(x => !x.Deleted)).ThenInclude(x => x.MarketingMethod)
            .Include(t => t.Request).ThenInclude(r => r!.CommissionSplits.Where(x => !x.Deleted))
            .Include(t => t.Request).ThenInclude(r => r!.TaxForms.Where(x => !x.Deleted)).ThenInclude(x => x.TaxForm)
            .Include(t => t.Request).ThenInclude(r => r!.TaxDueDates.Where(x => !x.Deleted))
            .Include(t => t.Request).ThenInclude(r => r!.Approvers.Where(x => !x.Deleted))
            .Include(t => t.Request).ThenInclude(r => r!.Client).ThenInclude(c => c!.Person)
            .Include(t => t.Request).ThenInclude(r => r!.Client).ThenInclude(c => c!.EntityType)
            .Include(t => t.Request).ThenInclude(r => r!.Client).ThenInclude(c => c!.Industry)
            .Include(t => t.Request).ThenInclude(r => r!.Client).ThenInclude(c => c!.ReferralSource)
            .Include(t => t.Request).ThenInclude(r => r!.Client).ThenInclude(c => c!.Addresses.Where(a => !a.Deleted)).ThenInclude(a => a.Address)
            .Include(t => t.Request).ThenInclude(r => r!.Client).ThenInclude(c => c!.Addresses.Where(a => !a.Deleted)).ThenInclude(a => a.AddressType)
            .Include(t => t.Request).ThenInclude(r => r!.Client).ThenInclude(c => c!.Contacts.Where(x => !x.Deleted)).ThenInclude(x => x.Person)
            .Include(t => t.Request).ThenInclude(r => r!.Client).ThenInclude(c => c!.Contacts.Where(x => !x.Deleted)).ThenInclude(x => x.ContactRole)
            .Include(t => t.Request).ThenInclude(r => r!.Client).ThenInclude(c => c!.Relationships.Where(x => !x.Deleted)).ThenInclude(x => x.RelatedClient).ThenInclude(x => x!.Person)
            .Include(t => t.Request).ThenInclude(r => r!.Client).ThenInclude(c => c!.Relationships.Where(x => !x.Deleted)).ThenInclude(x => x.RelationType)
            .Include(t => t.Request).ThenInclude(r => r!.Client).ThenInclude(c => c!.Relationships.Where(x => !x.Deleted)).ThenInclude(x => x.FilingType)
            .Include(t => t.Request).ThenInclude(r => r!.Client).ThenInclude(c => c!.Relationships.Where(x => !x.Deleted)).ThenInclude(x => x.BillingPreference)
            .Include(t => t.Request).ThenInclude(r => r!.Client).ThenInclude(c => c!.Relationships.Where(x => !x.Deleted)).ThenInclude(x => x.RelatedStatus)
            .Include(t => t.Request).ThenInclude(r => r!.Client).ThenInclude(c => c!.Contracts.Where(x => !x.Deleted))
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<RemsApprovalTask>> ListByRequestAsync(Guid remsId, CancellationToken cancellationToken = default)
        => await Tasks
            .Include(t => t.ChecklistItems.Where(i => !i.Deleted))
            .Include(t => t.Approver)
            .Where(t => t.REMSId == remsId)
            .OrderBy(t => t.RoundNumber).ThenBy(t => t.CreatedOnUtc).ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);

    public async Task<int> GetNextRoundNumberAsync(Guid remsId, CancellationToken cancellationToken = default)
    {
        var max = await Tasks.IgnoreQueryFilters()
            .Where(t => t.REMSId == remsId)
            .Select(t => (int?)t.RoundNumber)
            .MaxAsync(cancellationToken);
        return (max ?? 0) + 1;
    }

    // Whose inbox: the approver's latest task per request — nothing newer on the same request by round,
    // then by id — or, for a Tenant Admin or Super Admin reading the whole tenant, every approver's task
    // on the round each request is on now.
    private IQueryable<RemsApprovalTask> InboxOf(Guid approverId, Guid? tenantId)
    {
        if (tenantId is { } tenant)
        {
            var all = Tasks.Where(t => t.TenantId == tenant);
            return all.Where(t => !all.Any(other => other.REMSId == t.REMSId && other.RoundNumber > t.RoundNumber));
        }
        var mine = Tasks.Where(t => t.ApproverId == approverId);
        return mine.Where(t => !mine.Any(other =>
            other.REMSId == t.REMSId
            && (other.RoundNumber > t.RoundNumber || (other.RoundNumber == t.RoundNumber && other.Id > t.Id))));
    }

    // The ROWS of that inbox. The approver's own is one task per request already. The tenant-wide one is
    // read per request too: one row per round, carried by the task updated last — so the row's audit
    // columns name who last acted on the round, and the default order puts the latest decisions first.
    private IQueryable<RemsApprovalTask> InboxRowsOf(Guid approverId, Guid? tenantId)
    {
        var tasks = InboxOf(approverId, tenantId);
        if (tenantId is null)
        {
            return tasks;
        }
        return tasks.Where(t => !tasks.Any(other =>
            other.REMSId == t.REMSId
            && (other.UpdatedOnUtc > t.UpdatedOnUtc || (other.UpdatedOnUtc == t.UpdatedOnUtc && other.Id > t.Id))));
    }

    public async Task<IReadOnlyList<Guid>> ListInboxCseIdsAsync(Guid approverId, Guid? tenantId, CancellationToken cancellationToken = default)
        => await InboxOf(approverId, tenantId)
            .Where(t => t.Request!.CSEId != null)
            .Select(t => t.Request!.CSEId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListInboxApproverIdsAsync(Guid approverId, Guid? tenantId, CancellationToken cancellationToken = default)
        => await InboxOf(approverId, tenantId)
            .Select(t => t.ApproverId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RemsClientChoice>> ListInboxClientsAsync(Guid approverId, Guid? tenantId, CancellationToken cancellationToken = default)
    {
        var clients = await InboxOf(approverId, tenantId)
            .Select(t => new
            {
                Id = t.Request!.ClientId,
                Name = t.Request!.Client!.Person != null ? t.Request!.Client!.Person.ClientDisplayName : t.Request!.Client!.LegalName ?? string.Empty,
                Maconomy = t.Request!.Client!.MaconomyCustomerNumber,
            })
            .Distinct()
            .ToListAsync(cancellationToken);
        return _maconomy.Choices(clients.Select(c => (c.Id, c.Name, c.Maconomy)));
    }

    private IQueryable<RemsApprovalTask> InboxTasks(RemsApprovalTaskQuery query, bool withRoundStatus, bool withStatus)
    {
        var tasks = InboxRowsOf(query.ApproverId, query.TenantId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var t = query.Search.Trim();
            var maconomy = _maconomy.NumbersByName(t);
            tasks = tasks.Where(x =>
                x.Request!.RemsNumber.Contains(t)
                || (x.Request!.Client!.Person != null
                    && (x.Request!.Client!.Person.ClientDisplayName.Contains(t)
                        || x.Request!.Client!.Person.FirstName.Contains(t)
                        || x.Request!.Client!.Person.LastName.Contains(t)))
                || (x.Request!.Client!.LegalName != null && x.Request!.Client!.LegalName.Contains(t))
                || (x.Request!.Client!.MaconomyCustomerNumber != null && maconomy.Contains(x.Request!.Client!.MaconomyCustomerNumber)));
        }
        if (query.Role is { } role)
        {
            tasks = tasks.Where(t => t.ApproverRole == role);
        }
        if (query.ApproverUserId is { } approverId)
        {
            // A tenant-wide row is a request: the round it is on has to hold the approver's task.
            tasks = query.TenantId is null
                ? tasks.Where(t => t.ApproverId == approverId)
                : tasks.Where(t => _dbContext.RemsApprovalTasks.Any(o =>
                    o.REMSId == t.REMSId && o.RoundNumber == t.RoundNumber && o.ApproverId == approverId));
        }
        // A decision belongs to a task; a request row has none to filter by.
        if (withStatus && query.Status is { } status && query.TenantId is null)
        {
            tasks = tasks.Where(t => t.Status == status);
        }
        if (query.CseUserId is { } cseId)
        {
            tasks = tasks.Where(x => x.Request!.CSEId == cseId);
        }
        if (query.ClientPersonIds is { Count: > 0 } clientIds)
        {
            tasks = tasks.Where(x => clientIds.Contains(x.Request!.ClientId));
        }
        if (query.SentFromUtc is { } sentFrom)
        {
            tasks = tasks.Where(t => t.CreatedOnUtc >= sentFrom);
        }
        if (query.SentToUtc is { } sentTo)
        {
            tasks = tasks.Where(t => t.CreatedOnUtc <= sentTo);
        }
        if (query.DecidedFromUtc is { } decidedFrom)
        {
            tasks = tasks.Where(t => t.DecidedOnUtc >= decidedFrom);
        }
        if (query.DecidedToUtc is { } decidedTo)
        {
            tasks = tasks.Where(t => t.DecidedOnUtc <= decidedTo);
        }
        if (query.CreatedFromUtc is { } createdFrom)
        {
            tasks = tasks.Where(t => t.CreatedOnUtc >= createdFrom);
        }
        if (query.CreatedToUtc is { } createdTo)
        {
            tasks = tasks.Where(t => t.CreatedOnUtc <= createdTo);
        }
        if (query.UpdatedFromUtc is { } updatedFrom)
        {
            tasks = tasks.Where(t => t.UpdatedOnUtc >= updatedFrom);
        }
        if (query.UpdatedToUtc is { } updatedTo)
        {
            tasks = tasks.Where(t => t.UpdatedOnUtc <= updatedTo);
        }
        if (withRoundStatus && !string.IsNullOrWhiteSpace(query.RoundStatus))
        {
            tasks = WhereRoundStatus(tasks, query.RoundStatus);
        }
        return tasks;
    }

    // A round's outcome, from its tasks: one decline rejects it, every approval approves it, otherwise it is pending.
    private IQueryable<RemsApprovalTask> WhereRoundStatus(IQueryable<RemsApprovalTask> tasks, string value)
    {
        var all = _dbContext.RemsApprovalTasks;
        var wanted = value.Trim();
        if (wanted.Equals(PartiallyApproved, StringComparison.OrdinalIgnoreCase))
        {
            return tasks.Where(x =>
                !all.Any(o => o.REMSId == x.REMSId && o.RoundNumber == x.RoundNumber && o.Status == RemsApprovalTaskStatus.Rejected)
                && all.Any(o => o.REMSId == x.REMSId && o.RoundNumber == x.RoundNumber && o.Status != RemsApprovalTaskStatus.Approved)
                && all.Any(o => o.REMSId == x.REMSId && o.RoundNumber == x.RoundNumber && o.Status == RemsApprovalTaskStatus.Approved));
        }
        if (!Enum.TryParse<RemsApprovalRoundStatus>(wanted, true, out var roundStatus))
        {
            return tasks.Where(x => false);
        }
        return roundStatus switch
        {
            RemsApprovalRoundStatus.Rejected => tasks.Where(x =>
                all.Any(o => o.REMSId == x.REMSId && o.RoundNumber == x.RoundNumber && o.Status == RemsApprovalTaskStatus.Rejected)),
            RemsApprovalRoundStatus.Approved => tasks.Where(x =>
                !all.Any(o => o.REMSId == x.REMSId && o.RoundNumber == x.RoundNumber && o.Status != RemsApprovalTaskStatus.Approved)),
            _ => tasks.Where(x =>
                !all.Any(o => o.REMSId == x.REMSId && o.RoundNumber == x.RoundNumber && o.Status == RemsApprovalTaskStatus.Rejected)
                && all.Any(o => o.REMSId == x.REMSId && o.RoundNumber == x.RoundNumber && o.Status != RemsApprovalTaskStatus.Approved)
                && !all.Any(o => o.REMSId == x.REMSId && o.RoundNumber == x.RoundNumber && o.Status == RemsApprovalTaskStatus.Approved)),
        };
    }

    public async Task<RemsApprovalTaskQuickCounts> CountInboxQuickFiltersAsync(RemsApprovalTaskQuery query, CancellationToken cancellationToken = default)
    {
        var forRound = InboxTasks(query, withRoundStatus: false, withStatus: true);
        var round = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var value in new[]
        {
            nameof(RemsApprovalRoundStatus.Pending), PartiallyApproved,
            nameof(RemsApprovalRoundStatus.Approved), nameof(RemsApprovalRoundStatus.Rejected),
        })
        {
            round[value] = await WhereRoundStatus(forRound, value).CountAsync(cancellationToken);
        }
        // No decision counts on the tenant-wide inbox: its rows are requests, which hold no decision of their own.
        var decision = new Dictionary<string, int>(StringComparer.Ordinal);
        if (query.TenantId is null)
        {
            var forDecision = InboxTasks(query, withRoundStatus: true, withStatus: false);
            foreach (var status in new[] { RemsApprovalTaskStatus.Pending, RemsApprovalTaskStatus.Approved })
            {
                decision[status.ToString()] = await forDecision.Where(t => t.Status == status).CountAsync(cancellationToken);
            }
        }
        return new RemsApprovalTaskQuickCounts(round, decision);
    }

    public async Task<(IReadOnlyList<RemsApprovalTask> Items, int Total)> ListTasksByApproverAsync(RemsApprovalTaskQuery query, CancellationToken cancellationToken = default)
    {
        var all = _dbContext.RemsApprovalTasks;
        var tasks = InboxTasks(query, withRoundStatus: true, withStatus: true);
        var total = await tasks.CountAsync(cancellationToken);
        var actors = ActorNames.Of(_dbContext);
        var withGraph = tasks
            .Include(t => t.Request).ThenInclude(r => r!.Status)
            .Include(t => t.Request).ThenInclude(r => r!.Type)
            .Include(t => t.Request).ThenInclude(r => r!.Client).ThenInclude(c => c!.Person)
            .Include(t => t.Request).ThenInclude(r => r!.ApprovalTasks.Where(o => !o.Deleted));
        var sorts = SortMap.For(withGraph, "updatedOnUtc")
            .Add("remsNumber", t => t.Request!.RemsNumber)
            .Add("client", t => t.Request!.Client!.Person != null ? t.Request!.Client!.Person.ClientDisplayName : t.Request!.Client!.LegalName, t => t.UpdatedOnUtc)
            .Add("cse", t => actors.Where(a => a.Id == t.Request!.CSEId).Select(a => a.Name).FirstOrDefault(), t => t.UpdatedOnUtc)
            .Add(
                "roundStatus",
                t => all.Any(o => o.REMSId == t.REMSId && o.RoundNumber == t.RoundNumber && o.Status == RemsApprovalTaskStatus.Rejected) ? 2
                    : !all.Any(o => o.REMSId == t.REMSId && o.RoundNumber == t.RoundNumber && o.Status != RemsApprovalTaskStatus.Approved) ? 1
                    : 0,
                t => all.Any(o => o.REMSId == t.REMSId && o.RoundNumber == t.RoundNumber && o.Status == RemsApprovalTaskStatus.Approved))
            .Add("status", t => t.Status, t => t.UpdatedOnUtc)
            .Add("approvals", t => all.Count(o => o.REMSId == t.REMSId && o.RoundNumber == t.RoundNumber && o.Status != RemsApprovalTaskStatus.Approved), t => t.UpdatedOnUtc)
            .Add("sentOnUtc", t => t.CreatedOnUtc, t => t.UpdatedOnUtc)
            .Add("decidedOnUtc", t => t.DecidedOnUtc, t => t.UpdatedOnUtc)
            .Add("createdBy", t => actors.Where(a => a.Id == t.CreatedById).Select(a => a.Name).FirstOrDefault(), t => t.UpdatedOnUtc)
            .Add("updatedBy", t => actors.Where(a => a.Id == t.UpdatedById).Select(a => a.Name).FirstOrDefault(), t => t.UpdatedOnUtc)
            .Add("createdOnUtc", t => t.CreatedOnUtc)
            .Add("updatedOnUtc", t => t.UpdatedOnUtc, t => t.CreatedOnUtc);
        if (string.Equals(query.Sort.SortBy, "client", StringComparison.OrdinalIgnoreCase)
            && await tasks.AnyAsync(t => t.Request!.Client!.MaconomyCustomerNumber != null, cancellationToken))
        {
            var keys = await tasks
                .Select(t => new ClientSortKey<DateTime>(
                    t.Id,
                    t.Request!.Client!.Person != null ? t.Request!.Client!.Person.ClientDisplayName : t.Request!.Client!.LegalName,
                    t.Request!.Client!.MaconomyCustomerNumber, t.UpdatedOnUtc))
                .ToListAsync(cancellationToken);
            var ids = _maconomy.PageByName(keys, query.Sort.Descending, query.Page, query.Limit);
            var page = await withGraph.Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken);
            return (ids.Join(page, id => id, t => t.Id, (_, t) => t).ToList(), total);
        }
        var items = await sorts.Apply(withGraph, query.Sort.SortBy, query.Sort.Descending)
            .Skip((query.Page - 1) * query.Limit)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<bool> IsApproverOnRequestAsync(Guid remsId, Guid userId, CancellationToken cancellationToken = default)
        => Tasks.AnyAsync(t => t.ApproverId == userId && t.REMSId == remsId, cancellationToken);

    public async Task<IReadOnlySet<Guid>> RequestsWithApproverAsync(IReadOnlyCollection<Guid> remsIds, Guid userId, CancellationToken cancellationToken = default)
    {
        var wanted = remsIds.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return new HashSet<Guid>();
        }
        var found = await Tasks
            .Where(t => t.ApproverId == userId && wanted.Contains(t.REMSId))
            .Select(t => t.REMSId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return found.ToHashSet();
    }

    public Task<Guid?> GetCurrentTaskIdOnRequestAsync(Guid remsId, Guid userId, CancellationToken cancellationToken = default)
        => Tasks
            .Where(t => t.ApproverId == userId && t.REMSId == remsId)
            .OrderByDescending(t => t.RoundNumber)
            .ThenByDescending(t => t.Id)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddTaskAsync(RemsApprovalTask task, CancellationToken cancellationToken = default)
        => await _dbContext.RemsApprovalTasks.AddAsync(task, cancellationToken);

    public void UpdateTask(RemsApprovalTask task) => _dbContext.RemsApprovalTasks.Update(task);

    public async Task AddChecklistItemAsync(RemsApprovalChecklistItem item, CancellationToken cancellationToken = default)
        => await _dbContext.RemsApprovalChecklistItems.AddAsync(item, cancellationToken);

    public void UpdateChecklistItem(RemsApprovalChecklistItem item) => _dbContext.RemsApprovalChecklistItems.Update(item);
}
