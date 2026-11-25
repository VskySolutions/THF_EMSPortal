using System.Linq.Expressions;
using EmsPortal.Application.Abstractions.Persistence;
using EmsPortal.Application.Common;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Domain.Entities;
using EmsPortal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence.Repositories;

/// <summary>
/// Generic Deleted Records Management over the soft-deletable entities. Restores and hard-deletes also
/// cascade the Universal Feature rows that share the record's <c>(EntityType, EntityId)</c> key.
/// <para>
/// Every supported type is declared once in <see cref="Handlers"/> rather than repeated across four
/// switch statements. The four operations are the same query in every case — filter by id, honour the
/// tenant scope, act — and differ only in the DbSet, the column that names a record to a human, and how
/// that type is tenant-scoped (a required <c>TenantId</c>, a nullable one for rows that can be
/// platform-wide, or none at all for a global type like User). Those three things are what a registration
/// supplies, checked by the compiler against the concrete entity.
/// </para>
/// </summary>
internal sealed class DeletedRecordsRepository : IDeletedRecordsRepository
{
    /// <summary>
    /// The four operations for one entity type, closed over its concrete CLR type.
    /// <see cref="PurgeChildren"/> is set only for a type that roots an aggregate, and runs before the
    /// record itself is removed.
    /// </summary>
    private sealed record Handler(
        Func<Guid?, IQueryable<DeletedRecordRow>> Deleted,
        Func<Guid, Guid?, IQueryable<DeletedRecordRow>> ById,
        Func<Guid, Guid?, CancellationToken, Task<int>> Restore,
        Func<Guid, Guid?, CancellationToken, Task<int>> Purge,
        Func<DateTime, Guid?, CancellationToken, Task<int>> CountOverdue)
    {
        public Func<Guid, CancellationToken, Task>? PurgeChildren { get; init; }
    }

    private readonly EmsPortalDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IReadOnlyDictionary<EntityType, Handler> _handlers;

    public DeletedRecordsRepository(EmsPortalDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _handlers = BuildHandlers();
    }

    public bool IsSupported(EntityType entityType) => _handlers.ContainsKey(entityType);

    private Guid? Effective(Guid? tenantId) => tenantId ?? (_tenantContext.IsResolved ? _tenantContext.TenantId : (Guid?)null);

    private IReadOnlyDictionary<EntityType, Handler> BuildHandlers()
    {
        var map = new Dictionary<EntityType, Handler>();

        // Tenant-scoped by a required TenantId.
        Add(map, EntityType.UserGroup, _dbContext.UserGroups, g => g.Name, g => g.TenantId);
        Add(map, EntityType.PermissionGroup, _dbContext.PermissionGroups, g => g.Name, g => g.TenantId);
        Add(map, EntityType.Tag, _dbContext.Tags, t => t.Name, t => t.TenantId);
        Add(map, EntityType.SmtpAccount, _dbContext.Set<SmtpAccount>(), a => a.AccountName, a => a.TenantId);

        // A sticky note has no name of its own; the title is optional, so fall back to the body.
        Add(map, EntityType.StickyNote, _dbContext.StickyNotes, n => n.Title ?? n.Body, n => n.TenantId);

        // Nullable TenantId: these rows may be platform-wide, in which case no tenant owns them and only
        // an unscoped (Super Admin) caller sees them.
        AddNullableTenant(
            map, EntityType.Person, _dbContext.Persons,
            p => p.DisplayName,
            p => p.TenantId);
        AddNullableTenant(map, EntityType.OptionSet, _dbContext.OptionSets, s => s.Name, s => s.TenantId);
        // Identified by subject rather than TemplateKey: the key is an enum, and .ToString() on one inside
        // a projection is not reliably translatable — the subject is a plain string column and reads
        // better to a human anyway.
        AddNullableTenant(map, EntityType.EmailTemplate, _dbContext.EmailTemplates, t => t.Subject, t => t.TenantId);
        // A role is either the platform's or one tenant's own, so a tenant admin only ever sees, restores
        // and purges the roles their own tenant created — another tenant's are not theirs to touch, and a
        // platform role is the Super Admin's to put back.
        AddNullableTenant(map, EntityType.Role, _dbContext.Roles, r => r.Name, r => r.TenantId);

        // A user has no tenant column: they are in a tenant through a role assignment there, and the
        // assignments stay on a deleted user.
        Register<User>(map, EntityType.User, _dbContext.Users, u => u.DisplayName, _ => null, scoped: false,
            membership: (users, tenant) => users.Where(u =>
                _dbContext.UserTenantRoles.IgnoreQueryFilters().Any(r => r.UserId == u.Id && r.TenantId == tenant)));
        map[EntityType.User] = map[EntityType.User] with { PurgeChildren = PurgeUserChildrenAsync };

        // Global, with no tenant column at all: visible to whoever may manage them.
        AddGlobal(map, EntityType.Tenant, _dbContext.Tenants, t => t.Name);

        // A REMS request roots an aggregate rather than standing alone, so purging one has to take its
        // whole graph with it.
        Add(map, EntityType.Rems, _dbContext.RemsRequests, r => r.RemsNumber, r => r.TenantId);
        map[EntityType.Rems] = map[EntityType.Rems] with { PurgeChildren = PurgeRemsAggregateAsync };

        return map;
    }

