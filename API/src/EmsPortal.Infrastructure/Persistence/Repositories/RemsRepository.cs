using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

internal sealed class RemsRepository : IRemsRepository
{
    private readonly EmsPortalDbContext _dbContext;

    public RemsRepository(EmsPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<REMS?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.Rems
            // Type and Status are option-set items now, so they travel with the request: every caller reads
            // the CODE off the navigation, and the workflow branches on it.
            .Include(r => r.Type)
            .Include(r => r.Status)
            .Include(r => r.Files.Where(f => !f.Deleted))
                .ThenInclude(f => f.Media)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<REMS>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Rems
            .Include(r => r.Type)
            .Include(r => r.Status)
            .OrderByDescending(r => r.UpdatedOnUtc)
            .ThenByDescending(r => r.CreatedOnUtc)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<REMS> Items, int Total)> ListRequestsAsync(
        RemsRequestListOptions options, CancellationToken cancellationToken = default)
    {
        var query = ApplyFieldFilters(ApplyScope(ApplyVisibility(options), options), options);

        var total = await query.CountAsync(cancellationToken);
        // Most-recently-touched first, so a request that anything has moved on — a form sent, an
        // engagement edited, an approval decided — surfaces at the top rather than staying wherever its
        // creation date put it. CreatedOnUtc breaks ties for rows written in the same tick.
        // Type and Status are option-set ITEMS, and the row this list builds reads the code off each of
        // them. Included here rather than on the filtered query above, because that one is also counted
        // and grouped — an Include on a query that ends in an aggregate is work EF does nothing with.
        // Assigned Admin, CSE and EMS State are absent from the map: all three are resolved or derived by
        // the controller after this query, so none of them is a column to order on.
        var withOptions = query
            .Include(r => r.Type)
            .Include(r => r.Status);
        var sorts = SortMap.For(withOptions, "updatedOnUtc")
            .Add("remsNumber", r => r.REMSNumber)
            .Add("type", r => r.Type!.Value, r => r.UpdatedOnUtc)
            .Add("clientName", r => r.ClientPerson!.ClientDisplayName, r => r.REMSNumber)
            .Add("status", r => r.Status!.Value, r => r.UpdatedOnUtc)
            .Add("customerEmail", r => r.ClientPerson!.PrimaryEmail, r => r.REMSNumber)
            .Add("customerMobileNumber", r => r.ClientPerson!.MobileNumber, r => r.REMSNumber)
            .Add("createdOnUtc", r => r.CreatedOnUtc)
            .Add("updatedOnUtc", r => r.UpdatedOnUtc, r => r.CreatedOnUtc);

        var items = await sorts.Apply(withOptions, options.Sort.SortBy, options.Sort.Descending)
            .Skip((options.Page - 1) * options.Limit)
            .Take(options.Limit)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    /// <summary>
    /// How many pool requests fall into each of the Admin Pool's three views, in one round trip. Built on
    /// the SAME visibility predicate and field filters as <see cref="ListRequestsAsync"/> — minus the pool
    /// sub-filter, which is what is being counted — so the badge on a view can never disagree with the
    /// number of rows clicking it produces.
    /// </summary>
    public async Task<RemsPoolCounts> CountPoolScopesAsync(
        RemsRequestListOptions options, CancellationToken cancellationToken = default)
    {
        var me = options.CallerUserId;
        const string draft = RemsRequestStatuses.Draft;

        var query = ApplyFieldFilters(ApplyVisibility(options), options)
            .Where(r => r.Status!.Value != draft);

        // GroupBy over a constant collapses the three counts into a single aggregate query.
        var counts = await query
            .GroupBy(_ => 1)
            .Select(g => new RemsPoolCounts(
                g.Count(r => r.AdminAssignedToId == null),
                g.Count(r => r.AdminAssignedToId == me),
                g.Count()))
            .FirstOrDefaultAsync(cancellationToken);

        // No matching rows => no groups => no row at all, which is a legitimate all-zero result.
        return counts ?? new RemsPoolCounts(0, 0, 0);
    }

    /// <summary>
    /// The record-level visibility predicate (WO-111) — who may see which requests at all. Tenant
    /// isolation is ambient and applied on top of this by the DbContext query filter.
    /// </summary>
    private IQueryable<REMS> ApplyVisibility(RemsRequestListOptions options)
    {
        var me = options.CallerUserId;
        const string draft = RemsRequestStatuses.Draft;

        // A REMS Admin sees the whole tenant, DRAFTS INCLUDED, and may work one (RemsSetupAccess.CanWork):
        // a referral that stalls half-written stalls invisibly, and the person whose job it is to keep the
        // pipeline moving has to be able to see it and finish it. The list's Created By Me / All toggle is
        // what keeps that out of their own way, and it defaults to their own work.
        return options.CallerIsPrivileged
            // Admin / Super Admin: every request in the tenant, whatever stage it is at and whoever raised it.
            ? _dbContext.Rems.AsQueryable()
            // Partner-only: own drafts and drafts naming them; non-draft when created or involved.
            //
            // OnBehalfOfUserId is half of "own" here, and was missing: a delegate raising a request in a
            // shareholder's seat produces the SHAREHOLDER's request (RemsSetupAccess.IsInitiator and the
            // controller's IsMine both say so), but this predicate only ever asked who typed it, so the
            // principal could not see their own work in any list. The controller's CanSee already admitted
            // them, which made the gap the asymmetry its own docs warn against, running the wrong way: a
            // request GetById would open that no list would ever offer. The (TenantId, OnBehalfOfUserId,
            // Status) index exists for exactly this predicate.
            : _dbContext.Rems.Where(r =>
                (r.Status!.Value == draft
                    && (r.CreatedById == me || r.OnBehalfOfUserId == me || r.AdminAssignedToId == me)) ||
                (r.Status!.Value != draft
                    && (r.CreatedById == me || r.OnBehalfOfUserId == me
                        || r.AdminAssignedToId == me || r.CSEId == me)));
    }

    /// <summary>The requested VIEW, layered on top of the security predicate — never a substitute for it.</summary>
    private static IQueryable<REMS> ApplyScope(IQueryable<REMS> query, RemsRequestListOptions options)
    {
        var me = options.CallerUserId;
        const string draft = RemsRequestStatuses.Draft;

        if (options.Scope == RemsListScope.Partner)
        {
            // "All" is the whole of what the caller may see — for a REMS Admin the tenant's requests,
            // other people's drafts included; for everybody else, everything they created or are named on.
            // It narrows nothing on top of the visibility predicate, which is what makes it "all".
            //
            // "Created By Me" is authorship and nothing else: raised BY the caller, or FOR them by a
            // delegate acting in their seat (the same pair the rest of REMS calls IsMine — a delegate's
            // work is the principal's work). It deliberately excludes the requests that merely NAME the
            // caller as CSE or reviewing admin. Those are somebody else's referral that landed on their
            // desk, and a view labelled by who created something cannot be the view that lists them.
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

    /// <summary>The user-supplied search/filter narrowing, shared by the list and its pool counts.</summary>
    private static IQueryable<REMS> ApplyFieldFilters(IQueryable<REMS> query, RemsRequestListOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ClientName))
        {
            var t = options.ClientName.Trim();
            // Against the client's name as the list SHOWS it — "Smith John Jr." — and against each half
            // of it on its own, because a reader types whichever they have. Surname-first is how the row
            // reads, but nobody searching for John Smith types "Smith John".
            query = query.Where(r =>
                r.ClientPerson!.ClientDisplayName.Contains(t)
                || r.ClientPerson!.FirstName.Contains(t)
                || r.ClientPerson!.LastName.Contains(t));
        }
        if (!string.IsNullOrWhiteSpace(options.Contact))
        {
            var t = options.Contact.Trim();
            query = query.Where(r =>
                (r.ClientPerson!.PrimaryEmail != null && r.ClientPerson!.PrimaryEmail.Contains(t)) ||
                (r.ClientPerson!.MobileNumber != null && r.ClientPerson!.MobileNumber.Contains(t)));
        }
        if (!string.IsNullOrWhiteSpace(options.Status))
        {
            var t = options.Status.Trim();
            query = query.Where(r => r.Status!.Value == t);
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

        return query;
    }

    public async Task<IReadOnlyList<RemsFormStateInfo>> GetFormStatesAsync(
        IReadOnlyCollection<Guid> remsIds, CancellationToken cancellationToken = default)
    {
        if (remsIds.Count == 0)
        {
            return Array.Empty<RemsFormStateInfo>();
        }

        // At most one active form per request (filtered unique index on (TenantId, REMSId)).
        return await _dbContext.RemsForms
            .Where(f => remsIds.Contains(f.REMSId))
            .Select(f => new RemsFormStateInfo(
                f.REMSId,
                f.IndustryGroup!.Value,
                f.Status,
                f.SentOnUtc,
                f.SubmittedOnUtc,
                f.Submissions.Any(),
                f.InviteCode))
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(REMS rems, CancellationToken cancellationToken = default)
        => await _dbContext.Rems.AddAsync(rems, cancellationToken);

    public void Update(REMS rems) => _dbContext.Rems.Update(rems);

    public void Remove(REMS rems) => _dbContext.Rems.Remove(rems);

    public async Task AddFileAsync(REMSFiles file, CancellationToken cancellationToken = default)
        => await _dbContext.RemsFiles.AddAsync(file, cancellationToken);

    public void RemoveFile(REMSFiles file) => _dbContext.RemsFiles.Remove(file);

    public async Task AddAdditionalEntityAsync(REMSAdditionalEntity additionalEntity, CancellationToken cancellationToken = default)
        => await _dbContext.RemsAdditionalEntities.AddAsync(additionalEntity, cancellationToken);

    public async Task AddAdditionalIndividualAsync(
        REMSAdditionalIndividual additionalIndividual, CancellationToken cancellationToken = default)
        => await _dbContext.RemsAdditionalIndividuals.AddAsync(additionalIndividual, cancellationToken);

    public async Task<IReadOnlyList<REMSAdditionalIndividual>> ListAdditionalIndividualsAsync(
        Guid remsId, CancellationToken cancellationToken = default)
        => await _dbContext.RemsAdditionalIndividuals
            .Where(a => a.REMSId == remsId)
            .OrderBy(a => a.CreatedOnUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<REMSAdditionalEntity>> ListAdditionalEntitiesAsync(Guid remsId, CancellationToken cancellationToken = default)
        => await _dbContext.RemsAdditionalEntities
            .Where(a => a.REMSId == remsId)
            .OrderBy(a => a.CreatedOnUtc)
            .ToListAsync(cancellationToken);

    public Task<REMSAdditionalEntity?> GetAdditionalEntityAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.RemsAdditionalEntities.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<REMSAdditionalIndividual?> GetAdditionalIndividualAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.RemsAdditionalIndividuals.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public void UpdateAdditionalIndividual(REMSAdditionalIndividual additionalIndividual)
        => _dbContext.RemsAdditionalIndividuals.Update(additionalIndividual);

    // ---- Related Entities (the parent/related-client list) ----

    /// <summary>
    /// A person whose return is filed WITH the client's is not a related client — they are the same
    /// client, and the list says so in the parent's own header ("Sandra Kim + Daniel Kim (Spouse) — same
    /// client, joint filing") rather than giving them a row and a status of their own.
    /// <para>
    /// Which is why this constant is applied to the COUNT and to the status filter but never to the
    /// search: somebody typing a joint spouse's name is looking for the request they appear on, and that
    /// request is on this list.
    /// </para>
    /// </summary>
    private const string JointFiling = "joint";

    public async Task<(IReadOnlyList<RemsRelatedEntityItem> Items, int Total)> ListRelatedEntitiesAsync(
        RemsRelatedEntityQuery query, CancellationToken cancellationToken = default)
    {
        // Held as queryables rather than joined in: a request has many of each, so joining would multiply
        // the parent rows and the count/filter clauses below are all EXISTS/COUNT questions anyway. Both
        // carry the ambient tenant + soft-delete filters.
        var individuals = _dbContext.RemsAdditionalIndividuals.AsQueryable();
        var entities = _dbContext.RemsAdditionalEntities.AsQueryable();

        // Every request that declared somebody alongside its client, joined to its form for the entity
        // type and the submission date. Inner join, like EMS Review's: these rows are written by the
        // public submit, so a request that has any of them necessarily has a form that was submitted.
        var rows =
            from r in _dbContext.Rems
            join f in _dbContext.RemsForms on r.Id equals f.REMSId
            where individuals.Any(a => a.REMSId == r.Id) || entities.Any(a => a.REMSId == r.Id)
            select new { Rems = r, Form = f };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var t = query.Search.Trim();
            // The related clients' own names are searchable too — this list is READ by them ("which
            // request was Falcon Logistics on?"), and the parent's name is often not what the reader has.
            // Against the client's name as the list SHOWS it — "Smith John Jr." — and against each half
            // of it on its own, because a reader types whichever they have.
            rows = rows.Where(x =>
                x.Rems.REMSNumber.Contains(t)
                || x.Rems.ClientPerson!.ClientDisplayName.Contains(t)
                || x.Rems.ClientPerson!.FirstName.Contains(t)
                || x.Rems.ClientPerson!.LastName.Contains(t)
                // Against the related client's name as the list SHOWS it — "Smith Jane" — and against each
                // half on its own, exactly as the parent's name above is matched: a reader types whichever
                // half they have, and neither order should be the only one that finds the row.
                || individuals.Any(a => a.REMSId == x.Rems.Id
                    && ((a.LastName + " " + a.FirstName).Contains(t)
                        || a.FirstName.Contains(t) || a.LastName.Contains(t)))
                || entities.Any(a => a.REMSId == x.Rems.Id && a.FullName.Contains(t)));
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            var entityType = query.EntityType.Trim();
            rows = rows.Where(x => x.Form.IndustryGroup!.Value == entityType);
        }

        if (!string.IsNullOrWhiteSpace(query.RelatedStatus))
        {
            var status = query.RelatedStatus.Trim();
            // A row nobody has answered for holds no status, and that IS "not initiated" — so the filter
            // for that value has to reach the nulls as well, or it would return none of the rows the list
            // actually shows it on.
            var includeUnanswered = status == RemsRelatedEntityStatuses.NotInitiated;
            rows = rows.Where(x =>
                individuals.Any(a => a.REMSId == x.Rems.Id && a.FilingType != JointFiling
                    && ((a.RelatedStatusId == null && includeUnanswered)
                        || (a.RelatedStatus != null && a.RelatedStatus.Value == status)))
                || entities.Any(a => a.REMSId == x.Rems.Id
                    && ((a.RelatedStatusId == null && includeUnanswered)
                        || (a.RelatedStatus != null && a.RelatedStatus.Value == status))));
        }

        // Counted AFTER the filters so the pager reflects the filtered set, not the whole list.
        var total = await rows.CountAsync(cancellationToken);

        // Ordered over the joined rows and over the WHOLE filtered set — that is what decides which rows
        // page 1 holds. The default is the request's last touch, like every other REMS list. The nested
        // Parent & Related Clients column is not orderable (it is a table, not a value); Related Clients
        // is the count of it, which is.
        var sorts = SortMap.For(rows, "updatedOnUtc")
            .Add("remsNumber", x => x.Rems.REMSNumber)
            .Add("clientName", x => x.Rems.ClientPerson!.ClientDisplayName, x => x.Rems.REMSNumber)
            .Add("entityType", x => x.Form.IndustryGroup!.Value, x => x.Rems.REMSNumber)
            .Add("submittedOnUtc", x => x.Form.SubmittedOnUtc, x => x.Rems.REMSNumber)
            .Add(
                "relatedCount",
                x => individuals.Count(a => a.REMSId == x.Rems.Id && a.FilingType != JointFiling)
                    + entities.Count(a => a.REMSId == x.Rems.Id),
                x => x.Rems.REMSNumber)
            .Add("createdOnUtc", x => x.Rems.CreatedOnUtc)
            .Add("updatedOnUtc", x => x.Rems.UpdatedOnUtc, x => x.Rems.REMSNumber);

        var items = await sorts.Apply(rows, query.Sort.SortBy, query.Sort.Descending)
            .Skip((query.Page - 1) * query.Limit)
            .Take(query.Limit)
            .Select(x => new RemsRelatedEntityItem(
                x.Rems.Id,
                x.Rems.REMSNumber,
                // The client's name as it reads — "Smith John Jr." for a person, the legal name for an
                // organisation. Composed by the database on Persons.ClientDisplayName, so every list says
                // it the same way and SQL can sort and search on it.
                x.Rems.ClientPerson!.ClientDisplayName,
                x.Rems.ClientPerson!.Suffix,
                x.Rems.ClientPerson!.PrimaryEmail,
                x.Form.IndustryGroup!.Value,
                x.Rems.Status!.Value,
                x.Rems.AdminAssignedToId,
                x.Form.SubmittedOnUtc,
                // The rows the nested table actually LISTS, which is why the joint filers are left out of
                // it: they are shown as part of the parent, not under it.
                individuals.Count(a => a.REMSId == x.Rems.Id && a.FilingType != JointFiling)
                    + entities.Count(a => a.REMSId == x.Rems.Id),
                x.Rems.CreatedById,
                x.Rems.OnBehalfOfUserId,
                x.Rems.CreatedOnUtc,
                x.Rems.UpdatedById,
                x.Rems.UpdatedOnUtc))
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<RemsRelatedClientItem>> ListRelatedClientsAsync(
        IReadOnlyCollection<Guid> remsIds, CancellationToken cancellationToken = default)
    {
        if (remsIds.Count == 0)
        {
            return Array.Empty<RemsRelatedClientItem>();
        }

        // Two reads rather than one UNION: the two tables share almost nothing but the parent they hang
        // off, and a union would mean projecting each into the other's missing columns in SQL to satisfy
        // a shape neither has. Two queries over one page of parents is the cheaper honesty.
        var individuals = await _dbContext.RemsAdditionalIndividuals
            .Where(a => remsIds.Contains(a.REMSId))
            .Select(a => new RemsRelatedClientItem(
                a.Id,
                a.REMSId,
                RemsRelatedClientKind.Individual,
                // SURNAME FIRST, as the Client column beside it reads and as Persons.ClientDisplayName
                // composes for the client themselves. A related client is a client, and a list that named
                // the parent "Smith John" and the child "Jane Smith" was naming one family two ways.
                //
                // Composed here rather than read off the minted Person: this row is the record of what was
                // DECLARED, and a Person edited afterwards must not rewrite the client's own answer — the
                // same reason the name is duplicated onto these columns at all.
                a.LastName + " " + a.FirstName,
                a.Suffix,
                a.RelationType,
                a.FilingType,
                a.Email,
                a.PhoneNumber,
                // Resolved here rather than left null for the caller to interpret: "nobody has answered"
                // and "somebody answered Not Initiated" are the same fact, and only one of them should
                // reach a screen.
                a.RelatedStatus == null ? RemsRelatedEntityStatuses.NotInitiated : a.RelatedStatus.Value,
                // A person on somebody's return never gets a request of their own — that is the whole
                // distinction between this table and the one below.
                null,
                a.CreatedOnUtc,
                a.SourceKey))
            .ToListAsync(cancellationToken);

        var entities = await _dbContext.RemsAdditionalEntities
            .Where(a => remsIds.Contains(a.REMSId))
            .Select(a => new RemsRelatedClientItem(
                a.Id,
                a.REMSId,
                RemsRelatedClientKind.Entity,
                a.FullName,
                // A company's name carries no generational particle, and the intake form does not ask how
                // another business relates to the client or how its return is filed — all three questions
                // are about people. Null rather than invented.
                null,
                null,
                null,
                a.EmailAddress,
                a.PhoneNumber,
                a.RelatedStatus == null ? RemsRelatedEntityStatuses.NotInitiated : a.RelatedStatus.Value,
                a.CreatedREMSId,
                a.CreatedOnUtc,
                a.SourceKey))
            .ToListAsync(cancellationToken);

        // Merged into ONE sequence per parent, in the order the client declared them: the two kinds can
        // both appear under one request (an individual's submission from before "Other Entities" stopped
        // being asked of them carries entities too), and the reference each row is numbered by counts
        // across the whole nested table rather than per table.
        return individuals
            .Concat(entities)
            .OrderBy(a => a.DeclaredOnUtc)
            .ThenBy(a => a.SourceKey, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetNumbersAsync(
        IReadOnlyCollection<Guid> remsIds, CancellationToken cancellationToken = default)
        => remsIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Rems
                .Where(r => remsIds.Contains(r.Id))
                .Select(r => new { r.Id, r.REMSNumber })
                .ToDictionaryAsync(r => r.Id, r => r.REMSNumber, cancellationToken);

    public void UpdateAdditionalEntity(REMSAdditionalEntity additionalEntity)
        => _dbContext.RemsAdditionalEntities.Update(additionalEntity);

    public async Task AddSendBackAsync(REMSSendBack sendBack, CancellationToken cancellationToken = default)
        => await _dbContext.RemsSendBacks.AddAsync(sendBack, cancellationToken);

    public Task<REMSSendBack?> GetOpenSendBackAsync(Guid remsId, CancellationToken cancellationToken = default)
        => _dbContext.RemsSendBacks
            .FirstOrDefaultAsync(s => s.REMSId == remsId && s.ResolvedOnUtc == null, cancellationToken);

    public async Task<IReadOnlyList<REMSSendBack>> ListSendBacksAsync(Guid remsId, CancellationToken cancellationToken = default)
        => await _dbContext.RemsSendBacks
            .Where(s => s.REMSId == remsId)
            .OrderBy(s => s.CreatedOnUtc)
            .ToListAsync(cancellationToken);

    public void UpdateSendBack(REMSSendBack sendBack) => _dbContext.RemsSendBacks.Update(sendBack);

    public Task<int> CountActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => _dbContext.Rems
            .IgnoreQueryFilters()
            .CountAsync(r => r.TenantId == tenantId && !r.Deleted, cancellationToken);

    public Task<bool> NumberExistsAsync(Guid tenantId, string number, CancellationToken cancellationToken = default)
        => _dbContext.Rems
            .IgnoreQueryFilters()
            .AnyAsync(r => r.TenantId == tenantId && !r.Deleted && r.REMSNumber == number, cancellationToken);

    public Task<bool> IsClientPersonSharedAsync(Guid personId, Guid excludingRemsId, CancellationToken cancellationToken = default)
        => _dbContext.Rems
            .AnyAsync(
                r => r.Id != excludingRemsId
                    && (r.ClientPersonId == personId || r.ExistingClientReferenceId == personId),
                cancellationToken);
}
