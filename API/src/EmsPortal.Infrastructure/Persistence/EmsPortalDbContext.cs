using EmsPortal.Application.Abstractions.Security;
using EmsPortal.Application.Abstractions.Tenancy;
using EmsPortal.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EmsPortal.Infrastructure.Persistence;

/// <summary>
/// EF Core unit of work for the EmsPortal application schema. Owns the
/// application tables (access management, email, option sets, universal
/// features, audit trail). Schema migrations are applied by the Integration API on
/// startup; the Background Worker and MCP Server are read/write consumers only.
/// <para>
/// Tenant isolation is enforced here: tenant-scoped entities carry a global query
/// filter on the resolved <see cref="ITenantContext.TenantId"/>, and inserts are
/// stamped with the active tenant. When no tenant is resolved (background/global
/// operations, or design-time), the filter is a no-op.
/// </para>
/// </summary>
public class EmsPortalDbContext : DbContext, IDataProtectionKeyContext
{
    private readonly ITenantContext _tenantContext;
    private readonly IActorAccessor _actorAccessor;

    public EmsPortalDbContext(
        DbContextOptions<EmsPortalDbContext> options,
        ITenantContext tenantContext,
        IActorAccessor actorAccessor)
        : base(options)
    {
        _tenantContext = tenantContext;
        _actorAccessor = actorAccessor;
    }

    public DbSet<AuditTrailEntry> AuditTrail => Set<AuditTrailEntry>();

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<UserTenantRole> UserTenantRoles => Set<UserTenantRole>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<TenantRole> TenantRoles => Set<TenantRole>();

    public DbSet<Person> Persons => Set<Person>();

    public DbSet<Address> Addresses => Set<Address>();

    public DbSet<Media> Media => Set<Media>();

    public DbSet<PermissionGroup> PermissionGroups => Set<PermissionGroup>();

    public DbSet<PermissionGroupPermission> PermissionGroupPermissions => Set<PermissionGroupPermission>();

    public DbSet<RolePermissionGroup> RolePermissionGroups => Set<RolePermissionGroup>();

    public DbSet<PermissionGroupTemplate> PermissionGroupTemplates => Set<PermissionGroupTemplate>();

    public DbSet<DashboardLayout> DashboardLayouts => Set<DashboardLayout>();

    public DbSet<UserGroup> UserGroups => Set<UserGroup>();

    public DbSet<UserGroupMember> UserGroupMembers => Set<UserGroupMember>();

    public DbSet<UserDepartment> UserDepartments => Set<UserDepartment>();

    public DbSet<SmtpAccount> SmtpAccounts => Set<SmtpAccount>();
    public DbSet<MaconomyConnection> MaconomyConnections => Set<MaconomyConnection>();

    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();

    // ---- Option Sets (tenant-configurable input value lists) ----
    public DbSet<OptionSet> OptionSets => Set<OptionSet>();
    public DbSet<OptionSetItem> OptionSetItems => Set<OptionSetItem>();

    // ---- Universal Features (Phase 14) ----
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<ConversationMessageMention> ConversationMessageMentions => Set<ConversationMessageMention>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<EntityTag> EntityTags => Set<EntityTag>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<Pin> Pins => Set<Pin>();
    public DbSet<ColourCode> ColourCodes => Set<ColourCode>();
    public DbSet<Checklist> Checklists => Set<Checklist>();
    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();
    public DbSet<StickyNote> StickyNotes => Set<StickyNote>();
    public DbSet<StickyNoteDismissal> StickyNoteDismissals => Set<StickyNoteDismissal>();
    public DbSet<UserStickyNoteState> UserStickyNoteStates => Set<UserStickyNoteState>();
    public DbSet<DeletedRecordRetentionConfig> DeletedRecordRetentionConfigs => Set<DeletedRecordRetentionConfig>();
    public DbSet<FieldModifiedLog> FieldModifiedLogs => Set<FieldModifiedLog>();
    public DbSet<ModifiedLogFieldConfig> ModifiedLogFieldConfigs => Set<ModifiedLogFieldConfig>();

