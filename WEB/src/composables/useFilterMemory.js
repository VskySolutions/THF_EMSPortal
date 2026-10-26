import { watch } from "vue";
import { LocalStorage } from "quasar";

// A list's quick-filter picks, kept in LocalStorage so leaving the page and coming back finds them still
// on — and wiped with the rest of the store by auth.clearSession() at logout. `bindings` is name → ref;
// toRef a property of the filters object for a column filter. What was saved is written back during setup,
// so the first load already carries it and no reload watcher fires for the restore. `defaults` (name →
// value) is what a filter starts on for a reader who has never touched the list's shortcuts.
export function useFilterMemory (pageKey, bindings, defaults = {}) {
  const storageKey = `filters:${pageKey}`;
  const names = Object.keys(bindings);

  // Empty means unset: null, "", or an empty selection.
  const isSet = (v) => v !== null && v !== undefined && v !== "" && !(Array.isArray(v) && v.length === 0);

  const saved = LocalStorage.getItem(storageKey);
  const remembered = saved && typeof saved === "object" ? saved : null;
  names.forEach((name) => {
    if (remembered && Object.prototype.hasOwnProperty.call(remembered, name)) {
      // Remembered as cleared counts too: a null leaves the filter off rather than back on its default.
      if (isSet(remembered[name])) bindings[name].value = remembered[name];
    } else if (isSet(defaults[name])) {
      bindings[name].value = defaults[name];
    }
  });

  // Every binding is written on each change, the cleared ones as null, so a filter the reader switched off
  // stays off and only one they have never touched falls back to its default.
  watch(names.map((name) => bindings[name]), () => {
    const next = {};
    names.forEach((name) => { next[name] = isSet(bindings[name].value) ? bindings[name].value : null; });
    LocalStorage.set(storageKey, next);
  }, { deep: true });
}
