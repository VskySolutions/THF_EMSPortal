import { computed, ref } from "vue";
import { LocalStorage } from "quasar";
import { tenantApi } from "services/api";
import { useAuthStore } from "stores/auth";
import { useTenantStore } from "stores/tenant";

// Super-Admin tenant scope.
export const TENANT_SCOPE_KEY = "adminTenantOverride";

const scopeTenantId = ref(LocalStorage.getItem(TENANT_SCOPE_KEY) || null);
const tenantOptions = ref([]);
const loadingTenants = ref(false);
// The fetch currently in flight, shared by every caller: the layout and the toolbar button both ask for
// the list as they mount, and one request should serve them both.
let inflight = null;
// Bumped whenever the cache is deliberately emptied, so a request already in the air cannot land
// afterwards and fill it back in.
let generation = 0;

/** Forget everything this module is holding. */
const forget = () => {
  generation += 1;
  scopeTenantId.value = null;
  tenantOptions.value = [];
  loadingTenants.value = false;
  inflight = null;
};

// Never rejects: an unreachable list is a dropdown that stays empty, not an error for a caller to handle.
const fetchTenants = async () => {
  const mine = generation;
  loadingTenants.value = true;
  try {
    const resp = await tenantApi.list({ page: 1, limit: 100 });
    if (mine !== generation) return; // forgotten while this was in the air
    tenantOptions.value = (resp?.data || []).map((t) => ({ label: t.name, value: t.tenantId }));
  } catch {
    // non-fatal: the dropdown simply stays empty
  } finally {
    if (mine === generation) loadingTenants.value = false;
  }
};

if (typeof window !== "undefined") {
  window.addEventListener("session-cleared", forget);
}

export function useTenantScope () {
  const authStore = useAuthStore();
  const tenantStore = useTenantStore();

  // Mirrors the backend guard exactly — the header is only honoured for a Super Admin, so nobody else is
  // shown a control that would silently do nothing.
  const canScopeTenant = computed(() => authStore.roles.includes("SuperAdmin"));

  // Falls back to the caller's own tenant so the dropdown always shows where they actually are.
  const selectedTenantId = computed(() => scopeTenantId.value || tenantStore.activeTenantId);

  // True while viewing somebody else's tenant — worth saying out loud, since every screen is affected.
  const isScoped = computed(() =>
    !!scopeTenantId.value && scopeTenantId.value !== tenantStore.activeTenantId);

  const scopedTenantName = computed(() =>
    tenantOptions.value.find((t) => t.value === selectedTenantId.value)?.label || "");

  // `force` re-reads a list already held.
  const loadTenants = async ({ force = false } = {}) => {
    if (!canScopeTenant.value) return;
    if (inflight) {
      await inflight;
      // A forced read cannot settle for the answer that request is giving: it went out BEFORE the change
      // it is being asked to reflect. Everyone else has now been served by it.
      if (!force) return;
    }
    if (!force && tenantOptions.value.length) return;
    const run = fetchTenants();
    inflight = run;
    try {
      await run;
    } finally {
      if (inflight === run) inflight = null;
    }
  };

  /** Re-read the list after a tenant has been created, renamed, deactivated or archived. */
  const refreshTenants = () => loadTenants({ force: true });

  const setScope = (tenantId) => {
    const next = tenantId || null;
    if (next === scopeTenantId.value) return;
    scopeTenantId.value = next;
    if (next) LocalStorage.set(TENANT_SCOPE_KEY, next);
    else LocalStorage.remove(TENANT_SCOPE_KEY);
    // Every list page already reloads on this event (see useListTable), so one dispatch refreshes the app
    // instead of each page wiring up its own watcher.
    window.dispatchEvent(new Event("tenant-switched"));
  };

  const clearScope = () => setScope(null);

  return {
    canScopeTenant,
    selectedTenantId,
    isScoped,
    scopedTenantName,
    tenantOptions,
    loadingTenants,
    loadTenants,
    refreshTenants,
    setScope,
    clearScope
  };
}