    // ---- Registration ----

    private void Add<T>(
        IDictionary<EntityType, Handler> map, EntityType type, IQueryable<T> set,
        Expression<Func<T, string>> identity, Expression<Func<T, Guid>> tenantOf) where T : AuditableEntity
        => Register(map, type, set, identity, Nullable(tenantOf), scoped: true);

    private void AddNullableTenant<T>(
        IDictionary<EntityType, Handler> map, EntityType type, IQueryable<T> set,
        Expression<Func<T, string>> identity, Expression<Func<T, Guid?>> tenantOf) where T : AuditableEntity
        => Register(map, type, set, identity, tenantOf, scoped: true);

    private void AddGlobal<T>(
        IDictionary<EntityType, Handler> map, EntityType type, IQueryable<T> set,
        Expression<Func<T, string>> identity) where T : AuditableEntity
        => Register<T>(map, type, set, identity, _ => null, scoped: false);

    /// <summary>Lifts a required TenantId selector to the nullable shape the generic code works in.</summary>
    private static Expression<Func<T, Guid?>> Nullable<T>(Expression<Func<T, Guid>> selector)
        => Expression.Lambda<Func<T, Guid?>>(
            Expression.Convert(selector.Body, typeof(Guid?)), selector.Parameters);

    private void Register<T>(
        IDictionary<EntityType, Handler> map,
        EntityType type,
        IQueryable<T> set,
        Expression<Func<T, string>> identity,
        Expression<Func<T, Guid?>> tenantOf,
        bool scoped,
        Func<IQueryable<T>, Guid, IQueryable<T>>? membership = null) where T : AuditableEntity
    {
        // The soft-delete query filter is bypassed throughout — deleted rows are precisely what this
        // repository exists to find — so the tenant predicate is applied by hand instead.
        IQueryable<T> InTenant(Guid? tenant)
        {
            var query = set.IgnoreQueryFilters();
            if (membership is not null && tenant is { } own)
            {
                return membership(query, own);
            }
            if (!scoped || tenant is not { } t)
            {
                return query;
            }

            var parameter = tenantOf.Parameters[0];
            var predicate = Expression.Lambda<Func<T, bool>>(
                Expression.Equal(tenantOf.Body, Expression.Constant(t, typeof(Guid?))), parameter);
            return query.Where(predicate);
        }

        // Every registered entity keys on "Id"; EF.Property keeps that uniform without an interface the
        // domain types would otherwise all have to implement.
        IQueryable<T> One(Guid id, Guid? tenant) => InTenant(tenant).Where(e => EF.Property<Guid>(e, "Id") == id);

        IQueryable<DeletedRecordRow> Project(IQueryable<T> query)
        {
            var toRow = BuildRowProjection(identity, tenantOf);
            return query.Select(toRow);
        }

        map[type] = new Handler(
            Deleted: tenant => Project(InTenant(tenant).Where(e => e.Deleted).OrderByDescending(e => e.DeletedOnUtc)),
            ById: (id, tenant) => Project(One(id, tenant).Where(e => e.Deleted)),
            Restore: (id, tenant, ct) => One(id, tenant).Where(e => e.Deleted).ExecuteUpdateAsync(
                s => s.SetProperty(e => e.Deleted, false).SetProperty(e => e.DeletedOnUtc, (DateTime?)null), ct),
            Purge: (id, tenant, ct) => One(id, tenant).ExecuteDeleteAsync(ct),
            CountOverdue: (cutoff, tenant, ct) =>
                InTenant(tenant).Where(e => e.Deleted && e.DeletedOnUtc <= cutoff).CountAsync(ct));
    }

