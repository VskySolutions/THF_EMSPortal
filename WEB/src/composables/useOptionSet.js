import { ref, watch, onMounted } from "vue";
import { optionSetApi, getApiErrorMessage } from "services/api";

// Loads the effective, ordered, active values for a list key — optionally filtered by a parent item for
// dependent lists.
export function useOptionSetOptions (getParams) {
  const options = ref([]);
  const loading = ref(false);
  const error = ref(null);

  const reload = async () => {
    const { key, parentItemId } = getParams() || {};
    if (!key) {
      options.value = [];
      return;
    }
    loading.value = true;
    error.value = null;
    try {
      const items = await optionSetApi.resolve({ key, parentItemId: parentItemId || undefined });
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
