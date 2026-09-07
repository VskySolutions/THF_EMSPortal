// Dashboard widget registry (WO-73) — the single source of truth for every dashboard widget.

export const WIDGETS = [
  // ---- TENANT ADMIN ----
  { key: "userSummary", title: "User Summary", description: "User counts and engagement.", role: "tenantAdmin", category: "users", component: () => import("../tenant/UserSummaryPanel.vue") },
  { key: "userRoleDistribution", title: "Role Distribution", description: "Users by role.", role: "tenantAdmin", category: "users", component: () => import("../tenant/UserRoleDistributionChart.vue") },

  // ---- SUPER ADMIN (platform) ----
  { key: "tenantKpiCards", title: "Tenant KPIs", description: "Platform-wide tenant and activity metrics.", role: "superAdmin", category: "platform", component: () => import("../super/TenantKpiCards.vue") },
  { key: "tenantHealthTable", title: "Tenant Health", description: "Per-tenant activity status.", role: "superAdmin", category: "platform", component: () => import("../super/TenantHealthTable.vue") },
  { key: "platformGrowthChart", title: "Platform Growth", description: "Tenant and user growth over time.", role: "superAdmin", category: "platform", component: () => import("../super/PlatformGrowthChart.vue") },
  { key: "tenantOnboardingPanel", title: "Tenant Onboarding", description: "Tenants with incomplete setup.", role: "superAdmin", category: "platform", component: () => import("../super/TenantOnboardingPanel.vue") },
  { key: "systemAlertsPanel", title: "System Alerts", description: "Platform-wide alerts by severity.", role: "superAdmin", category: "platform", component: () => import("../super/SystemAlertsPanel.vue") },
  { key: "platformUserAnalytics", title: "User Analytics", description: "Cross-tenant user analytics.", role: "superAdmin", category: "platform", component: () => import("../super/PlatformUserAnalytics.vue") }
];

// Ordered key lists used when the server returns no saved layout. Mirrors the backend defaults:
// there are no common-tier widgets; tenantAdmin = user keys; superAdmin adds the platform keys.
const COMMON_KEYS = WIDGETS.filter((w) => w.role === "common").map((w) => w.key);
const TENANT_KEYS = WIDGETS.filter((w) => w.role === "tenantAdmin").map((w) => w.key);
const SUPER_KEYS = WIDGETS.filter((w) => w.role === "superAdmin").map((w) => w.key);

export function defaultLayoutForRole (role) {
  if (role === "superAdmin") return [...COMMON_KEYS, ...TENANT_KEYS, ...SUPER_KEYS];
  if (role === "tenantAdmin") return [...COMMON_KEYS, ...TENANT_KEYS];
  return [...COMMON_KEYS];
}

// Widgets visible to a role: tenantAdmin sees all tenant widgets; superAdmin sees everything.
export function widgetsForRole (role) {
  const keys = new Set(defaultLayoutForRole(role));
  return WIDGETS.filter((w) => keys.has(w.key));
}

// No widgets are hidden by default — every visible widget shows on first load.
export function defaultHiddenForRole () {
  return [];
}

// Quick lookup by key.
export const WIDGETS_BY_KEY = Object.freeze(
  WIDGETS.reduce((map, w) => { map[w.key] = w; return map; }, {}));
