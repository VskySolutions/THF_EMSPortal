import { reactive, computed, unref } from "vue";

// Every phone field on screen reports here whether its number fits its country, so a form can refuse to
// save without knowing how many phone fields it holds or where they are.
const fields = reactive(new Map());
let nextId = 0;

/** Called by AppPhoneInput. `entry` is reactive: { el, valid, active, show }. Returns the way out. */
export const registerPhoneField = (entry) => {
  const id = ++nextId;
  fields.set(id, entry);
  return () => fields.delete(id);
};

export const PHONE_FIELDS_ISSUE = "A phone number is incomplete or not valid for its country — see the highlighted field.";

/**
 * The phone fields on screen, or only those inside `root` (an element, a component, or a ref of either).
 * A field that is read-only or disabled is nobody's to correct and is not counted.
 */
export function usePhoneFields (root = null) {
  const scoped = () => {
    const target = unref(root);
    const el = target?.$el || target;
    return [...fields.values()].filter((f) => f.active && f.el && (!el?.contains || el.contains(f.el)));
  };

  const invalidPhones = computed(() => scoped().filter((f) => !f.valid));
  const phonesValid = computed(() => invalidPhones.value.length === 0);
  /** For a form's list of what is still to fix: one line, or none. */
  const phoneIssues = computed(() => (phonesValid.value ? [] : [PHONE_FIELDS_ISSUE]));

  /** At save time: shows each field's message, brings the first into view, and answers whether to go on. */
  const validatePhones = () => {
    const bad = invalidPhones.value;
    if (!bad.length) return true;
    bad.forEach((f) => f.show());
    try {
      bad[0].el.scrollIntoView({ behavior: "smooth", block: "center" });
    } catch { /* noop */ }
    return false;
  };

  return { invalidPhones, phonesValid, phoneIssues, validatePhones };
}
