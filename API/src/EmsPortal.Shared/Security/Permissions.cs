namespace EmsPortal.Shared.Security;

/// <summary>
/// The platform permission catalogue. Custom roles (RBAC) are composed of these
/// permission keys; API endpoints are gated by them (replacing fixed role policies
/// over the RBAC rollout). Keys are stable strings of the form "area.action".
/// </summary>
public static class Permissions
{
    // Tenants
    public const string TenantsRead = "tenants.read";
    public const string TenantsWrite = "tenants.write";
    public const string TenantsArchive = "tenants.archive";

    // Persons (CRM master records — the precursor to a login account)
    public const string PersonsRead = "persons.read";
    public const string PersonsWrite = "persons.write";
    public const string PersonsDelete = "persons.delete";

    // Users
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";
    public const string UsersResetPassword = "users.reset_password";
    /// <summary>Create user groups and assign/remove users from them (and delete groups).</summary>
    public const string UsersGroupManagement = "users.groupManagement";

    // Roles (RBAC management)
    public const string RolesRead = "roles.read";
    public const string RolesWrite = "roles.write";
    public const string RolesAssign = "roles.assign";

    // Permission Groups (RBAC composition layer)
    /// <summary>Create, edit, and compose Permission Groups (and compose them into roles).</summary>
    public const string GroupsManage = "groups.manage";

    // SMTP Email Accounts
    /// <summary>Create, edit, delete, set-active, and test-send SMTP email accounts. Reads require only <see cref="UsersRead"/>.</summary>
    public const string EmailManage = "email.manage";

    // Integrations
    /// <summary>Set up, test and remove the tenant's Maconomy connection. The customer lookup it powers needs no permission beyond signing in.</summary>
    public const string IntegrationsMaconomyManage = "integrations.maconomy.manage";

    // Universal Features (Phase 14)
    /// <summary>Manage tenant-wide Universal Feature settings: tags, shared saved views, tenant sticky
    /// notes, and Modified Log field configuration.</summary>
    public const string SettingsManage = "settings.manage";
    /// <summary>View, restore, and permanently delete soft-deleted records (Deleted Records Management).</summary>
    public const string RecordsAdminDelete = "records.adminDelete";

    // Option Sets (tenant-configurable input value lists)
    /// <summary>Read option lists and their values (for pickers and the management UI).</summary>
    public const string OptionSetsRead = "optionSets.read";
    /// <summary>Create, edit, reorder, and delete a tenant's own option lists and values.</summary>
    public const string OptionSetsManage = "optionSets.manage";

    // REMS — Real Estate Management System (WO-110+). The operational roles Partner/Admin are composed of
    // these keys (see ForPartner/ForAdmin). The four seat roles are composed of none of them, deliberately.
    /// <summary>View REMS requests.</summary>
    public const string RemsRequestsRead = "rems.requests.read";
    /// <summary>Create REMS requests.</summary>
    public const string RemsRequestsCreate = "rems.requests.create";
    /// <summary>Edit REMS requests.</summary>
    public const string RemsRequestsUpdate = "rems.requests.update";
    /// <summary>Delete REMS requests.</summary>
    public const string RemsRequestsDelete = "rems.requests.delete";
    /// <summary>
    /// Pick a REMS request up from EMS Review, and hand it back. Nobody assigns anybody — the key means
    /// "may claim work off the shared queue", so it belongs to the admins who work that queue and to
    /// nobody else. The name is historical; read it as the pick-up right.
    /// </summary>
    public const string RemsRequestsAssign = "rems.requests.assign";
    // Which requests an admin may SEE is deliberately not a permission of its own: EMS Review is the whole
    // tenant's queue and every admin reads all of it, because a request waiting for pickup has to be
    // visible to the person who might pick it up.
    /// <summary>Create, edit, and configure REMS forms.</summary>
    public const string RemsFormsManage = "rems.forms.manage";
    /// <summary>Send REMS forms to recipients.</summary>
    public const string RemsFormsSend = "rems.forms.send";
    /// <summary>Create and manage REMS engagements.</summary>
    public const string RemsEngagementsManage = "rems.engagements.manage";
    /// <summary>Initiate/route a REMS approval round.</summary>
    public const string RemsApprovalsSend = "rems.approvals.send";
    // There is deliberately no "act on an approval task" permission. Approver-ness is data, not a role —
    // the CSE, each commission recipient and anyone added on the Approval tab become approvers, and any
    // role can end up in one of those seats. A permission gate could therefore contradict the engagement
    // data and lock a real approver out of a task created for them. Ownership is the check instead:
    // RemsApprovalController requires an authenticated caller and ApproverId == caller on every task.
    /// <summary>Read the REMS email log.</summary>
    public const string RemsEmailLogRead = "rems.emailLog.read";

