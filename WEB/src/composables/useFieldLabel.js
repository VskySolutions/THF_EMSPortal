import { computed, unref } from "vue";

// Parses a field label that may encode "mandatory" as a trailing "*" (the app-wide convention.
export function useFieldLabel (label, required) {
  const raw = computed(() => unref(label) || "");
  const text = computed(() => raw.value.replace(/\s*\*\s*$/, "").trim());
  const isRequired = computed(() => !!unref(required) || /\*\s*$/.test(raw.value));
  return { text, isRequired };
}