    // ---- REMS (WO-110) ----
    public DbSet<REMS> Rems => Set<REMS>();
    public DbSet<REMSFiles> RemsFiles => Set<REMSFiles>();
    public DbSet<REMSAdditionalEntity> RemsAdditionalEntities => Set<REMSAdditionalEntity>();
    public DbSet<REMSAdditionalIndividual> RemsAdditionalIndividuals => Set<REMSAdditionalIndividual>();
    public DbSet<REMSSendBack> RemsSendBacks => Set<REMSSendBack>();
    public DbSet<REMSDelegation> RemsDelegations => Set<REMSDelegation>();
    public DbSet<REMSForm> RemsForms => Set<REMSForm>();
    public DbSet<REMSFormDraft> RemsFormDrafts => Set<REMSFormDraft>();
    public DbSet<REMSFormSubmission> RemsFormSubmissions => Set<REMSFormSubmission>();
    public DbSet<REMSFormEmailEvent> RemsFormEmailEvents => Set<REMSFormEmailEvent>();
    public DbSet<REMSClient> RemsClients => Set<REMSClient>();
    public DbSet<REMSEntity> RemsEntities => Set<REMSEntity>();
    public DbSet<REMSEntityAddress> RemsEntityAddresses => Set<REMSEntityAddress>();
    public DbSet<REMSEntityContact> RemsEntityContacts => Set<REMSEntityContact>();
    public DbSet<REMSEngagement> RemsEngagements => Set<REMSEngagement>();
    public DbSet<REMSEngagementAuditDetail> RemsEngagementAuditDetails => Set<REMSEngagementAuditDetail>();
    public DbSet<REMSEngagementGovernmentDetail> RemsEngagementGovernmentDetails => Set<REMSEngagementGovernmentDetail>();
    public DbSet<REMSEngagementPersonnelRate> RemsEngagementPersonnelRates => Set<REMSEngagementPersonnelRate>();
    public DbSet<REMSEngagementTaxDetail> RemsEngagementTaxDetails => Set<REMSEngagementTaxDetail>();
    public DbSet<REMSEngagementTaxForm> RemsEngagementTaxForms => Set<REMSEngagementTaxForm>();
    public DbSet<REMSEngagementMarketingMethod> RemsEngagementMarketingMethods => Set<REMSEngagementMarketingMethod>();
    public DbSet<REMSEngagementCommissionSplit> RemsEngagementCommissionSplits => Set<REMSEngagementCommissionSplit>();
    public DbSet<REMSEngagementApprover> RemsEngagementApprovers => Set<REMSEngagementApprover>();
    public DbSet<REMSApprovalRound> RemsApprovalRounds => Set<REMSApprovalRound>();
    public DbSet<REMSApprovalTask> RemsApprovalTasks => Set<REMSApprovalTask>();
    public DbSet<REMSApprovalChecklistItem> RemsApprovalChecklistItems => Set<REMSApprovalChecklistItem>();
    public DbSet<RemsSettings> RemsSettings => Set<RemsSettings>();
    public DbSet<RemsDepartmentDirector> RemsDepartmentDirectors => Set<RemsDepartmentDirector>();

    /// <summary>Data Protection key ring storage (Multi-Tenancy ADR-002).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EmsPortalDbContext).Assembly);

