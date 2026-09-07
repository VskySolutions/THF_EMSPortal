import { ref, watch, onMounted } from "vue";
import { optionSetApi, getApiErrorMessage, EntityType } from "services/api";
import { useEntityMeta } from "composables/uf/useEntityMeta";

// The entity types that can carry option lists (mirrors the backend EntityType enum), as
// ready-to-use { label, value } select options. Reused by list filters and the set form.
export function useEntityTypeOptions () {
  const { labelFor } = useEntityMeta();
  const options = Object.values(EntityType).map((value) => ({ label: labelFor(value), value }));
  return { options };
}

// Loads the effective, ordered, active values for a (entityType, key) — optionally filtered by a parent
// item for cascading lists.
export function useOptionSetOptions (getParams) {
  const options = ref([]);
  const loading = ref(false);
  const error = ref(null);

  const reload = async () => {
    const { entityType, key, parentItemId } = getParams() || {};
    if (!entityType || !key) {
      options.value = [];
      return;
    }
    loading.value = true;
    error.value = null;
    try {
      const items = await optionSetApi.resolve({ entityType, key, parentItemId: parentItemId || undefined });
      // Keep the raw item so callers can read metadata / default / parent links.
      options.value = (items || []).map((i) => ({ label: i.label, value: i.value, raw: i }));
    } catch (err) {
      error.value = getApiErrorMessage(err);
      options.value = [];
    } finally {
      loading.value = false;
    }
  };

  watch(() => getParams(), reload, { deep: true });
  onMounted(reload);

  return { options, loading, error, reload };
}
