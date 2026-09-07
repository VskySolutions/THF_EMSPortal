namespace EmsPortal.Api.Dashboard;

/// <summary>Role-based default dashboard widget orders.</summary>
public static class DashboardDefaultLayouts
{
    /// <summary>Widgets every authenticated user sees when no role-specific layout applies.</summary>
    public static readonly string[] Common = Array.Empty<string>();

    /// <summary>Tenant Admin = user widgets.</summary>
    public static readonly string[] TenantAdmin = { "userSummary", "userRoleDistribution" };

    /// <summary>Super Admin = TenantAdmin + platform/cross-tenant widgets.</summary>
    public static readonly string[] SuperAdmin = TenantAdmin.Concat(new[]
    {
        "tenantKpiCards", "tenantHealthTable", "platformGrowthChart",
        "tenantOnboardingPanel", "systemAlertsPanel", "platformUserAnalytics",
    }).ToArray();

    /// <summary>The ordered default widget keys for a resolved dashboard role.</summary>
    public static IReadOnlyList<string> For(DashboardRole role) => role switch
    {
        DashboardRole.SuperAdmin => SuperAdmin,
        DashboardRole.TenantAdmin => TenantAdmin,
        _ => Common,
    };

    /// <summary>Widgets hidden by default.</summary>
    public static IReadOnlyList<string> DefaultHiddenFor(DashboardRole role) => Array.Empty<string>();
}

/// <summary>The dashboard layout tier a caller resolves to (independent of the RBAC role string).</summary>
public enum DashboardRole
{
    Common = 0,
    TenantAdmin = 1,
    SuperAdmin = 2,
}
