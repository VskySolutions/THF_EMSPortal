import { ref, watch, onMounted, unref } from "vue";
import { dashboardApi, getApiErrorMessage } from "services/api";

// Dashboard data composables (WO-73).

const paramsFrom = (dateRange, tenantId) => {
  const params = { dateRange: unref(dateRange) };
  const tid = unref(tenantId);
  if (tid) params.tenantId = tid;
  return params;
};

// ---- Users ----
export function useUserDashboard (dateRange, tenantId = null) {
  const loading = ref(false);
  const error = ref(null);
  const kpis = ref(null);
  const roleDistribution = ref([]);
  const activityFeed = ref([]);

  const refresh = async () => {
    loading.value = true;
    error.value = null;
    try {
      const data = await dashboardApi.users(paramsFrom(dateRange, tenantId));
      kpis.value = data?.kpis ?? null;
      roleDistribution.value = data?.roleDistribution ?? [];
      activityFeed.value = data?.activityFeed ?? [];
    } catch (err) {
      error.value = getApiErrorMessage(err);
    } finally {
      loading.value = false;
    }
  };

  watch(() => [unref(dateRange), unref(tenantId)], refresh);
  onMounted(refresh);

  return { loading, error, refresh, kpis, roleDistribution, activityFeed };
}

// ---- Platform (Super Admin) ----
export function usePlatformDashboard (dateRange, tenantId = null) {
  const loading = ref(false);
  const error = ref(null);
  const tenantKpis = ref(null);
  const tenantHealth = ref([]);
  const growth = ref([]);
  const onboarding = ref([]);
  const systemAlerts = ref([]);
  const userAnalytics = ref(null);

  // The platform endpoint takes no tenantId; `forceRefresh` bypasses the server cache.
  const refresh = async (forceRefresh = false) => {
    loading.value = true;
    error.value = null;
    try {
      const data = await dashboardApi.platform({ dateRange: unref(dateRange) }, forceRefresh);
      tenantKpis.value = data?.tenantKpis ?? null;
      tenantHealth.value = data?.tenantHealth ?? [];
      growth.value = data?.growth ?? [];
      onboarding.value = data?.onboarding ?? [];
      systemAlerts.value = data?.systemAlerts ?? [];
      userAnalytics.value = data?.userAnalytics ?? null;
    } catch (err) {
      error.value = getApiErrorMessage(err);
    } finally {
      loading.value = false;
    }
  };

  watch(() => [unref(dateRange), unref(tenantId)], () => refresh(false));
  onMounted(() => refresh(false));

  return {
    loading,
    error,
    refresh,
    tenantKpis,
    tenantHealth,
    growth,
    onboarding,
    systemAlerts,
    userAnalytics
  };
}
