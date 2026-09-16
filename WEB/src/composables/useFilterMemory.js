import { watch } from "vue";
import { LocalStorage } from "quasar";

// A list's quick-filter picks, kept in LocalStorage so leaving the page and coming back finds them still
// on — and wiped with the rest of the store by auth.clearSession() at logout. `bindings` is name → ref;
// toRef a property of the filters object for a column filter. What was saved is written back during setup,
// so the first load already carries it and no reload watcher fires for the restore.
export function useFilterMemory (pageKey, bindings) {
  const storageKey = `filters:${pageKey}`;
  const names = Object.keys(bindings);

  const saved = LocalStorage.getItem(storageKey);
  const remembered = saved && typeof saved === "object" ? saved : {};
  names.forEach((name) => {
    if (Object.prototype.hasOwnProperty.call(remembered, name)) bindings[name].value = remembered[name];
  });

  // Empty means unset: null, "", or an empty selection — nothing worth remembering.
  const isSet = (v) => v !== null && v !== undefined && v !== "" && !(Array.isArray(v) && v.length === 0);

  watch(names.map((name) => bindings[name]), () => {
    const next = {};
    names.forEach((name) => { if (isSet(bindings[name].value)) next[name] = bindings[name].value; });
    if (Object.keys(next).length) LocalStorage.set(storageKey, next);
    else LocalStorage.remove(storageKey);
  }, { deep: true });
}