    /// <summary>
    /// Configure REMS for the tenant: which user directs each department, and whatever else becomes
    /// tenant-wide REMS setup. Separate from <see cref="RemsEngagementsManage"/> on purpose — working the
    /// engagements that flow through a configuration is not the same right as deciding the configuration,
    /// and every REMS Admin holds the former.
    /// </summary>
    public const string RemsSettingsManage = "rems.settings.manage";

    /// <summary>
    /// Arrange REMS delegation on somebody ELSE's behalf — naming who may prepare and send a person's
    /// requests while they are away. Naming your OWN delegates is self-service and needs no permission.
    /// Its own key rather than users.write: handing one person another's work is a REMS decision, and
    /// the right to edit an account says nothing about it.
    /// </summary>
    public const string RemsDelegationsManage = "rems.delegations.manage";

    /// <summary>Every defined permission key.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        TenantsRead, TenantsWrite, TenantsArchive,
        PersonsRead, PersonsWrite, PersonsDelete,
        UsersRead, UsersWrite, UsersResetPassword, UsersGroupManagement,
        RolesRead, RolesWrite, RolesAssign,
        GroupsManage,
        EmailManage,
        IntegrationsMaconomyManage,
        SettingsManage, RecordsAdminDelete,
        OptionSetsRead, OptionSetsManage,
        RemsRequestsRead, RemsRequestsCreate, RemsRequestsUpdate, RemsRequestsDelete, RemsRequestsAssign,
        RemsFormsManage, RemsFormsSend, RemsEngagementsManage,
        RemsApprovalsSend, RemsEmailLogRead, RemsSettingsManage, RemsDelegationsManage
    };

    /// <summary>Permission sets for the seeded system roles.</summary>
    public static IReadOnlyList<string> ForSuperAdmin() => All;

    public static IReadOnlyList<string> ForTenantAdmin() => new[]
    {
        TenantsRead,
        // Deleting persons stays Super-Admin-only (PersonsDelete intentionally excluded here).
        PersonsRead, PersonsWrite,
        UsersRead, UsersWrite, UsersResetPassword, UsersGroupManagement,
        // Tenant Admins manage the roles of users in their OWN tenant, and build roles of their own to
        // assign. The permission alone is not the whole boundary. UsersController confines them to their
        // active tenant, refuses to grant the Super Admin role, refuses a Super Admin target, and refuses
        // a role another tenant owns; RolesController confines roles.write to the roles their own tenant
        // owns — the platform roles, this one included, stay a Super Admin's to change — and holds the
        // keys they may put in one inside the tenant ceiling (ADR-003).
        RolesRead, RolesWrite, RolesAssign,
        // Tenant Admins manage Permission Groups within their own tenant.
        GroupsManage,
        // Tenant Admins manage their tenant's SMTP email accounts.
        EmailManage,
        // Tenant Admins connect their tenant to Maconomy.
        IntegrationsMaconomyManage,
        // Tenant Admins manage tenant-wide UF settings and the deleted-records lifecycle.
        SettingsManage, RecordsAdminDelete,
        // Tenant Admins manage their tenant's option lists.
        OptionSetsRead, OptionSetsManage,
        // Full REMS access within their tenant — the same set a REMS Admin holds. Tenant isolation still
        // applies: this widens WHAT they can do in their tenant, never WHICH tenant.
        RemsRequestsRead, RemsRequestsCreate, RemsRequestsUpdate, RemsRequestsDelete, RemsRequestsAssign,
        RemsFormsManage, RemsFormsSend, RemsEngagementsManage,
        RemsApprovalsSend, RemsEmailLogRead, RemsSettingsManage, RemsDelegationsManage
    };

    /// <summary>REMS Partner: works their own requests (read/create/update) and sends them to the client.</summary>
    public static IReadOnlyList<string> ForPartner() => new[]
    {
        // RemsFormsSend is a Partner permission: the initiator emails the intake link to the client
        // themselves rather than handing the request to an admin to send.
        //
        // RemsRequestsAssign is NOT: it means "may pick a request up", which is the admins' move on a
        // queue a partner never works.
        RemsRequestsRead, RemsRequestsCreate, RemsRequestsUpdate,
        // The email log follows the sending: the person chasing a client is the one who needs to know
        // whether the last three emails reached them, and since Phase 16 that person is the initiator
        // rather than an admin. Reading it is still record-scoped on top of this — the endpoint asks
        // RemsSetupAccess.CanRead, so holding the key is not permission to read every request's log.
        RemsFormsSend, RemsEmailLogRead, OptionSetsRead
    };

    /// <summary>REMS Admin: full request lifecycle plus pool, forms, engagements, approvals routing and the email log.</summary>
    public static IReadOnlyList<string> ForAdmin() => new[]
    {
        RemsRequestsRead, RemsRequestsCreate, RemsRequestsUpdate, RemsRequestsDelete, RemsRequestsAssign,
        RemsFormsManage, RemsFormsSend, RemsEngagementsManage,
        RemsApprovalsSend, RemsEmailLogRead, OptionSetsRead,
        // The firm's REMS setup and its cover arrangements are run by the people who run REMS.
        RemsSettingsManage, RemsDelegationsManage
    };

    /// <summary>
    /// The REMS seat roles — CSE, Engagement Executive, Billing Manager and Shareholder. They grant
    /// nothing, and that is the point: each one marks somebody as offerable in the picker that fills that
    /// seat on an engagement — or, for a Shareholder, as an automatic approver on every engagement —
    /// and what they may then do follows from being ON the engagement, not from a permission key. A CSE
    /// approves because they are this engagement's CSE; requiring a permission to do it could only ever
    /// lock a genuine one out.
    /// <para>
    /// One set for all four because there is nothing to tell apart — they are directories, not
    /// capabilities. The retired Approver role was the same idea and the same empty set.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> ForSeatRole() => Array.Empty<string>();

    /// <summary>
    /// The seeded permission set for a system role name (SuperAdmin/TenantAdmin, the REMS operational
    /// roles Partner/Admin, and the four seat roles), or an empty set for any other name (including custom
    /// roles). Used as the fallback when an assignment carries no explicit permission keys and when a
    /// caller holds only a role claim (API-key callers, pre-RBAC tokens).
    /// </summary>
    public static IReadOnlyList<string> ForSystemRole(string? roleName) => roleName switch
    {
        Roles.SuperAdmin => ForSuperAdmin(),
        Roles.TenantAdmin => ForTenantAdmin(),
        Roles.Partner => ForPartner(),
        Roles.Admin => ForAdmin(),
        // Listed rather than left to fall through, so the seat roles are visibly a decision — they grant
        // nothing on purpose, which reads very differently from a name nobody thought about.
        Roles.Cse or Roles.EngagementExecutive or Roles.BillingManager or Roles.Shareholder
            => ForSeatRole(),
        _ => Array.Empty<string>(),
    };
}