        // Tenant isolation filters, combined with the soft-delete filter. The tenant
        // filter is bypassed when no tenant is resolved so global background operations
        // still work; soft-deleted rows are always excluded.
        modelBuilder.Entity<AuditTrailEntry>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<PermissionGroup>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        // Persons are CRM master records owned by a tenant; scope them so a non-Super-Admin never
        // sees another tenant's people. Self-profile reads bypass this filter via GetByUserIdAsync.
        modelBuilder.Entity<Person>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        // User groups + memberships are tenant-scoped so a tenant only ever sees its own groups.
        modelBuilder.Entity<UserGroup>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<UserGroupMember>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<UserDepartment>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        // SMTP accounts are tenant-scoped; a tenant only ever sees its own mail accounts.
        modelBuilder.Entity<SmtpAccount>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<MaconomyConnection>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);

        // Universal Features (Phase 14): every UF table is tenant-scoped + soft-deletable, so it
        // carries the combined ambient-tenant + soft-delete filter. FieldModifiedLog is the lone
        // exception — it is append-only with no soft-delete column, so it uses a tenant-only filter.
        modelBuilder.Entity<ConversationMessage>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<ConversationMessageMention>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<Tag>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<EntityTag>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<Attachment>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<ActivityEvent>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<Reminder>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<Notification>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<NotificationPreference>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<Pin>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<ColourCode>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<Checklist>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<ChecklistItem>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<StickyNote>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<StickyNoteDismissal>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<UserStickyNoteState>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<DeletedRecordRetentionConfig>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<ModifiedLogFieldConfig>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<FieldModifiedLog>().HasQueryFilter(e => !_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId);

        // REMS (WO-110): every table is tenant-scoped. Most carry the combined ambient-tenant +
        // soft-delete filter; the two append-only tables (form submissions and email events) have no
        // logical delete and so use a tenant-only filter.
        modelBuilder.Entity<REMS>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSFiles>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSForm>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSFormDraft>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSClient>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSEntity>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSEntityAddress>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSEntityContact>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSEngagement>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSEngagementAuditDetail>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSEngagementGovernmentDetail>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSEngagementPersonnelRate>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSEngagementTaxDetail>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSEngagementTaxForm>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSEngagementMarketingMethod>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSEngagementCommissionSplit>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSEngagementApprover>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSApprovalRound>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSApprovalTask>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSApprovalChecklistItem>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<RemsSettings>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<RemsDepartmentDirector>().HasQueryFilter(e => (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId) && !e.Deleted);
        modelBuilder.Entity<REMSFormSubmission>().HasQueryFilter(e => !_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<REMSFormEmailEvent>().HasQueryFilter(e => !_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId);

        // Soft-delete filters for the non-tenant-scoped entities.
        modelBuilder.Entity<Tenant>().HasQueryFilter(e => !e.Deleted);
        modelBuilder.Entity<User>().HasQueryFilter(e => !e.Deleted);
        modelBuilder.Entity<UserTenantRole>().HasQueryFilter(e => !e.Deleted);
        modelBuilder.Entity<RefreshToken>().HasQueryFilter(e => !e.Deleted);
        // Soft-delete only, no tenant filter: password reset is an anonymous flow, so there is no resolved
        // tenant to filter by when the token is redeemed.
        modelBuilder.Entity<PasswordResetToken>().HasQueryFilter(e => !e.Deleted);
        // Email templates carry a nullable TenantId (null = platform default); a tenant filter would
        // hide the defaults, so they use the soft-delete filter only and are scoped explicitly in the repo.
        modelBuilder.Entity<EmailTemplate>().HasQueryFilter(e => !e.Deleted);
        // Option sets/items carry a nullable TenantId (null = platform-standard list); a tenant filter
        // would hide the standard lists, so they use the soft-delete filter only and are scoped
        // explicitly in the repository (standard rows ∪ the current tenant's rows).
        modelBuilder.Entity<OptionSet>().HasQueryFilter(e => !e.Deleted);
        modelBuilder.Entity<OptionSetItem>().HasQueryFilter(e => !e.Deleted);
        modelBuilder.Entity<Role>().HasQueryFilter(e => !e.Deleted);
        modelBuilder.Entity<TenantRole>().HasQueryFilter(e => !e.Deleted);
        modelBuilder.Entity<Address>().HasQueryFilter(e => !e.Deleted);
        modelBuilder.Entity<Media>().HasQueryFilter(e => !e.Deleted);
        modelBuilder.Entity<PermissionGroupTemplate>().HasQueryFilter(e => !e.Deleted);
        modelBuilder.Entity<DashboardLayout>().HasQueryFilter(e => !e.Deleted);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTenant();
        StampAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampTenant();
        StampAudit();
        return base.SaveChanges();
    }

    /// <summary>
    /// Stamps audit fields and converts deletes into soft-deletes. Created* is set on
    /// insert, Updated* on every change, and a requested delete becomes a Modified row
    /// flagged <see cref="AuditableEntity.Deleted"/> rather than being physically removed.
    /// </summary>
    private void StampAudit()
    {
        var now = DateTime.UtcNow;
        var actorId = Guid.TryParse(_actorAccessor.GetCurrentActor(), out var id) ? id : (Guid?)null;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedOnUtc = now;
                    entry.Entity.CreatedById = actorId;
                    entry.Entity.UpdatedOnUtc = now;
                    entry.Entity.UpdatedById = actorId;
                    entry.Entity.Deleted = false;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedOnUtc = now;
                    entry.Entity.UpdatedById = actorId;
                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.Deleted = true;
                    entry.Entity.DeletedOnUtc = now;
                    entry.Entity.UpdatedOnUtc = now;
                    entry.Entity.UpdatedById = actorId;
                    break;
            }
        }
    }

    /// <summary>Stamps the resolved tenant id on newly added tenant-scoped entities.</summary>
    private void StampTenant()
    {
        if (!_tenantContext.IsResolved)
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            switch (entry.Entity)
            {
                case AuditTrailEntry audit when audit.TenantId == Guid.Empty:
                    audit.TenantId = _tenantContext.TenantId;
                    break;
                case PermissionGroup permissionGroup when permissionGroup.TenantId == Guid.Empty:
                    permissionGroup.TenantId = _tenantContext.TenantId;
                    break;
                case Person person when person.TenantId is null || person.TenantId == Guid.Empty:
                    person.TenantId = _tenantContext.TenantId;
                    break;
                case UserGroup userGroup when userGroup.TenantId == Guid.Empty:
                    userGroup.TenantId = _tenantContext.TenantId;
                    break;
                case UserGroupMember member when member.TenantId == Guid.Empty:
                    member.TenantId = _tenantContext.TenantId;
                    break;
                case UserDepartment userDepartment when userDepartment.TenantId == Guid.Empty:
                    userDepartment.TenantId = _tenantContext.TenantId;
                    break;
                case SmtpAccount smtpAccount when smtpAccount.TenantId == Guid.Empty:
                    smtpAccount.TenantId = _tenantContext.TenantId;
                    break;
                case MaconomyConnection maconomyConnection when maconomyConnection.TenantId == Guid.Empty:
                    maconomyConnection.TenantId = _tenantContext.TenantId;
                    break;

                // ---- Universal Features (Phase 14) ----
                case ConversationMessage conversationMessage when conversationMessage.TenantId == Guid.Empty:
                    conversationMessage.TenantId = _tenantContext.TenantId;
                    break;
                case ConversationMessageMention conversationMessageMention when conversationMessageMention.TenantId == Guid.Empty:
                    conversationMessageMention.TenantId = _tenantContext.TenantId;
                    break;
                case Tag tag when tag.TenantId == Guid.Empty:
                    tag.TenantId = _tenantContext.TenantId;
                    break;
                case EntityTag entityTag when entityTag.TenantId == Guid.Empty:
                    entityTag.TenantId = _tenantContext.TenantId;
                    break;
                case Attachment attachment when attachment.TenantId == Guid.Empty:
                    attachment.TenantId = _tenantContext.TenantId;
                    break;
                case ActivityEvent activityEvent when activityEvent.TenantId == Guid.Empty:
                    activityEvent.TenantId = _tenantContext.TenantId;
                    break;
                case Reminder reminder when reminder.TenantId == Guid.Empty:
                    reminder.TenantId = _tenantContext.TenantId;
                    break;
                case Notification notification when notification.TenantId == Guid.Empty:
                    notification.TenantId = _tenantContext.TenantId;
                    break;
                case NotificationPreference notificationPreference when notificationPreference.TenantId == Guid.Empty:
                    notificationPreference.TenantId = _tenantContext.TenantId;
                    break;
                case Pin pin when pin.TenantId == Guid.Empty:
                    pin.TenantId = _tenantContext.TenantId;
                    break;
                case ColourCode colourCode when colourCode.TenantId == Guid.Empty:
                    colourCode.TenantId = _tenantContext.TenantId;
                    break;
                case Checklist checklist when checklist.TenantId == Guid.Empty:
                    checklist.TenantId = _tenantContext.TenantId;
                    break;
                case ChecklistItem checklistItem when checklistItem.TenantId == Guid.Empty:
                    checklistItem.TenantId = _tenantContext.TenantId;
                    break;
                case StickyNote stickyNote when stickyNote.TenantId == Guid.Empty:
                    stickyNote.TenantId = _tenantContext.TenantId;
                    break;
                case StickyNoteDismissal stickyNoteDismissal when stickyNoteDismissal.TenantId == Guid.Empty:
                    stickyNoteDismissal.TenantId = _tenantContext.TenantId;
                    break;
                case UserStickyNoteState userStickyNoteState when userStickyNoteState.TenantId == Guid.Empty:
                    userStickyNoteState.TenantId = _tenantContext.TenantId;
                    break;
                case DeletedRecordRetentionConfig retentionConfig when retentionConfig.TenantId == Guid.Empty:
                    retentionConfig.TenantId = _tenantContext.TenantId;
                    break;
                case FieldModifiedLog fieldModifiedLog when fieldModifiedLog.TenantId == Guid.Empty:
                    fieldModifiedLog.TenantId = _tenantContext.TenantId;
                    break;
                case ModifiedLogFieldConfig modifiedLogFieldConfig when modifiedLogFieldConfig.TenantId == Guid.Empty:
                    modifiedLogFieldConfig.TenantId = _tenantContext.TenantId;
                    break;

                // ---- REMS (WO-110) ----
                case REMS rems when rems.TenantId == Guid.Empty:
                    rems.TenantId = _tenantContext.TenantId;
                    break;
                case REMSFiles remsFiles when remsFiles.TenantId == Guid.Empty:
                    remsFiles.TenantId = _tenantContext.TenantId;
                    break;
                case REMSForm remsForm when remsForm.TenantId == Guid.Empty:
                    remsForm.TenantId = _tenantContext.TenantId;
                    break;
                case REMSFormDraft remsFormDraft when remsFormDraft.TenantId == Guid.Empty:
                    remsFormDraft.TenantId = _tenantContext.TenantId;
                    break;
                case REMSFormSubmission remsFormSubmission when remsFormSubmission.TenantId == Guid.Empty:
                    remsFormSubmission.TenantId = _tenantContext.TenantId;
                    break;
                case REMSFormEmailEvent remsFormEmailEvent when remsFormEmailEvent.TenantId == Guid.Empty:
                    remsFormEmailEvent.TenantId = _tenantContext.TenantId;
                    break;
                case REMSClient remsClient when remsClient.TenantId == Guid.Empty:
                    remsClient.TenantId = _tenantContext.TenantId;
                    break;
                case REMSEntity remsEntity when remsEntity.TenantId == Guid.Empty:
                    remsEntity.TenantId = _tenantContext.TenantId;
                    break;
                case REMSEntityAddress remsEntityAddress when remsEntityAddress.TenantId == Guid.Empty:
                    remsEntityAddress.TenantId = _tenantContext.TenantId;
                    break;
                case REMSEntityContact remsEntityContact when remsEntityContact.TenantId == Guid.Empty:
                    remsEntityContact.TenantId = _tenantContext.TenantId;
                    break;
                case REMSEngagement remsEngagement when remsEngagement.TenantId == Guid.Empty:
                    remsEngagement.TenantId = _tenantContext.TenantId;
                    break;
                case REMSEngagementAuditDetail remsAuditDetail when remsAuditDetail.TenantId == Guid.Empty:
                    remsAuditDetail.TenantId = _tenantContext.TenantId;
                    break;
                case REMSEngagementGovernmentDetail remsGovDetail when remsGovDetail.TenantId == Guid.Empty:
                    remsGovDetail.TenantId = _tenantContext.TenantId;
                    break;
                case REMSEngagementPersonnelRate remsPersonnelRate when remsPersonnelRate.TenantId == Guid.Empty:
                    remsPersonnelRate.TenantId = _tenantContext.TenantId;
                    break;
                case REMSEngagementTaxDetail remsTaxDetail when remsTaxDetail.TenantId == Guid.Empty:
                    remsTaxDetail.TenantId = _tenantContext.TenantId;
                    break;
                case REMSEngagementTaxForm remsTaxForm when remsTaxForm.TenantId == Guid.Empty:
                    remsTaxForm.TenantId = _tenantContext.TenantId;
                    break;
                case REMSEngagementMarketingMethod remsMarketingMethod when remsMarketingMethod.TenantId == Guid.Empty:
                    remsMarketingMethod.TenantId = _tenantContext.TenantId;
                    break;
                case REMSEngagementCommissionSplit remsCommissionSplit when remsCommissionSplit.TenantId == Guid.Empty:
                    remsCommissionSplit.TenantId = _tenantContext.TenantId;
                    break;
                case REMSEngagementApprover remsEngagementApprover when remsEngagementApprover.TenantId == Guid.Empty:
                    remsEngagementApprover.TenantId = _tenantContext.TenantId;
                    break;
                case REMSApprovalRound remsApprovalRound when remsApprovalRound.TenantId == Guid.Empty:
                    remsApprovalRound.TenantId = _tenantContext.TenantId;
                    break;
                case REMSApprovalTask remsApprovalTask when remsApprovalTask.TenantId == Guid.Empty:
                    remsApprovalTask.TenantId = _tenantContext.TenantId;
                    break;
                case REMSApprovalChecklistItem remsChecklistItem when remsChecklistItem.TenantId == Guid.Empty:
                    remsChecklistItem.TenantId = _tenantContext.TenantId;
                    break;
                case RemsSettings remsSettings when remsSettings.TenantId == Guid.Empty:
                    remsSettings.TenantId = _tenantContext.TenantId;
                    break;
                case RemsDepartmentDirector remsDepartmentDirector when remsDepartmentDirector.TenantId == Guid.Empty:
                    remsDepartmentDirector.TenantId = _tenantContext.TenantId;
                    break;
            }
        }
    }
}
