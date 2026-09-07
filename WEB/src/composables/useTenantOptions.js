import { ref, computed } from "vue";
import { tenantApi } from "services/api";
import { useTenantStore } from "stores/tenant";
import { usePermissions, Permissions } from "composables/usePermissions";

// Tenant-selection helper shared by every form that assigns a tenant (user create, person create/edit).
export function useTenantOptions () {
  const tenantStore = useTenantStore();
  const { has } = usePermissions();

  const canChooseTenant = computed(() => has(Permissions.TenantsWrite));
  const activeTenantId = computed(() => tenantStore.activeTenantId);

  const tenantOptions = ref([]);
  const loadingTenants = ref(false);

  const loadTenants = async () => {
    if (!canChooseTenant.value || tenantOptions.value.length) return;
    loadingTenants.value = true;
    try {
      const resp = await tenantApi.list({ page: 1, limit: 100 });
      tenantOptions.value = (resp?.data || []).map((t) => ({ label: t.name, value: t.tenantId }));
    } catch {
      // non-fatal: the dropdown simply stays empty
    } finally {
      loadingTenants.value = false;
    }
  };

  const tenantName = (id) => tenantOptions.value.find((t) => t.value === id)?.label || id;

  return { canChooseTenant, activeTenantId, tenantOptions, loadingTenants, loadTenants, tenantName };
}
