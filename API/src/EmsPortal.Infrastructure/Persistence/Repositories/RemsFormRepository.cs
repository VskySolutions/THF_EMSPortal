using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class RemsFormRepository : IRemsFormRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public RemsFormRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<REMSForm?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.RemsForms
            // The entity type is an option-set item; its CODE decides what the client's form asks.
            .Include(f => f.EntityType)
            .Include(f => f.Drafts)
            .Include(f => f.Submissions)
            .Include(f => f.EmailEvents)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public Task<REMSForm?> GetByRemsIdAsync(Guid remsId, CancellationToken cancellationToken = default)
        // At most one active form per request (filtered unique index on (TenantId, REMSId)); tenant + soft-delete
        // scoped by the ambient query filter. Email events are loaded so callers can read sent/locked state.
        => _dbContext.RemsForms
            // The entity type is an option-set item; its CODE decides what the client's form asks.
            .Include(f => f.EntityType)
            .Include(f => f.EmailEvents)
            .FirstOrDefaultAsync(f => f.REMSId == remsId, cancellationToken);

    public Task<REMSForm?> GetWithSubmissionsByRemsIdAsync(Guid remsId, CancellationToken cancellationToken = default)
        => _dbContext.RemsForms
            // The entity type is an option-set item; its CODE decides what the client's form asks.
            .Include(f => f.EntityType)
            .Include(f => f.Submissions)
            .FirstOrDefaultAsync(f => f.REMSId == remsId, cancellationToken);

    public async Task<(IReadOnlyList<RemsClientFormItem> Items, int Total)> ListClientFormsAsync(
        RemsClientFormQuery query, CancellationToken cancellationToken = default)
    {
        // Every SUBMITTED request that has a form. Inner-join REMS (tenant + not-deleted) to its form so
        // both ambient query filters apply; project the submitted state and the request's assigned
        // Admin/CSE.
        // Order on the SOURCE columns before projecting — EF cannot translate an OrderBy over the
        // projected record. SubmittedOnUtc DESC puts submitted forms first, not-yet-submitted (null) last.
        //
        // Drafts are excluded. A form record exists from the moment the initiator saves a CSE and an entity
        // type, which is well before the request is anybody's but theirs, and this list is the admins'
        // queue: a draft here would read "Waiting for pickup" over a referral its author is still writing,
        // and would 403 for every admin who tried to open it (drafts are creator-only — see
        // RemsRequestsController.CanSee).
        const string draft = RemsRequestStatuses.Draft;
        var rows =
            from r in _dbContext.Rems
            join f in _dbContext.RemsForms on r.Id equals f.REMSId
            where r.Status!.Value != draft
            select new { Rems = r, Form = f };

        // The list's two quick filters. "All" is every row an admin's queue holds, waiting-for-pickup ones
        // included, so it needs no clause of its own.
        if (query.Assignment == RemsClientFormAssignment.Mine)
        {
            var me = query.CallerUserId;
            rows = rows.Where(x => x.Rems.AdminAssignedToId == me);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var t = query.Search.Trim();
            // Against the client's name as the list SHOWS it — "Smith John Jr." — and against each half
            // of it on its own, because a reader types whichever they have.
            rows = rows.Where(x =>
                x.Rems.REMSNumber.Contains(t)
                || x.Rems.ClientPerson!.ClientDisplayName.Contains(t)
                || x.Rems.ClientPerson!.FirstName.Contains(t)
                || x.Rems.ClientPerson!.LastName.Contains(t));
        }
        if (query.Submitted is { } submitted)
        {
            // "Submitted" is the same expression the projection reports, so the filter and the column agree.
            rows = submitted
                ? rows.Where(x => x.Form.Status == RemsFormStatus.Submitted || x.Form.SubmittedOnUtc != null)
                : rows.Where(x => x.Form.Status != RemsFormStatus.Submitted && x.Form.SubmittedOnUtc == null);
        }
        if (!string.IsNullOrWhiteSpace(query.RequestStatus))
        {
            var s = query.RequestStatus.Trim();
            rows = rows.Where(x => x.Rems.Status!.Value == s);
        }

        // Counted AFTER the filters so the pager reflects the filtered set, not the whole list.
        var total = await rows.CountAsync(cancellationToken);
        // Ordered here, over the joined rows, because EF cannot order a projected record — and over the
        // WHOLE filtered set, because that is what decides which rows page 1 holds. The default is the
        // REQUEST's last touch, matching the audit columns this row carries and the other REMS lists.
        // The Assigned Admin and CSE columns are absent: both are ids this list resolves to names
        // afterwards, so neither is a column to order on.
        var sorts = SortMap.For(rows, "updatedOnUtc")
            .Add("remsNumber", x => x.Rems.REMSNumber)
            .Add("clientName", x => x.Rems.ClientPerson!.ClientDisplayName, x => x.Rems.REMSNumber)
            .Add("submitted", x => x.Form.Status == RemsFormStatus.Submitted || x.Form.SubmittedOnUtc != null, x => x.Rems.UpdatedOnUtc)
            .Add("requestStatus", x => x.Rems.Status!.Value, x => x.Rems.UpdatedOnUtc)
            .Add("submittedOnUtc", x => x.Form.SubmittedOnUtc, x => x.Rems.REMSNumber)
            .Add("createdOnUtc", x => x.Rems.CreatedOnUtc)
            .Add("updatedOnUtc", x => x.Rems.UpdatedOnUtc, x => x.Rems.REMSNumber);

        var items = await sorts.Apply(rows, query.Sort.SortBy, query.Sort.Descending)
            .Skip((query.Page - 1) * query.Limit)
            .Take(query.Limit)
            .Select(x => new RemsClientFormItem(
                x.Rems.Id,
                x.Rems.REMSNumber,
                // The client's name as it reads — "Smith John Jr." for a person, the legal name for an
                // organisation. Composed by the database on Persons.ClientDisplayName, so every list says it
                // the same way and SQL can sort and search on it. The particle follows, for the cell that
                // draws it in bold at the end of the name.
                x.Rems.ClientPerson!.ClientDisplayName,
                x.Rems.ClientPerson!.Suffix,
                x.Rems.Status!.Value,
                x.Form.Status == RemsFormStatus.Submitted || x.Form.SubmittedOnUtc != null,
                x.Form.SubmittedOnUtc,
                x.Rems.AdminAssignedToId,
                x.Rems.CSEId,
                x.Rems.CreatedById,
                x.Rems.CreatedOnUtc,
                x.Rems.UpdatedById,
                x.Rems.UpdatedOnUtc))
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<REMSForm?> GetByInviteCodeAsync(Guid tenantId, string inviteCode, CancellationToken cancellationToken = default)
        => _dbContext.RemsForms
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.TenantId == tenantId && !f.Deleted && f.InviteCode == inviteCode, cancellationToken);

    public Task<REMSForm?> GetByInviteCodeUnscopedAsync(string inviteCode, CancellationToken cancellationToken = default)
        // Public form (no tenant context): resolve by invite code across tenants (IgnoreQueryFilters), and
        // include the owning request — even soft-deleted — plus the drafts so the caller can inspect request
        // state and upsert/submit. Tracked so the submit transaction can update the form + request.
        => _dbContext.RemsForms
            .IgnoreQueryFilters()
            .Include(f => f.EntityType)
            .Include(f => f.Rems).ThenInclude(r => r!.Status)
            .Include(f => f.Rems).ThenInclude(r => r!.Type)
            .Include(f => f.Drafts)
            .FirstOrDefaultAsync(f => !f.Deleted && f.InviteCode == inviteCode, cancellationToken);

    public Task<bool> InviteCodeExistsAsync(Guid tenantId, string inviteCode, CancellationToken cancellationToken = default)
        => _dbContext.RemsForms
            .IgnoreQueryFilters()
            .AnyAsync(f => f.TenantId == tenantId && !f.Deleted && f.InviteCode == inviteCode, cancellationToken);

    public async Task AddAsync(REMSForm form, CancellationToken cancellationToken = default)
        => await _dbContext.RemsForms.AddAsync(form, cancellationToken);

    public void Update(REMSForm form) => _dbContext.RemsForms.Update(form);

    public void Remove(REMSForm form) => _dbContext.RemsForms.Remove(form);

    public async Task AddDraftAsync(REMSFormDraft draft, CancellationToken cancellationToken = default)
        => await _dbContext.RemsFormDrafts.AddAsync(draft, cancellationToken);

    public void UpdateDraft(REMSFormDraft draft) => _dbContext.RemsFormDrafts.Update(draft);

    public async Task AddSubmissionAsync(REMSFormSubmission submission, CancellationToken cancellationToken = default)
        => await _dbContext.RemsFormSubmissions.AddAsync(submission, cancellationToken);

    public async Task AddEmailEventAsync(REMSFormEmailEvent emailEvent, CancellationToken cancellationToken = default)
        => await _dbContext.RemsFormEmailEvents.AddAsync(emailEvent, cancellationToken);

    public async Task<IReadOnlyList<REMSFormEmailEvent>> ListEmailEventsAsync(Guid remsFormId, CancellationToken cancellationToken = default)
        => await _dbContext.RemsFormEmailEvents
            .Where(e => e.REMSFormId == remsFormId)
            .OrderByDescending(e => e.OccurredOnUtc)
            .ThenByDescending(e => e.CreatedOnUtc)
            .ToListAsync(cancellationToken);

    public Task<REMSFormEmailEvent?> GetSentEventByProviderMessageIdUnscopedAsync(
        string providerMessageId, CancellationToken cancellationToken = default)
        // No tenant/user context on a webhook: ignore query filters so the anchor resolves across tenants,
        // and read-only (AsNoTracking) since we only need its TenantId + REMSFormId.
        => _dbContext.RemsFormEmailEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.ProviderMessageId == providerMessageId && e.EventType == RemsFormEmailEventType.Sent,
                cancellationToken);

    public Task<bool> EmailEventExistsAsync(
        Guid tenantId, string providerMessageId, RemsFormEmailEventType eventType, CancellationToken cancellationToken = default)
        => _dbContext.RemsFormEmailEvents
            .IgnoreQueryFilters()
            .AnyAsync(
                e => e.TenantId == tenantId && e.ProviderMessageId == providerMessageId && e.EventType == eventType,
                cancellationToken);

    public async Task<bool> TryAppendProviderEmailEventAsync(
        REMSFormEmailEvent emailEvent, CancellationToken cancellationToken = default)
    {
        await _dbContext.RemsFormEmailEvents.AddAsync(emailEvent, cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // A concurrent post beat us to the filtered unique index (TenantId, ProviderMessageId, EventType).
            // Detach the rejected entity so the shared context stays usable for the remaining events.
            _dbContext.Entry(emailEvent).State = EntityState.Detached;
            return false;
        }
    }

    // SQL Server duplicate-key errors: 2627 (unique constraint) / 2601 (unique index).
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sql
            && sql.Errors.Cast<SqlError>().Any(e => e.Number is 2601 or 2627);

    public async Task<(IReadOnlyList<RemsInboxItem> Items, int Total)> ListInboxAsync(
        RemsInboxQuery query, CancellationToken cancellationToken = default)
    {
        // Every request that has a form is a candidate; the request must resolve through the (soft-delete +
        // tenant) filter on REMS. Tenant isolation on the form itself is ambient.
        var forms = _dbContext.RemsForms.Where(f => f.Rems != null);
        if (query.FormState is { } state)
        {
            forms = forms.Where(f => f.Status == state);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var t = query.Search.Trim();
            forms = forms.Where(f =>
                f.Rems!.REMSNumber.Contains(t)
                || f.Rems!.ClientPerson!.ClientDisplayName.Contains(t)
                || f.Rems!.ClientPerson!.FirstName.Contains(t)
                || f.Rems!.ClientPerson!.LastName.Contains(t));
        }
        if (!string.IsNullOrWhiteSpace(query.RequestStatus))
        {
            var s = query.RequestStatus.Trim();
            forms = forms.Where(f => f.Rems!.Status!.Value == s);
        }

        // Counted AFTER the filters so the pager reflects the filtered set, not the whole inbox.
        var total = await forms.CountAsync(cancellationToken);

        // Project the latest email event via an anonymous-type subquery (FirstOrDefault => null when the
        // form has no events yet), then shape the record in memory.
        // The REQUEST's last touch leads — this row is keyed on it and reports its audit trail — with the
        // form's own modification date as the tie-break for requests moved in the same tick.
        var rows = await forms
            .OrderByDescending(f => f.Rems!.UpdatedOnUtc)
            .ThenByDescending(f => f.UpdatedOnUtc)
            .ThenByDescending(f => f.CreatedOnUtc)
            .Skip((query.Page - 1) * query.Limit)
            .Take(query.Limit)
            .Select(f => new
            {
                f.REMSId,
                f.Rems!.REMSNumber,
                // The client's name as it reads, composed by the database on Persons.ClientDisplayName.
                ClientName = f.Rems!.ClientPerson!.ClientDisplayName,
                EngagementType = f.Rems!.Type!.Value,
                RequestStatus = f.Rems!.Status!.Value,
                f.Status,
                f.UpdatedOnUtc,
                f.CreatedByUserId,
                f.SentOnUtc,
                RemsAssignedToId = f.Rems!.AdminAssignedToId,
                RemsCreatedById = f.Rems!.CreatedById,
                RemsCreatedOnUtc = f.Rems!.CreatedOnUtc,
                RemsUpdatedById = f.Rems!.UpdatedById,
                RemsUpdatedOnUtc = f.Rems!.UpdatedOnUtc,
                LatestEvent = f.EmailEvents
                    .OrderByDescending(e => e.OccurredOnUtc)
                    .Select(e => new { e.EventType, e.OccurredOnUtc })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(x => new RemsInboxItem(
                x.REMSId,
                x.REMSNumber,
                x.ClientName,
                x.EngagementType,
                x.RequestStatus,
                x.Status,
                x.UpdatedOnUtc,
                x.CreatedByUserId,
                x.SentOnUtc,
                x.LatestEvent is null ? null : x.LatestEvent.EventType,
                x.LatestEvent is null ? null : x.LatestEvent.OccurredOnUtc,
                x.RemsAssignedToId,
                x.RemsCreatedById,
                x.RemsCreatedOnUtc,
                x.RemsUpdatedById,
                x.RemsUpdatedOnUtc))
            .ToList();

        return (items, total);
    }
}
