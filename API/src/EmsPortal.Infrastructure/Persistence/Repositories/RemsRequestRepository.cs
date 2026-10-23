using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class RemsRequestRepository : IRemsRequestRepository
{
    private const string JointFiling = "joint";

    private readonly EmsPortalDbContext _dbContext;
    private readonly MaconomyClientReader _maconomy;

    public RemsRequestRepository(EmsPortalDbContext dbContext, MaconomyClientReader maconomy)
    {
        _dbContext = dbContext;
        _maconomy = maconomy;
    }

    private IQueryable<RemsRequest> Requests => _dbContext.RemsRequests;

    // ---- Loads ----

    public Task<RemsRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Requests
            .Include(r => r.Type)
            .Include(r => r.Status)
            .Include(r => r.Client).ThenInclude(c => c!.Person)
            .Include(r => r.Client).ThenInclude(c => c!.EntityType)
            .Include(r => r.Files.Where(f => !f.Deleted)).ThenInclude(f => f.Media)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<RemsRequest?> GetWorkspaceAsync(Guid id, CancellationToken cancellationToken = default)
        => Requests.WorkspaceGraph().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<RemsRequest?> GetByInviteCodeUnscopedAsync(string inviteCode, CancellationToken cancellationToken = default)
        => Requests.IgnoreQueryFilters().WorkspaceGraph()
            .FirstOrDefaultAsync(r => !r.Deleted && r.InviteCode == inviteCode, cancellationToken);

    public Task<bool> InviteCodeExistsAsync(Guid tenantId, string inviteCode, CancellationToken cancellationToken = default)
        => Requests.IgnoreQueryFilters()
            .AnyAsync(r => r.TenantId == tenantId && !r.Deleted && r.InviteCode == inviteCode, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetNumbersAsync(IReadOnlyCollection<Guid> remsIds, CancellationToken cancellationToken = default)
        => remsIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await Requests
                .Where(r => remsIds.Contains(r.Id))
                .Select(r => new { r.Id, r.RemsNumber })
                .ToDictionaryAsync(r => r.Id, r => r.RemsNumber, cancellationToken);

    public async Task<IReadOnlyList<RemsFormStateInfo>> GetFormStatesAsync(IReadOnlyCollection<Guid> remsIds, CancellationToken cancellationToken = default)
    {
        if (remsIds.Count == 0)
        {
            return Array.Empty<RemsFormStateInfo>();
        }
        var rows = await Requests
            .Where(r => remsIds.Contains(r.Id))
            .Select(r => new
            {
                r.Id,
                EntityType = r.Client!.EntityType != null ? r.Client.EntityType.Value : null,
                r.InviteCode,
                r.InviteSentOnUtc,
                r.FormSubmittedOnUtc,
            })
            .ToListAsync(cancellationToken);
        return rows
            .Select(x => new RemsFormStateInfo(
                x.Id, x.EntityType, FormStatusOf(x.InviteCode, x.InviteSentOnUtc, x.FormSubmittedOnUtc),
                x.InviteSentOnUtc, x.FormSubmittedOnUtc, x.FormSubmittedOnUtc != null, x.InviteCode))
            .ToList();
    }

    private static RemsFormStatus? FormStatusOf(string? inviteCode, DateTime? sentOnUtc, DateTime? submittedOnUtc)
        => submittedOnUtc is not null ? RemsFormStatus.Submitted
            : sentOnUtc is not null ? RemsFormStatus.Sent
            : inviteCode is not null ? RemsFormStatus.Saved
            : null;

    public Task<bool> IsClientSharedAsync(Guid clientId, Guid excludingRemsId, CancellationToken cancellationToken = default)
        => Requests.AnyAsync(r => r.Id != excludingRemsId && r.ClientId == clientId, cancellationToken);

    public async Task<IReadOnlyList<RemsRequest>> ListByClientAsync(Guid clientId, CancellationToken cancellationToken = default)
        => await Requests
            .Include(r => r.Type)
            .Include(r => r.Status)
            .Where(r => r.ClientId == clientId)
            .OrderByDescending(r => r.CreatedOnUtc)
            .ToListAsync(cancellationToken);

    // ---- Request lists ----

    public async Task<(IReadOnlyList<RemsRequest> Items, int Total)> ListRequestsAsync(
        RemsRequestListOptions options, CancellationToken cancellationToken = default)
    {
        var query = ApplyFieldFilters(ApplyScope(ApplyVisibility(options), options), options);
        var total = await query.CountAsync(cancellationToken);
        var actors = ActorNames.Of(_dbContext);
        var withOptions = query
            .Include(r => r.Type)
            .Include(r => r.Status)
            .Include(r => r.Client).ThenInclude(c => c!.Person)
            .Include(r => r.Client).ThenInclude(c => c!.EntityType);
        var sorts = SortMap.For(withOptions, "updatedOnUtc")
            .Add("remsNumber", r => r.RemsNumber)
            .Add("type", r => r.Type!.Value, r => r.UpdatedOnUtc)
            .Add("clientName", r => r.Client!.Person != null ? r.Client.Person.ClientDisplayName : r.Client!.LegalName, r => r.RemsNumber)
            .Add("status", r => r.Status!.Value, r => r.UpdatedOnUtc)
            .Add("assignedAdmin", r => actors.Where(a => a.Id == r.AdminAssignedToId).Select(a => a.Name).FirstOrDefault(), r => r.RemsNumber)
            .Add("cse", r => actors.Where(a => a.Id == r.CSEId).Select(a => a.Name).FirstOrDefault(), r => r.RemsNumber)
            .Add("emsFormState", r => r.FormSubmittedOnUtc != null ? 3 : r.InviteSentOnUtc != null ? 2 : r.InviteCode != null ? 1 : 0, r => r.RemsNumber)
            .Add("clientSubmissionState", r => r.FormSubmittedOnUtc != null ? 2 : r.InviteSentOnUtc != null ? 1 : 0, r => r.RemsNumber)
            .Add("entityType", r => r.Client!.EntityType != null ? r.Client.EntityType.Value : null, r => r.RemsNumber)
            .Add("customerEmail", r => r.Client!.PrimaryEmail, r => r.RemsNumber)
            .Add("customerMobileNumber", r => r.Client!.PrimaryPhone, r => r.RemsNumber)
            .Add("createdBy", r => actors.Where(a => a.Id == r.CreatedById).Select(a => a.Name).FirstOrDefault(), r => r.RemsNumber)
            .Add("updatedBy", r => actors.Where(a => a.Id == r.UpdatedById).Select(a => a.Name).FirstOrDefault(), r => r.RemsNumber)
            .Add("createdOnUtc", r => r.CreatedOnUtc)
            .Add("updatedOnUtc", r => r.UpdatedOnUtc, r => r.CreatedOnUtc);
        if (ClientColumnOf(options.Sort.SortBy) is { } column
            && await query.AnyAsync(r => r.Client!.MaconomyCustomerNumber != null, cancellationToken))
        {
            var keys = await query
                .Select(r => new ClientSortKey<string>(
                    r.Id,
                    column == ClientColumn.Email ? r.Client!.PrimaryEmail
                        : column == ClientColumn.Phone ? r.Client!.PrimaryPhone
                        : r.Client!.Person != null ? r.Client.Person.ClientDisplayName : r.Client!.LegalName,
                    r.Client!.MaconomyCustomerNumber,
                    r.RemsNumber))
                .ToListAsync(cancellationToken);
            var ids = column switch
            {
                ClientColumn.Email => _maconomy.PageByEmail(keys, options.Sort.Descending, options.Page, options.Limit),
                ClientColumn.Phone => _maconomy.PageByPhone(keys, options.Sort.Descending, options.Page, options.Limit),
                _ => _maconomy.PageByName(keys, options.Sort.Descending, options.Page, options.Limit),
            };
            var page = await withOptions.Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken);
            return (ids.Join(page, id => id, r => r.Id, (_, r) => r).ToList(), total);
        }
        var items = await sorts.Apply(withOptions, options.Sort.SortBy, options.Sort.Descending)
            .Skip((options.Page - 1) * options.Limit)
            .Take(options.Limit)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    private enum ClientColumn { Name, Email, Phone }

    private static ClientColumn? ClientColumnOf(string? sortBy) => sortBy?.Trim().ToLowerInvariant() switch
    {
        "clientname" => ClientColumn.Name,
        "customeremail" => ClientColumn.Email,
        "customermobilenumber" => ClientColumn.Phone,
        _ => null,
    };

    public async Task<IReadOnlyList<RemsClientChoice>> ListRequestClientsAsync(RemsRequestListOptions options, CancellationToken cancellationToken = default)
        => await ChoicesOf(ApplyScope(ApplyVisibility(options), options), cancellationToken);

    private async Task<IReadOnlyList<RemsClientChoice>> ChoicesOf(IQueryable<RemsRequest> requests, CancellationToken cancellationToken)
    {
        var clients = await requests
            .Select(r => new
            {
                Id = r.ClientId,
                Name = r.Client!.Person != null ? r.Client.Person.ClientDisplayName : r.Client!.LegalName ?? string.Empty,
                Maconomy = r.Client!.MaconomyCustomerNumber,
            })
            .Distinct()
            .ToListAsync(cancellationToken);
        return _maconomy.Choices(clients.Select(c => (c.Id, c.Name, c.Maconomy)));
    }

    public async Task<RemsPoolCounts> CountPoolScopesAsync(RemsRequestListOptions options, CancellationToken cancellationToken = default)
    {
        var me = options.CallerUserId;
        const string draft = RemsRequestStatuses.Draft;
        var query = ApplyFieldFilters(ApplyVisibility(options), options).Where(r => r.Status!.Value != draft);
        var counts = await query
            .GroupBy(_ => 1)
            .Select(g => new RemsPoolCounts(
                g.Count(r => r.AdminAssignedToId == null),
                g.Count(r => r.AdminAssignedToId == me),
                g.Count()))
            .FirstOrDefaultAsync(cancellationToken);
        return counts ?? new RemsPoolCounts(0, 0, 0);
    }

    private IQueryable<RemsRequest> ApplyVisibility(RemsRequestListOptions options)
    {
        var me = options.CallerUserId;
        const string draft = RemsRequestStatuses.Draft;
        return options.CallerIsPrivileged
            ? Requests
            : Requests.Where(r =>
                (r.Status!.Value == draft
                    && (r.CreatedById == me || r.OnBehalfOfUserId == me || r.AdminAssignedToId == me)) ||
                (r.Status!.Value != draft
                    && (r.CreatedById == me || r.OnBehalfOfUserId == me
                        || r.AdminAssignedToId == me || r.CSEId == me)));
    }

    private static IQueryable<RemsRequest> ApplyScope(IQueryable<RemsRequest> query, RemsRequestListOptions options)
    {
        var me = options.CallerUserId;
        const string draft = RemsRequestStatuses.Draft;
        if (options.Scope == RemsListScope.Partner)
        {
            return options.Ownership == RemsListOwnership.All
                ? query
                : query.Where(r => r.CreatedById == me || r.OnBehalfOfUserId == me);
        }
        if (options.Scope != RemsListScope.Pool)
        {
            return query;
        }
        query = query.Where(r => r.Status!.Value != draft);
        return options.PoolFilter switch
        {
            RemsPoolFilter.Unassigned => query.Where(r => r.AdminAssignedToId == null),
            RemsPoolFilter.Mine => query.Where(r => r.AdminAssignedToId == me),
            _ => query,
        };
    }

    private IQueryable<RemsRequest> ApplyFieldFilters(IQueryable<RemsRequest> query, RemsRequestListOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ClientName))
        {
            var t = options.ClientName.Trim();
            query = query.WhereClientNameMatches(t, _maconomy.NumbersByName(t));
        }
        if (!string.IsNullOrWhiteSpace(options.Contact))
        {
            var t = options.Contact.Trim();
            query = query.WhereClientContactMatches(t, _maconomy.NumbersByContact(t));
        }
        if (!string.IsNullOrWhiteSpace(options.Status))
        {
            query = query.WhereStatus(options.Status);
        }
        if (!string.IsNullOrWhiteSpace(options.Type))
        {
            var t = options.Type.Trim();
            query = query.Where(r => r.Type!.Value == t);
        }
        if (options.AssignedAdminUserId is { } adminId)
        {
            query = query.Where(r => r.AdminAssignedToId == adminId);
        }
        if (options.CreatedFromUtc is { } from)
        {
            query = query.Where(r => r.CreatedOnUtc >= from);
        }
        if (options.CreatedToUtc is { } to)
        {
            query = query.Where(r => r.CreatedOnUtc <= to);
        }
        if (options.CseUserId is { } cseId)
        {
            query = query.Where(r => r.CSEId == cseId);
        }
        if (!string.IsNullOrWhiteSpace(options.EntityType))
        {
            var t = options.EntityType.Trim();
            query = query.Where(r => r.Client!.EntityType != null && r.Client.EntityType.Value == t);
        }
        if (!string.IsNullOrWhiteSpace(options.EmsFormState))
        {
            query = query.WhereFormState(options.EmsFormState);
        }
        if (options.ClientPersonIds is { Count: > 0 } clientIds)
        {
            query = query.Where(r => clientIds.Contains(r.ClientId));
        }
        if (!string.IsNullOrWhiteSpace(options.ClientSubmissionState))
        {
            query = query.WhereClientSubmissionState(options.ClientSubmissionState);
        }
        if (options.UpdatedFromUtc is { } updatedFrom)
        {
            query = query.Where(r => r.UpdatedOnUtc >= updatedFrom);
        }
        if (options.UpdatedToUtc is { } updatedTo)
        {
            query = query.Where(r => r.UpdatedOnUtc <= updatedTo);
        }
        return query;
    }

    public async Task<RemsRequestQuickCounts> CountQuickFiltersAsync(RemsRequestListOptions options, CancellationToken cancellationToken = default)
    {
        var scoped = ApplyScope(ApplyVisibility(options), options);
        var forStatus = ApplyFieldFilters(scoped, options with { Status = null });
        var status = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var code in new[] { RemsRequestStatuses.Draft, RemsRequestStatuses.AwaitingCustomer, RemsRequestStatuses.WaitingForPickup })
        {
            status[code] = await forStatus.WhereStatus(code).CountAsync(cancellationToken);
        }
        var forEms = ApplyFieldFilters(scoped, options with { EmsFormState = null });
        var ems = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var state in new[] { nameof(RemsFormStatus.Sent), nameof(RemsFormStatus.Submitted) })
        {
            ems[state] = await forEms.WhereFormState(state).CountAsync(cancellationToken);
        }
        var visible = ApplyVisibility(options);
        var ownership = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, view) in new[] { ("mine", RemsListOwnership.Mine), ("all", RemsListOwnership.All) })
        {
            ownership[key] = await ApplyFieldFilters(ApplyScope(visible, options with { Ownership = view }), options)
                .CountAsync(cancellationToken);
        }
        return new RemsRequestQuickCounts(status, ems, ownership);
    }

    // ---- EMS Review ----

    // Requests past draft that have a form, with the review page's filters.
    private IQueryable<RemsRequest> ReviewRequests(RemsClientFormQuery query, bool withAssignment, bool withRequestStatus, bool withFormFilters)
    {
        const string draft = RemsRequestStatuses.Draft;
        var rems = Requests.Where(r => r.Status!.Value != draft && r.InviteCode != null);
        if (withAssignment && query.Assignment == RemsClientFormAssignment.Mine)
        {
            var me = query.CallerUserId;
            rems = rems.Where(r => r.AdminAssignedToId == me);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var t = query.Search.Trim();
            var maconomy = _maconomy.NumbersByName(t);
            rems = rems.Where(r => r.RemsNumber.Contains(t)).Union(rems.WhereClientNameMatches(t, maconomy));
        }
        if (withRequestStatus && !string.IsNullOrWhiteSpace(query.RequestStatus))
        {
            rems = rems.WhereStatus(query.RequestStatus);
        }
        if (query.AssignedAdminUserId is { } adminId)
        {
            rems = rems.Where(r => r.AdminAssignedToId == adminId);
        }
        if (query.CseUserId is { } cseId)
        {
            rems = rems.Where(r => r.CSEId == cseId);
        }
        if (query.ClientPersonIds is { Count: > 0 } clientIds)
        {
            rems = rems.Where(r => clientIds.Contains(r.ClientId));
        }
        if (query.CreatedFromUtc is { } createdFrom)
        {
            rems = rems.Where(r => r.CreatedOnUtc >= createdFrom);
        }
        if (query.CreatedToUtc is { } createdTo)
        {
            rems = rems.Where(r => r.CreatedOnUtc <= createdTo);
        }
        if (query.UpdatedFromUtc is { } updatedFrom)
        {
            rems = rems.Where(r => r.UpdatedOnUtc >= updatedFrom);
        }
        if (query.UpdatedToUtc is { } updatedTo)
        {
            rems = rems.Where(r => r.UpdatedOnUtc <= updatedTo);
        }
        if (withFormFilters)
        {
            if (query.SubmittedFromUtc is { } from)
            {
                rems = rems.Where(r => r.FormSubmittedOnUtc >= from);
            }
            if (query.SubmittedToUtc is { } to)
            {
                rems = rems.Where(r => r.FormSubmittedOnUtc <= to);
            }
            if (query.Submitted is { } wanted)
            {
                rems = wanted
                    ? rems.Where(r => r.FormSubmittedOnUtc != null)
                    : rems.Where(r => r.FormSubmittedOnUtc == null);
            }
        }
        return rems;
    }

    public async Task<RemsClientFormQuickCounts> CountClientFormQuickFiltersAsync(RemsClientFormQuery query, CancellationToken cancellationToken = default)
    {
        var me = query.CallerUserId;
        var forAssignment = ReviewRequests(query, withAssignment: false, withRequestStatus: true, withFormFilters: true);
        var assignment = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["all"] = await forAssignment.CountAsync(cancellationToken),
            ["mine"] = await forAssignment.Where(r => r.AdminAssignedToId == me).CountAsync(cancellationToken),
        };
        var forStatus = ReviewRequests(query, withAssignment: true, withRequestStatus: false, withFormFilters: true);
        var status = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var code in new[] { RemsRequestStatuses.WaitingForPickup, RemsRequestStatuses.AdminReview, RemsRequestStatuses.AwaitingAdminConfirmation })
        {
            status[code] = await forStatus.WhereStatus(code).CountAsync(cancellationToken);
        }
        return new RemsClientFormQuickCounts(assignment, status);
    }

    public async Task<IReadOnlyList<RemsClientChoice>> ListClientFormClientsAsync(CancellationToken cancellationToken = default)
    {
        const string draft = RemsRequestStatuses.Draft;
        return await ChoicesOf(Requests.Where(r => r.Status!.Value != draft && r.InviteCode != null), cancellationToken);
    }

    public async Task<(IReadOnlyList<RemsClientFormItem> Items, int Total)> ListClientFormsAsync(RemsClientFormQuery query, CancellationToken cancellationToken = default)
    {
        var rows = ReviewRequests(query, withAssignment: true, withRequestStatus: true, withFormFilters: true);
        var total = await rows.CountAsync(cancellationToken);
        var actors = ActorNames.Of(_dbContext);
        var sorts = SortMap.For(rows, "updatedOnUtc")
            .Add("remsNumber", r => r.RemsNumber)
            .Add("clientName", r => r.Client!.Person != null ? r.Client.Person.ClientDisplayName : r.Client!.LegalName, r => r.RemsNumber)
            .Add("submitted", r => r.FormSubmittedOnUtc != null, r => r.UpdatedOnUtc)
            .Add("requestStatus", r => r.Status!.Value, r => r.UpdatedOnUtc)
            .Add("submittedOnUtc", r => r.FormSubmittedOnUtc, r => r.RemsNumber)
            .Add("assignedAdmin", r => actors.Where(a => a.Id == r.AdminAssignedToId).Select(a => a.Name).FirstOrDefault(), r => r.RemsNumber)
            .Add("cse", r => actors.Where(a => a.Id == r.CSEId).Select(a => a.Name).FirstOrDefault(), r => r.RemsNumber)
            .Add("createdBy", r => actors.Where(a => a.Id == r.CreatedById).Select(a => a.Name).FirstOrDefault(), r => r.RemsNumber)
            .Add("updatedBy", r => actors.Where(a => a.Id == r.UpdatedById).Select(a => a.Name).FirstOrDefault(), r => r.RemsNumber)
            .Add("createdOnUtc", r => r.CreatedOnUtc)
            .Add("updatedOnUtc", r => r.UpdatedOnUtc, r => r.RemsNumber);
        IReadOnlyList<Guid>? order = null;
        IQueryable<RemsRequest> page;
        if (string.Equals(query.Sort.SortBy, "clientName", StringComparison.OrdinalIgnoreCase)
            && await rows.AnyAsync(r => r.Client!.MaconomyCustomerNumber != null, cancellationToken))
        {
            var keys = await rows
                .Select(r => new ClientSortKey<string>(
                    r.Id, r.Client!.Person != null ? r.Client.Person.ClientDisplayName : r.Client!.LegalName,
                    r.Client!.MaconomyCustomerNumber, r.RemsNumber))
                .ToListAsync(cancellationToken);
            var ids = order = _maconomy.PageByName(keys, query.Sort.Descending, query.Page, query.Limit);
            page = rows.Where(r => ids.Contains(r.Id));
        }
        else
        {
            page = sorts.Apply(rows, query.Sort.SortBy, query.Sort.Descending)
                .Skip((query.Page - 1) * query.Limit)
                .Take(query.Limit);
        }
        var read = await page
            .Select(r => new RemsClientFormItem(
                r.Id,
                r.RemsNumber,
                r.Client!.Person != null ? r.Client.Person.ClientDisplayName : r.Client!.LegalName ?? string.Empty,
                r.Client!.Person != null ? r.Client.Person.Suffix : null,
                r.Status!.Value,
                r.FormSubmittedOnUtc != null,
                r.FormSubmittedOnUtc,
                r.AdminAssignedToId,
                r.CSEId,
                r.CreatedById,
                r.CreatedOnUtc,
                r.UpdatedById,
                r.UpdatedOnUtc,
                r.Client!.MaconomyCustomerNumber))
            .ToListAsync(cancellationToken);
        var items = (order is null ? read : order.Join(read, id => id, i => i.RemsId, (_, i) => i))
            .Select(i => i.MaconomyCustomerNumber is null ? i : i with { ClientName = _maconomy.Name(i.MaconomyCustomerNumber, i.ClientName) })
            .ToList();
        return (items, total);
    }

    // ---- Form inbox ----

    public async Task<(IReadOnlyList<RemsInboxItem> Items, int Total)> ListInboxAsync(RemsInboxQuery query, CancellationToken cancellationToken = default)
    {
        var forms = Requests.Where(r => r.InviteCode != null);
        if (query.FormState is { } state)
        {
            forms = forms.WhereFormState(state.ToString());
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var t = query.Search.Trim();
            var maconomy = _maconomy.NumbersByName(t);
            forms = forms.Where(r => r.RemsNumber.Contains(t)).Union(forms.WhereClientNameMatches(t, maconomy));
        }
        if (!string.IsNullOrWhiteSpace(query.RequestStatus))
        {
            var s = query.RequestStatus.Trim();
            forms = forms.Where(r => r.Status!.Value == s);
        }
        var total = await forms.CountAsync(cancellationToken);
        var rows = await forms
            .OrderByDescending(r => r.UpdatedOnUtc)
            .ThenByDescending(r => r.FormLastSavedOnUtc)
            .ThenByDescending(r => r.CreatedOnUtc)
            .Skip((query.Page - 1) * query.Limit)
            .Take(query.Limit)
            .Select(r => new
            {
                r.Id,
                r.RemsNumber,
                ClientName = r.Client!.Person != null ? r.Client.Person.ClientDisplayName : r.Client!.LegalName ?? string.Empty,
                Maconomy = r.Client!.MaconomyCustomerNumber,
                EngagementType = r.Type!.Value,
                RequestStatus = r.Status!.Value,
                r.InviteCode,
                r.InviteSentOnUtc,
                r.InviteSentByUserId,
                r.FormLastSavedOnUtc,
                r.FormSubmittedOnUtc,
                r.AdminAssignedToId,
                r.CreatedById,
                r.CreatedOnUtc,
                r.UpdatedById,
                r.UpdatedOnUtc,
                LatestEvent = r.EmailEvents
                    .OrderByDescending(e => e.OccurredOnUtc)
                    .Select(e => new { e.EventType, e.OccurredOnUtc })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        var items = rows
            .Select(x => new RemsInboxItem(
                x.Id,
                x.RemsNumber,
                _maconomy.Name(x.Maconomy, x.ClientName),
                x.EngagementType,
                x.RequestStatus,
                FormStatusOf(x.InviteCode, x.InviteSentOnUtc, x.FormSubmittedOnUtc) ?? RemsFormStatus.Saved,
                x.FormLastSavedOnUtc ?? x.InviteSentOnUtc ?? x.UpdatedOnUtc,
                x.InviteSentByUserId ?? x.CreatedById ?? Guid.Empty,
                x.InviteSentOnUtc,
                x.LatestEvent?.EventType,
                x.LatestEvent?.OccurredOnUtc,
                x.AdminAssignedToId,
                x.CreatedById,
                x.CreatedOnUtc,
                x.UpdatedById,
                x.UpdatedOnUtc))
            .ToList();
        return (items, total);
    }

    // ---- Related entities ----

    // Requests whose client has declared related parties, with the page's filters.
    private IQueryable<RemsRequest> RelatedRequests(RemsRelatedEntityQuery query, bool withEntityType, bool withRelatedStatus)
    {
        var rems = Requests.Where(r => r.Client!.Relationships.Any());
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var t = query.Search.Trim();
            var maconomy = _maconomy.NumbersByName(t);
            rems = rems.Where(r =>
                    r.RemsNumber.Contains(t)
                    || r.Client!.Relationships.Any(x =>
                        (x.RelatedClient!.Person != null
                            && ((x.RelatedClient.Person.LastName + " " + x.RelatedClient.Person.FirstName).Contains(t)
                                || x.RelatedClient.Person.FirstName.Contains(t)
                                || x.RelatedClient.Person.LastName.Contains(t)))
                        || (x.RelatedClient!.LegalName != null && x.RelatedClient.LegalName.Contains(t))))
                .Union(rems.WhereClientNameMatches(t, maconomy));
        }
        if (withEntityType && !string.IsNullOrWhiteSpace(query.EntityType))
        {
            var entityType = query.EntityType.Trim();
            rems = rems.Where(r => r.Client!.EntityType != null && r.Client.EntityType.Value == entityType);
        }
        if (withRelatedStatus && !string.IsNullOrWhiteSpace(query.RelatedStatus))
        {
            rems = WhereRelatedStatus(rems, query.RelatedStatus);
        }
        if (query.ClientPersonIds is { Count: > 0 } clientIds)
        {
            rems = rems.Where(r => clientIds.Contains(r.ClientId));
        }
        if (!string.IsNullOrWhiteSpace(query.RequestStatus))
        {
            rems = rems.WhereStatus(query.RequestStatus);
        }
        if (query.SubmittedFromUtc is { } submittedFrom)
        {
            rems = rems.Where(r => r.FormSubmittedOnUtc >= submittedFrom);
        }
        if (query.SubmittedToUtc is { } submittedTo)
        {
            rems = rems.Where(r => r.FormSubmittedOnUtc <= submittedTo);
        }
        if (query.CreatedFromUtc is { } createdFrom)
        {
            rems = rems.Where(r => r.CreatedOnUtc >= createdFrom);
        }
        if (query.CreatedToUtc is { } createdTo)
        {
            rems = rems.Where(r => r.CreatedOnUtc <= createdTo);
        }
        if (query.UpdatedFromUtc is { } updatedFrom)
        {
            rems = rems.Where(r => r.UpdatedOnUtc >= updatedFrom);
        }
        if (query.UpdatedToUtc is { } updatedTo)
        {
            rems = rems.Where(r => r.UpdatedOnUtc <= updatedTo);
        }
        return rems;
    }

    // A joint filer is the primary client's other half, not a party of their own, so it does not count.
    private static IQueryable<RemsRequest> WhereRelatedStatus(IQueryable<RemsRequest> rems, string code)
    {
        var status = code.Trim();
        var includeUnanswered = status == RemsRelatedEntityStatuses.NotInitiated;
        return rems.Where(r => r.Client!.Relationships.Any(x =>
            (x.RelatedClient!.PersonId == null || x.FilingType == null || x.FilingType.Value != JointFiling)
            && ((x.RelatedStatusId == null && includeUnanswered)
                || (x.RelatedStatus != null && x.RelatedStatus.Value == status))));
    }

    public async Task<RemsRelatedEntityQuickCounts> CountRelatedEntityQuickFiltersAsync(RemsRelatedEntityQuery query, CancellationToken cancellationToken = default)
    {
        var byType = await RelatedRequests(query, withEntityType: false, withRelatedStatus: true)
            .Where(r => r.Client!.EntityType != null)
            .GroupBy(r => r.Client!.EntityType!.Value)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var entityType = byType.ToDictionary(x => x.Code, x => x.Count, StringComparer.Ordinal);
        var forStatus = RelatedRequests(query, withEntityType: true, withRelatedStatus: false);
        var relatedStatus = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var code in new[]
        {
            RemsRelatedEntityStatuses.NotInitiated, RemsRelatedEntityStatuses.RemsInitiated,
            RemsRelatedEntityStatuses.PendingApproval, RemsRelatedEntityStatuses.Approved,
        })
        {
            relatedStatus[code] = await WhereRelatedStatus(forStatus, code).CountAsync(cancellationToken);
        }
        return new RemsRelatedEntityQuickCounts(entityType, relatedStatus);
    }

    public async Task<IReadOnlyList<RemsClientChoice>> ListRelatedEntityClientsAsync(CancellationToken cancellationToken = default)
    {
        var unfiltered = new RemsRelatedEntityQuery(null, null, null, SortRequest.Default, Page: 1, Limit: 1);
        return await ChoicesOf(RelatedRequests(unfiltered, withEntityType: false, withRelatedStatus: false), cancellationToken);
    }

    public async Task<(IReadOnlyList<RemsRelatedEntityItem> Items, int Total)> ListRelatedEntitiesAsync(RemsRelatedEntityQuery query, CancellationToken cancellationToken = default)
    {
        var rows = RelatedRequests(query, withEntityType: true, withRelatedStatus: true);
        var total = await rows.CountAsync(cancellationToken);
        var actors = ActorNames.Of(_dbContext);
        var sorts = SortMap.For(rows, "updatedOnUtc")
            .Add("remsNumber", r => r.RemsNumber)
            .Add("clientName", r => r.Client!.Person != null ? r.Client.Person.ClientDisplayName : r.Client!.LegalName, r => r.RemsNumber)
            .Add("entityType", r => r.Client!.EntityType != null ? r.Client.EntityType.Value : null, r => r.RemsNumber)
            .Add("submittedOnUtc", r => r.FormSubmittedOnUtc, r => r.RemsNumber)
            .Add(
                "relatedCount",
                r => r.Client!.Relationships.Count(x => x.RelatedClient!.PersonId == null || x.FilingType == null || x.FilingType.Value != JointFiling),
                r => r.RemsNumber)
            .Add("requestStatus", r => r.Status!.Value, r => r.UpdatedOnUtc)
            .Add("createdBy", r => actors.Where(a => a.Id == r.CreatedById).Select(a => a.Name).FirstOrDefault(), r => r.RemsNumber)
            .Add("updatedBy", r => actors.Where(a => a.Id == r.UpdatedById).Select(a => a.Name).FirstOrDefault(), r => r.RemsNumber)
            .Add("createdOnUtc", r => r.CreatedOnUtc)
            .Add("updatedOnUtc", r => r.UpdatedOnUtc, r => r.RemsNumber);
        IReadOnlyList<Guid>? order = null;
        IQueryable<RemsRequest> page;
        if (string.Equals(query.Sort.SortBy, "clientName", StringComparison.OrdinalIgnoreCase)
            && await rows.AnyAsync(r => r.Client!.MaconomyCustomerNumber != null, cancellationToken))
        {
            var keys = await rows
                .Select(r => new ClientSortKey<string>(
                    r.Id, r.Client!.Person != null ? r.Client.Person.ClientDisplayName : r.Client!.LegalName,
                    r.Client!.MaconomyCustomerNumber, r.RemsNumber))
                .ToListAsync(cancellationToken);
            var ids = order = _maconomy.PageByName(keys, query.Sort.Descending, query.Page, query.Limit);
            page = rows.Where(r => ids.Contains(r.Id));
        }
        else
        {
            page = sorts.Apply(rows, query.Sort.SortBy, query.Sort.Descending)
                .Skip((query.Page - 1) * query.Limit)
                .Take(query.Limit);
        }
        var read = await page
            .Select(r => new RemsRelatedEntityItem(
                r.Id,
                r.RemsNumber,
                r.Client!.Person != null ? r.Client.Person.ClientDisplayName : r.Client!.LegalName ?? string.Empty,
                r.Client!.Person != null ? r.Client.Person.Suffix : null,
                r.Client!.PrimaryEmail,
                r.Client!.EntityType != null ? r.Client.EntityType.Value : null,
                r.Status!.Value,
                r.AdminAssignedToId,
                r.CSEId,
                r.FormSubmittedOnUtc,
                r.Client!.Relationships.Count(x => x.RelatedClient!.PersonId == null || x.FilingType == null || x.FilingType.Value != JointFiling),
                r.CreatedById,
                r.OnBehalfOfUserId,
                r.CreatedOnUtc,
                r.UpdatedById,
                r.UpdatedOnUtc,
                r.Client!.MaconomyCustomerNumber))
            .ToListAsync(cancellationToken);
        var items = (order is null ? read : order.Join(read, id => id, i => i.RemsId, (_, i) => i))
            .Select(i => i.MaconomyCustomerNumber is null
                ? i
                : i with
                {
                    ClientName = _maconomy.Name(i.MaconomyCustomerNumber, i.ClientName),
                    ClientEmail = _maconomy.Email(i.MaconomyCustomerNumber, i.ClientEmail),
                })
            .ToList();
        return (items, total);
    }

    public async Task<IReadOnlyList<RemsRelatedClientItem>> ListRelatedClientsAsync(IReadOnlyCollection<Guid> remsIds, CancellationToken cancellationToken = default)
    {
        if (remsIds.Count == 0)
        {
            return Array.Empty<RemsRelatedClientItem>();
        }
        var rows = await Requests
            .Where(r => remsIds.Contains(r.Id))
            .SelectMany(r => r.Client!.Relationships, (r, x) => new
            {
                x.Id,
                RemsId = r.Id,
                IsIndividual = x.RelatedClient!.PersonId != null,
                Name = x.RelatedClient!.Person != null
                    ? x.RelatedClient.Person.LastName + " " + x.RelatedClient.Person.FirstName
                    : x.RelatedClient!.LegalName ?? string.Empty,
                Suffix = x.RelatedClient!.Person != null ? x.RelatedClient.Person.Suffix : null,
                Relation = x.RelationType != null ? x.RelationType.Value : null,
                FilingType = x.FilingType != null ? x.FilingType.Value : null,
                Email = x.RelatedClient!.PrimaryEmail ?? (x.RelatedClient.Person != null ? x.RelatedClient.Person.PrimaryEmail : null),
                Phone = x.RelatedClient!.PrimaryPhone ?? (x.RelatedClient.Person != null ? x.RelatedClient.Person.MobileNumber : null),
                Status = x.RelatedStatus != null ? x.RelatedStatus.Value : RemsRelatedEntityStatuses.NotInitiated,
                CreatedRemsId = x.RelatedClient!.Requests
                    .Where(q => q.Id != r.Id)
                    .OrderByDescending(q => q.CreatedOnUtc)
                    .Select(q => (Guid?)q.Id)
                    .FirstOrDefault(),
                x.CreatedOnUtc,
            })
            .ToListAsync(cancellationToken);
        return rows
            .Select(a => new RemsRelatedClientItem(
                a.Id, a.RemsId,
                a.IsIndividual ? RemsRelatedClientKind.Individual : RemsRelatedClientKind.Entity,
                a.Name, a.Suffix, a.Relation, a.IsIndividual ? a.FilingType : null, a.Email, a.Phone, a.Status,
                a.CreatedRemsId, a.CreatedOnUtc, a.Id.ToString("N")))
            .OrderBy(a => a.DeclaredOnUtc)
            .ThenBy(a => a.SourceKey, StringComparer.Ordinal)
            .ToList();
    }

    // ---- Mutations ----

    public async Task AddAsync(RemsRequest request, CancellationToken cancellationToken = default)
        => await _dbContext.RemsRequests.AddAsync(request, cancellationToken);

    public void Update(RemsRequest request) => _dbContext.RemsRequests.Update(request);

    public void Remove(RemsRequest request) => _dbContext.RemsRequests.Remove(request);

    public async Task AddFileAsync(RemsFile file, CancellationToken cancellationToken = default)
        => await _dbContext.RemsFiles.AddAsync(file, cancellationToken);

    public void RemoveFile(RemsFile file) => _dbContext.RemsFiles.Remove(file);

    public async Task AddEmailEventAsync(RemsEmailEvent emailEvent, CancellationToken cancellationToken = default)
        => await _dbContext.RemsEmailEvents.AddAsync(emailEvent, cancellationToken);

    public async Task<IReadOnlyList<RemsEmailEvent>> ListEmailEventsAsync(Guid remsId, CancellationToken cancellationToken = default)
        => await _dbContext.RemsEmailEvents
            .Where(e => e.REMSId == remsId)
            .OrderByDescending(e => e.OccurredOnUtc)
            .ThenByDescending(e => e.CreatedOnUtc)
            .ToListAsync(cancellationToken);

    public Task<RemsEmailEvent?> GetSentEventByProviderMessageIdUnscopedAsync(string providerMessageId, CancellationToken cancellationToken = default)
        => _dbContext.RemsEmailEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.ProviderMessageId == providerMessageId && e.EventType == RemsFormEmailEventType.Sent, cancellationToken);

    public Task<bool> EmailEventExistsAsync(Guid tenantId, string providerMessageId, RemsFormEmailEventType eventType, CancellationToken cancellationToken = default)
        => _dbContext.RemsEmailEvents
            .IgnoreQueryFilters()
            .AnyAsync(e => e.TenantId == tenantId && e.ProviderMessageId == providerMessageId && e.EventType == eventType, cancellationToken);

    public async Task<bool> TryAppendProviderEmailEventAsync(RemsEmailEvent emailEvent, CancellationToken cancellationToken = default)
    {
        await _dbContext.RemsEmailEvents.AddAsync(emailEvent, cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Errors.Cast<SqlError>().Any(e => e.Number is 2601 or 2627))
        {
            _dbContext.Entry(emailEvent).State = EntityState.Detached;
            return false;
        }
    }

    public async Task AddStatusHistoryAsync(RemsStatusHistory history, CancellationToken cancellationToken = default)
        => await _dbContext.RemsStatusHistory.AddAsync(history, cancellationToken);

    public async Task<IReadOnlyList<RemsStatusHistory>> ListStatusHistoryAsync(Guid remsId, CancellationToken cancellationToken = default)
        => await _dbContext.RemsStatusHistory
            .Include(h => h.FromStatus)
            .Include(h => h.ToStatus)
            .Where(h => h.REMSId == remsId)
            .OrderBy(h => h.ChangedOnUtc).ThenBy(h => h.Id)
            .ToListAsync(cancellationToken);

    public async Task<RemsStatusHistory?> GetOpenSendBackAsync(Guid remsId, CancellationToken cancellationToken = default)
    {
        var latest = await _dbContext.RemsStatusHistory
            .Include(h => h.ToStatus)
            .Where(h => h.REMSId == remsId)
            .OrderByDescending(h => h.ChangedOnUtc).ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return latest is not null && latest.ToStatus?.Value == RemsRequestStatuses.ReturnedToInitiator ? latest : null;
    }

    public async Task<IReadOnlyList<RemsStatusHistory>> ListSendBacksAsync(Guid remsId, CancellationToken cancellationToken = default)
        => await _dbContext.RemsStatusHistory
            .Include(h => h.ToStatus)
            .Where(h => h.REMSId == remsId && h.ToStatus != null && h.ToStatus.Value == RemsRequestStatuses.ReturnedToInitiator)
            .OrderBy(h => h.ChangedOnUtc).ThenBy(h => h.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RemsApprover>> ListApproversAsync(Guid remsId, CancellationToken cancellationToken = default)
        => await _dbContext.RemsApprovers.Where(a => a.REMSId == remsId).ToListAsync(cancellationToken);

    public async Task AddApproverAsync(RemsApprover approver, CancellationToken cancellationToken = default)
        => await _dbContext.RemsApprovers.AddAsync(approver, cancellationToken);

    public void RemoveApprover(RemsApprover approver) => _dbContext.RemsApprovers.Remove(approver);

    public async Task AddCommissionSplitAsync(RemsCommissionSplit split, CancellationToken cancellationToken = default)
        => await _dbContext.RemsCommissionSplits.AddAsync(split, cancellationToken);

    public void RemoveCommissionSplit(RemsCommissionSplit split) => _dbContext.RemsCommissionSplits.Remove(split);

    public Task<int> CountActiveCommissionSplitsAsync(Guid remsId, CancellationToken cancellationToken = default)
        => _dbContext.RemsCommissionSplits.CountAsync(s => s.REMSId == remsId, cancellationToken);

    public async Task AddPersonnelRateAsync(RemsPersonnelRate rate, CancellationToken cancellationToken = default)
        => await _dbContext.RemsPersonnelRates.AddAsync(rate, cancellationToken);

    public void RemovePersonnelRate(RemsPersonnelRate rate) => _dbContext.RemsPersonnelRates.Remove(rate);

    public async Task AddMarketingMethodAsync(RemsMarketingMethod method, CancellationToken cancellationToken = default)
        => await _dbContext.RemsMarketingMethods.AddAsync(method, cancellationToken);

    public void RemoveMarketingMethod(RemsMarketingMethod method) => _dbContext.RemsMarketingMethods.Remove(method);

    public async Task AddTaxFormAsync(RemsTaxForm taxForm, CancellationToken cancellationToken = default)
        => await _dbContext.RemsTaxForms.AddAsync(taxForm, cancellationToken);

    public void RemoveTaxForm(RemsTaxForm taxForm) => _dbContext.RemsTaxForms.Remove(taxForm);

    public async Task AddTaxDueDateAsync(RemsTaxDueDate dueDate, CancellationToken cancellationToken = default)
        => await _dbContext.RemsTaxDueDates.AddAsync(dueDate, cancellationToken);

    public void UpdateTaxDueDate(RemsTaxDueDate dueDate) => _dbContext.RemsTaxDueDates.Update(dueDate);

    public void RemoveTaxDueDate(RemsTaxDueDate dueDate) => _dbContext.RemsTaxDueDates.Remove(dueDate);
}
