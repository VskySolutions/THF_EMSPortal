import { ref } from "vue";
import { ufColourApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";

// The swatches a user may tint a row with.
export const ROW_COLOUR_PALETTE = Object.freeze([
  "#ef5350", "#ec407a", "#ab47bc", "#5c6bc0", "#42a5f5", "#26a69a", "#9ccc65", "#ffa726"
]);

// Batch colour-code fetch + write for a list page: given the visible rows' ids, keeps a reactive map {
// entityId: colour } for AppDataTable's `row-colours`, and writes one row's colour back.
export function useColourCodes (entityType) {
  const notify = useNotify();
  const colours = ref({});

  const loadFor = async (entityIds) => {
    const ids = (entityIds || []).filter(Boolean);
    if (!ids.length) {
      colours.value = {};
      return;
    }
    try {
      colours.value = (await ufColourApi.batch(entityType, ids)) || {};
    } catch {
      // A list that cannot read its colours is still a list. Silent because the failure costs the reader
      // nothing they had — unlike a failed WRITE below, which loses something they just did.
      colours.value = {};
    }
  };

  const colourOf = (entityId) => colours.value[entityId] || null;

  /** Set (or, with a null colour, clear) one row's tint. Patched locally so the row turns at once. */
  const setColour = async (entityId, colour) => {
    try {
      await ufColourApi.upsert({ entityType, entityId, colour });
      // Replaced rather than mutated: the map backs a computed the table reads, and a key deleted in
      // place does not always reach it.
      const next = { ...colours.value };
      if (colour) next[entityId] = colour;
      else delete next[entityId];
      colours.value = next;
      return true;
    } catch (err) {
      notify.error(getApiErrorMessage(err));
      return false;
    }
  };

  return { colours, loadFor, colourOf, setColour };
}
