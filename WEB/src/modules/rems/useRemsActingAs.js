import { ref, computed } from "vue";
import { LocalStorage } from "quasar";
import { remsApi } from "services/api";

// Whose REMS work the user is currently doing — their own, or a principal who has delegated to them.
const STORAGE_KEY = "remsActingForUserId";

const options = ref([]);
const loaded = ref(false);
const actingForId = ref(LocalStorage.getItem(STORAGE_KEY) || null);

export function useRemsActingAs () {
  const load = async () => {
    try {
      options.value = (await remsApi.actingFor()) || [];
    } catch {
      // No delegations is the overwhelmingly common case and reads the same as a failed lookup: act as
      // yourself. Not worth an error banner on every page load.
      options.value = [];
    }
    loaded.value = true;
    // A grant that has lapsed or been withdrawn since the choice was made is no longer offered, so the
    // stored id would keep being sent and keep being refused. Drop it rather than leave it dangling.
    if (actingForId.value && !options.value.some((o) => o.principalUserId === actingForId.value)) {
      setActingFor(null);
    }
  };

  const setActingFor = (principalUserId) => {
    actingForId.value = principalUserId || null;
    if (principalUserId) LocalStorage.set(STORAGE_KEY, principalUserId);
    else LocalStorage.remove(STORAGE_KEY);
  };

  const current = computed(() =>
    options.value.find((o) => o.principalUserId === actingForId.value) || null);

  return {
    /** Principals this user may act for today. Empty for almost everyone. */
    options,
    loaded,
    /** The chosen principal, or null when acting as themselves. */
    current,
    actingForId,
    /** Whether there is any choice to make — the picker hides itself entirely when there is not. */
    hasDelegations: computed(() => options.value.length > 0),
    /** What the chosen seat allows; acting as yourself is unrestricted. */
    canSend: computed(() => (current.value ? current.value.canSend : true)),
    load,
    setActingFor
  };
}
