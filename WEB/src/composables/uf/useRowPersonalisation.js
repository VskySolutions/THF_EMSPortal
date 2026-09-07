import { ref, computed } from "vue";
import { ufPinApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useColourCodes, ROW_COLOUR_PALETTE } from "composables/uf/useColourCodes";

/** The two PERSONAL marks a user may put on a list row: a pin that floats it to the top, and a colour that
    tints it. */

// Mirrors PersonalFeaturesController.MaxPinsPerType. Held here so the button can say why it is disabled
// rather than letting the click come back a 400.
export const MAX_PINS_PER_TYPE = 5;

// The user's whole pin set fits in one page — the server caps it at 50 across every entity type.
const PIN_PAGE_SIZE = 50;

// What to call the row in a confirmation. A list that passes no label still gets a sentence that reads,
// rather than one with a hole where the record's name should be.
const named = (label, fallback = "Record") => (String(label || "").trim() || fallback);

export function useRowPersonalisation (entityType) {
  const notify = useNotify();
  const { colours, loadFor, colourOf, setColour } = useColourCodes(entityType);

  // entityId → pin id. The id is what unpinning needs, so the map holds it rather than a bare Set.
  const pins = ref(new Map());
  const pinsLoaded = ref(false);
  // The row a write is in flight for, so exactly one control spins.
  const busyId = ref("");

  const pinnedRowKeys = computed(() => [...pins.value.keys()]);
  const pinCount = computed(() => pins.value.size);
  const pinLimitReached = computed(() => pins.value.size >= MAX_PINS_PER_TYPE);

  const isPinned = (entityId) => pins.value.has(entityId);

  const loadPins = async () => {
    try {
      const res = await ufPinApi.list({ page: 1, limit: PIN_PAGE_SIZE });
      const next = new Map();
      (res?.data || [])
        .filter((p) => Number(p.entityType) === Number(entityType))
        .forEach((p) => next.set(p.entityId, p.id));
      pins.value = next;
      pinsLoaded.value = true;
    } catch {
      // Same bargain as the colours: a list that cannot read the marks is still a list.
    }
  };

  /** Call after every load with the ids now on screen. */
  const sync = async (entityIds) => {
    await Promise.all([
      pinsLoaded.value ? Promise.resolve() : loadPins(),
      loadFor(entityIds)
    ]);
  };

  /** Pin or unpin one row. `label` is what the record calls itself — a REMS number, a name — so the
      confirmation can say which row moved. */
  const togglePin = async (entityId, label = "") => {
    if (busyId.value) return;
    const existing = pins.value.get(entityId);
    if (!existing && pinLimitReached.value) {
      notify.warning(`You can pin at most ${MAX_PINS_PER_TYPE} records of this type. Unpin one first.`);
      return;
    }
    busyId.value = entityId;
    try {
      const next = new Map(pins.value);
      if (existing) {
        await ufPinApi.remove(existing);
        next.delete(entityId);
        notify.info(`${named(label)} unpinned.`);
      } else {
        const pin = await ufPinApi.create({ entityType, entityId });
        next.set(entityId, pin?.id || null);
        // Says where it went AND that it went there for them alone — the row has just jumped its place
        // on a list everybody else sees unchanged, which is worth one sentence.
        notify.success(`${named(label)} pinned to the top of this page, for you only.`);
      }
      pins.value = next;
    } catch (err) {
      notify.error(getApiErrorMessage(err));
      // The server is the authority on the limit and on what is already pinned, so a failure re-reads
      // rather than guessing which half of the toggle survived.
      await loadPins();
    } finally {
      busyId.value = "";
    }
  };

  /** Tint or clear one row. Confirmed the same way a pin is, and for the same reason. */
  const applyColour = async (entityId, colour, label = "") => {
    if (busyId.value) return;
    busyId.value = entityId;
    try {
      // Only on a write that actually landed — setColour reports its own failure, and a success toast
      // over the top of an error one would be the screen contradicting itself.
      if (await setColour(entityId, colour)) {
        if (colour) notify.success(`${named(label)} coloured, for you only.`);
        else notify.info(`Colour cleared from ${named(label, "this row")}.`);
      }
    } finally {
      busyId.value = "";
    }
  };

  return {
    palette: ROW_COLOUR_PALETTE,
    // pins
    pinnedRowKeys,
    pinCount,
    pinLimitReached,
    isPinned,
    togglePin,
    // colours
    colours,
    colourOf,
    applyColour,
    // shared
    busyId,
    sync
  };
}
