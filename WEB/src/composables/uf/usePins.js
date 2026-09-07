import { ref } from "vue";
import { ufPinApi } from "services/api";
import { useNotify } from "composables/useNotify";

// Reactive pin state for a single entity record (detail header Pin toggle).
export function usePins (entityType, entityId, initialPinned = false) {
  const notify = useNotify();
  // Seed from a known state (e.g. a list that already loaded the user's pins) for instant display;
  // refresh() still reconciles and resolves the pin id needed to unpin.
  const pinned = ref(initialPinned);
  const pinId = ref(null);
  const busy = ref(false);

  const refresh = async () => {
    try {
      // Scan the user's pins for this record (small set — max 50 per user).
      const res = await ufPinApi.list({ page: 1, limit: 50 });
      const match = (res?.data || []).find(
        (p) => Number(p.entityType) === Number(entityType) && p.entityId === entityId
      );
      pinned.value = !!match;
      pinId.value = match?.id || null;
    } catch {
      // ignore — toggle still works
    }
  };

  const toggle = async () => {
    if (busy.value) return;
    busy.value = true;
    try {
      if (pinned.value && pinId.value) {
        await ufPinApi.remove(pinId.value);
        pinned.value = false;
        pinId.value = null;
        notify.info("Removed from pinned.");
      } else {
        const pin = await ufPinApi.create({ entityType, entityId });
        pinned.value = true;
        pinId.value = pin?.id || null;
        notify.success("Pinned.");
      }
    } catch (err) {
      notify.error(err?.response?.data?.error?.details || "Could not update pin.");
    } finally {
      busy.value = false;
    }
  };

  return { pinned, busy, refresh, toggle };
}