    /// <summary>Builds the row projection from the type's identity/tenant selectors, sharing one parameter.</summary>
    private static Expression<Func<T, DeletedRecordRow>> BuildRowProjection<T>(
        Expression<Func<T, string>> identity, Expression<Func<T, Guid?>> tenantOf) where T : AuditableEntity
    {
        var parameter = identity.Parameters[0];
        var tenantBody = new ParameterSwap(tenantOf.Parameters[0], parameter).Visit(tenantOf.Body)!;

        // The tenant stays nullable in SQL: a Guid.Empty literal comes back from SQL Server as text and
        // cannot be read as a Guid, which broke the list for every type without a tenant column.
        var tenantValue = tenantBody;

        // The members are named so the list can be ordered by a column of the row: without them EF cannot
        // tell which constructor argument a property reads, and every sort fails to translate.
        var ctor = typeof(DeletedRecordRow).GetConstructors()[0];
        var members = ctor.GetParameters().Select(p => (System.Reflection.MemberInfo)typeof(DeletedRecordRow).GetProperty(p.Name!)!);
        return Expression.Lambda<Func<T, DeletedRecordRow>>(
            Expression.New(
                ctor,
                new[]
                {
                    Expression.Call(typeof(EF), nameof(EF.Property), new[] { typeof(Guid) }, parameter, Expression.Constant("Id")),
                    identity.Body,
                    tenantValue,
                    Expression.Property(parameter, nameof(AuditableEntity.UpdatedById)),
                    Expression.Property(parameter, nameof(AuditableEntity.DeletedOnUtc)),
                },
                members),
            parameter);
    }

