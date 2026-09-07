import { ref, reactive, computed, onBeforeUnmount } from "vue";
import { getApiErrorMessage } from "services/api";

/** Debounced auto-save for a screen made of several independently-written parts. */
export function useAutoSave (savers, options = {}) {
  const delay = options.delay ?? 1200;
  const isEnabled = options.enabled || (() => true);
  const keys = Object.keys(savers);

  const dirty = reactive(Object.fromEntries(keys.map((key) => [key, false])));
  // idle | pending | saving | saved | blocked | error
  const state = ref("idle");
  const message = ref("");

  let timer = null;
  let inflight = null;
  let suspended = 0;

  const pending = computed(() => keys.some((key) => dirty[key]));

  const writePendingParts = async () => {
    state.value = "saving";
    let failure = null;
    let wrote = false;

    for (const key of keys) {
      if (!dirty[key]) continue;
      dirty[key] = false;
      try {
        const reason = await savers[key]();
        if (typeof reason === "string" && reason) {
          dirty[key] = true;
          failure = failure || { kind: "blocked", message: reason };
        } else {
          wrote = true;
        }
      } catch (err) {
        dirty[key] = true;
        const kind = err?.response ? "error" : "blocked";
        // A server refusal outranks a not-fillable-yet part: it is the one the user has to act on.
        if (!failure || (kind === "error" && failure.kind !== "error")) {
          message.value = kind === "error" ? getApiErrorMessage(err) : (err?.message || "Not saved.");
          failure = { kind, message: message.value };
        }
      }
    }

    // A part re-dirtied WHILE this pass was writing has a timer of its own already running, so the pass
    // ends on "still something to save" rather than claiming everything landed.
    state.value = failure ? failure.kind : (pending.value ? "pending" : "saved");
    message.value = failure ? failure.message : "";

    // Whatever the screen needs to re-read once its own writes have landed. A refresh that fails is not
    // a save that failed, so it never touches the state above.
    if (wrote && options.onSaved) {
      try {
        await options.onSaved();
      } catch {
        // Nothing to say: the write itself succeeded and is already reported.
      }
    }
  };

  const flush = async () => {
    clearTimeout(timer);
    // Never two passes at once. A flush arriving mid-write waits for it, then writes whatever the user
    // changed in the meantime.
    if (inflight) await inflight;
    if (!pending.value || suspended > 0 || !isEnabled()) return;
    const pass = writePendingParts();
    inflight = pass;
    try {
      await pass;
    } finally {
      // Only if a later flush has not already taken the slot, or that pass would stop being waited on.
      if (inflight === pass) inflight = null;
    }
  };

  const mark = (key) => {
    if (suspended > 0 || !isEnabled() || !(key in dirty)) return;
    dirty[key] = true;
    if (state.value !== "saving") {
      state.value = "pending";
      message.value = "";
    }
    clearTimeout(timer);
    timer = setTimeout(() => { void flush(); }, delay);
  };

  // Around a reload: seeding the forms writes to the very state the watchers listen to, and that is the
  // screen catching up with the server rather than the user typing.
  const suspend = () => {
    suspended += 1;
    clearTimeout(timer);
  };
  const resume = () => { suspended = Math.max(0, suspended - 1); };

  const reset = () => {
    clearTimeout(timer);
    keys.forEach((key) => { dirty[key] = false; });
    state.value = "idle";
    message.value = "";
  };

  onBeforeUnmount(() => clearTimeout(timer));

  return { state, message, pending, mark, flush, suspend, resume, reset };
}
