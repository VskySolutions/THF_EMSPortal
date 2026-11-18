import { ref } from "vue";
import { optionSetApi } from "services/api";

// The Person.NameSuffix option list, shared by every suffix box. A signed-in screen asks for it once; the
// anonymous client form is handed it with its load (setNameSuffixes), so a box there never asks.

const suffixes = ref([]);
let handed = false;
let loading = null;

const load = async () => {
  try {
    const items = await optionSetApi.resolve({ key: "Person.NameSuffix" });
    suffixes.value = (items || []).map((i) => ({ value: i.value, label: i.label, caption: i.description || "" }));
  } catch {
    // Suggestions only: the box still takes a typed suffix. Asked again by the next box.
    loading = null;
  }
};

/** Hands the list over instead of loading it: [{ value, label, description }]. */
export function setNameSuffixes (items) {
  handed = true;
  suffixes.value = (items || []).map((i) => ({ value: i.value, label: i.label, caption: i.description || "" }));
}

/** The suggestions, as a ref of [{ value, label, caption }]. */
export function useNameSuffixes () {
  if (!handed) loading ??= load();
  return suffixes;
}

if (typeof window !== "undefined") {
  // The list is the tenant's own copy where it has one.
  window.addEventListener("tenant-switched", () => { if (!handed) loading = load(); });
}