    /// <summary>Rebinds a second lambda's parameter onto the projection's, so both bodies compose.</summary>
    private sealed class ParameterSwap : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ParameterSwap(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node) => node == _from ? _to : base.VisitParameter(node);
    }

    // ---- Operations ----

    // What the deleted-records panel may be ordered by. Deleted By is an id the controller resolves to a
    // name afterwards; it orders by the ActorNames subquery, which is why the map is built per call.
    private SortMap<DeletedRecordRow> BuildDeletedSorts()
    {
        var actors = ActorNames.Of(_dbContext);
        return new SortMap<DeletedRecordRow>("deletedOnUtc")
            .Add("identity", r => r.Identity)
            .Add("deletedByName", r => actors.Where(a => a.Id == r.DeletedById).Select(a => a.Name).FirstOrDefault(), r => r.Identity)
            .Add("deletedOnUtc", r => r.DeletedOnUtc, r => r.Identity);
    }

    public async Task<(IReadOnlyList<DeletedRecordRow> Items, int Total)> ListDeletedAsync(
        EntityType entityType, Guid? tenantId, SortRequest sort, int page, int limit,
        CancellationToken cancellationToken = default)
    {
        if (!_handlers.TryGetValue(entityType, out var handler))
        {
            return (Array.Empty<DeletedRecordRow>(), 0);
        }

        var query = handler.Deleted(Effective(tenantId));
        var total = await query.CountAsync(cancellationToken);
        var items = await BuildDeletedSorts().Apply(query, sort.SortBy, sort.Descending)
            .Skip((page - 1) * limit).Take(limit).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<string?> GetDeletedIdentityAsync(
        EntityType entityType, Guid entityId, Guid? tenantId, CancellationToken cancellationToken = default)
    {
        if (!_handlers.TryGetValue(entityType, out var handler))
        {
            return null;
        }

        var row = await handler.ById(entityId, Effective(tenantId)).FirstOrDefaultAsync(cancellationToken);
        return row?.Identity;
    }

    public async Task<bool> RestoreAsync(
        EntityType entityType, Guid entityId, Guid? tenantId, CancellationToken cancellationToken = default)
    {
        if (!_handlers.TryGetValue(entityType, out var handler))
        {
            return false;
        }

        if (await handler.Restore(entityId, Effective(tenantId), cancellationToken) == 0)
        {
            return false;
        }

        // A user's person record was deleted with the account, so it comes back with it.
        if (entityType == EntityType.User)
        {
            await _dbContext.Persons.IgnoreQueryFilters()
                .Where(p => p.Deleted && _dbContext.Users.IgnoreQueryFilters().Any(u => u.Id == entityId && u.PersonId == p.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Deleted, false).SetProperty(p => p.DeletedOnUtc, (DateTime?)null), cancellationToken);
        }

        // A client deleted with their only request, and the person behind them, come back with it.
        if (entityType == EntityType.Rems)
        {
            var clients = _dbContext.Clients.IgnoreQueryFilters()
                .Where(c => c.Deleted && _dbContext.RemsRequests.IgnoreQueryFilters().Any(r => r.Id == entityId && r.ClientId == c.Id));
            await _dbContext.Persons.IgnoreQueryFilters()
                .Where(p => p.Deleted && clients.Any(c => c.PersonId == p.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Deleted, false).SetProperty(p => p.DeletedOnUtc, (DateTime?)null), cancellationToken);
            await clients
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Deleted, false).SetProperty(c => c.DeletedOnUtc, (DateTime?)null), cancellationToken);
        }

        await RestoreUniversalFeaturesAsync(entityType, entityId, cancellationToken);
        return true;
    }

    public async Task<string?> GetRestoreBlockerAsync(
        EntityType entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        if (entityType == EntityType.Rems)
        {
            // The email was free for another client while this one was deleted, and an email belongs to one client.
            var clientEmail = await _dbContext.RemsRequests.IgnoreQueryFilters()
                .Where(r => r.Id == entityId && r.Deleted && r.Client!.Deleted)
                .Select(r => r.Client!.PrimaryEmail).FirstOrDefaultAsync(cancellationToken);
            return !string.IsNullOrWhiteSpace(clientEmail) && await _dbContext.Clients.AnyAsync(c => c.PrimaryEmail == clientEmail && c.MaconomyCustomerNumber == null, cancellationToken)
                ? $"Another client now has the email {clientEmail}."
                : null;
        }

        if (entityType != EntityType.User)
        {
            return null;
        }

        // The email is free for reuse while its user is deleted, and two live users cannot share one.
        var email = await _dbContext.Users.IgnoreQueryFilters()
            .Where(u => u.Id == entityId && u.Deleted).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);
        return email is not null && await _dbContext.Users.AnyAsync(u => u.Email == email, cancellationToken)
            ? $"Another user now has the email {email}."
            : null;
    }

    /// <summary>
    /// Clears what would otherwise hold a user's row in place, and takes the person deleted with it. What
    /// is left to stop the purge is real history (requests, approvals, delegations).
    /// </summary>
    private async Task PurgeUserChildrenAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _dbContext.UserImportRows.IgnoreQueryFilters().Where(r => r.CreatedUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedUserId, (Guid?)null), cancellationToken);
        await _dbContext.RefreshTokens.IgnoreQueryFilters().Where(t => t.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        var personId = await _dbContext.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId).Select(u => u.PersonId).FirstOrDefaultAsync(cancellationToken);
        if (personId is { } id
            && await _dbContext.Persons.IgnoreQueryFilters().AnyAsync(p => p.Id == id && p.Deleted, cancellationToken))
        {
            await _dbContext.Users.IgnoreQueryFilters().Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PersonId, (Guid?)null), cancellationToken);
            await CascadeDeleteUniversalFeaturesAsync(EntityType.Person, id, cancellationToken);
            await _dbContext.Persons.IgnoreQueryFilters().Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken);
        }
    }

    public async Task<bool> HardDeleteAsync(
        EntityType entityType, Guid entityId, Guid? tenantId, CancellationToken cancellationToken = default)
    {
        if (!_handlers.TryGetValue(entityType, out var handler))
        {
            return false;
        }

        // Nothing is touched for a record that is not deleted, or not this tenant's.
        if (!await handler.ById(entityId, Effective(tenantId)).AnyAsync(cancellationToken))
        {
            return false;
        }

        // UF rows first: they reference the record by (EntityType, EntityId) rather than by FK, so nothing
        // in the database would clean them up, and removing the record first would strand them.
        await CascadeDeleteUniversalFeaturesAsync(entityType, entityId, cancellationToken);

        // Then the record's own children, for a type that roots an aggregate. Every REMS FK is Restrict,
        // so the root cannot go until its graph has.
        if (handler.PurgeChildren is { } purgeChildren)
        {
            await purgeChildren(entityId, cancellationToken);
        }

        return await handler.Purge(entityId, Effective(tenantId), cancellationToken) > 0;
    }

    public async Task<IReadOnlyDictionary<EntityType, int>> CountOverdueAsync(
        int retentionDays, Guid? tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = Effective(tenantId);
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var result = new Dictionary<EntityType, int>();

        foreach (var (type, handler) in _handlers)
        {
            result[type] = await handler.CountOverdue(cutoff, tenant, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Permanently removes every REMS_* row hanging off a request, children before parents: every FK in
    /// this graph is <c>Restrict</c>, so the order is load-bearing. The client and its Client_* rows stay
    /// — a client outlives any one request and may be named by another — as do the Media rows behind the
    /// request's files, which have a lifecycle of their own.
    /// </summary>
    private async Task PurgeRemsAggregateAsync(Guid remsId, CancellationToken cancellationToken)
    {
        var taskIds = _dbContext.RemsApprovalTasks.IgnoreQueryFilters()
            .Where(t => t.REMSId == remsId).Select(t => t.Id);

        // Approval chain.
        await _dbContext.RemsApprovalChecklistItems.IgnoreQueryFilters()
            .Where(i => taskIds.Contains(i.TaskId)).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.RemsApprovalTasks.IgnoreQueryFilters()
            .Where(t => t.REMSId == remsId).ExecuteDeleteAsync(cancellationToken);

        // Engagement setup.
        await _dbContext.RemsApprovers.IgnoreQueryFilters()
            .Where(a => a.REMSId == remsId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.RemsCommissionSplits.IgnoreQueryFilters()
            .Where(s => s.REMSId == remsId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.RemsPersonnelRates.IgnoreQueryFilters()
            .Where(r => r.REMSId == remsId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.RemsMarketingMethods.IgnoreQueryFilters()
            .Where(m => m.REMSId == remsId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.RemsTaxDueDates.IgnoreQueryFilters()
            .Where(d => d.REMSId == remsId).ExecuteDeleteAsync(cancellationToken);

        // Files, the email log and the status trail.
        await _dbContext.RemsFiles.IgnoreQueryFilters()
            .Where(f => f.REMSId == remsId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.RemsEmailEvents.IgnoreQueryFilters()
            .Where(e => e.REMSId == remsId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.RemsStatusHistory.IgnoreQueryFilters()
            .Where(h => h.REMSId == remsId).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Un-deletes the soft-deleted UF rows that share the record's (EntityType, EntityId) key.</summary>
    private async Task RestoreUniversalFeaturesAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken)
    {
        await _dbContext.ConversationMessages.IgnoreQueryFilters().Where(m => m.EntityType == entityType && m.EntityId == entityId && m.Deleted)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Deleted, false).SetProperty(m => m.DeletedOnUtc, (DateTime?)null), cancellationToken);
        await _dbContext.EntityTags.IgnoreQueryFilters().Where(e => e.EntityType == entityType && e.EntityId == entityId && e.Deleted)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Deleted, false).SetProperty(e => e.DeletedOnUtc, (DateTime?)null), cancellationToken);
        await _dbContext.Attachments.IgnoreQueryFilters().Where(a => a.EntityType == entityType && a.EntityId == entityId && a.Deleted)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Deleted, false).SetProperty(a => a.DeletedOnUtc, (DateTime?)null), cancellationToken);
        await _dbContext.Pins.IgnoreQueryFilters().Where(p => p.EntityType == entityType && p.EntityId == entityId && p.Deleted)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Deleted, false).SetProperty(p => p.DeletedOnUtc, (DateTime?)null), cancellationToken);
        await _dbContext.ColourCodes.IgnoreQueryFilters().Where(c => c.EntityType == entityType && c.EntityId == entityId && c.Deleted)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Deleted, false).SetProperty(c => c.DeletedOnUtc, (DateTime?)null), cancellationToken);
        await _dbContext.Checklists.IgnoreQueryFilters().Where(c => c.EntityType == entityType && c.EntityId == entityId && c.Deleted)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Deleted, false).SetProperty(c => c.DeletedOnUtc, (DateTime?)null), cancellationToken);
    }

    /// <summary>Permanently removes all UF rows for the record's (EntityType, EntityId) key. Child rows cascade via DB FKs.</summary>
    private async Task CascadeDeleteUniversalFeaturesAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken)
    {
        await _dbContext.ConversationMessages.IgnoreQueryFilters().Where(m => m.EntityType == entityType && m.EntityId == entityId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.EntityTags.IgnoreQueryFilters().Where(e => e.EntityType == entityType && e.EntityId == entityId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Attachments.IgnoreQueryFilters().Where(a => a.EntityType == entityType && a.EntityId == entityId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.ActivityEvents.IgnoreQueryFilters().Where(a => a.EntityType == entityType && a.EntityId == entityId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Reminders.IgnoreQueryFilters().Where(r => r.EntityType == entityType && r.EntityId == entityId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Checklists.IgnoreQueryFilters().Where(c => c.EntityType == entityType && c.EntityId == entityId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Pins.IgnoreQueryFilters().Where(p => p.EntityType == entityType && p.EntityId == entityId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.ColourCodes.IgnoreQueryFilters().Where(c => c.EntityType == entityType && c.EntityId == entityId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.FieldModifiedLogs.IgnoreQueryFilters().Where(l => l.EntityType == entityType && l.EntityId == entityId).ExecuteDeleteAsync(cancellationToken);
    }
}
