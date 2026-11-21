namespace EmsPortal.Shared.Security;

/// <summary>
/// The platform system roles enforced by RBAC (SuperAdmin &gt; TenantAdmin), the seeded REMS operational
/// roles (Partner/Admin), and the REMS SEAT roles a firm fills its engagements and approvals from. All
/// other access is governed by custom, permission-based roles.
/// </summary>
public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string TenantAdmin = "TenantAdmin";

    // REMS operational roles (WO-122). Seeded system roles carrying the REMS permission sets; assigned
    // per (user, tenant) alongside any other roles (multi-role assignments).
    public const string Partner = "Partner";
    public const string Admin = "Admin";

    // ---- REMS seat roles ----
    //
    // What a person IS to the firm. Holding one makes them offerable in the picker that fills that seat
    // on an engagement; the names carry spaces because the picker and the roles UI show them as they are.
    // A CSE grants nothing. An Engagement Executive or Billing Manager may also raise requests, for
    // clients already on file only (Permissions.ForExistingClientInitiator).
    public const string Cse = "CSE";
    public const string EngagementExecutive = "Engagement Executive";
    public const string BillingManager = "Billing Manager";

    /// <summary>
    /// On every engagement's approver list by standing, like the Department Director and the CSE: nothing
    /// writes its holders onto the editable list, so nothing takes them off. Grants no permission.
    /// </summary>
    public const string ManagingShareholder = "Managing Shareholder";

    /// <summary>A shareholder of the firm. A standing only: it grants nothing and no longer approves.</summary>
    public const string Shareholder = "Shareholder";

    // There is deliberately no Approver role: the "add approvers" picker offers every user in the tenant,
    // because an engagement can need a signature from anyone. Deciding an approval task is authorised by
    // OWNING the task, never by a role.
}
