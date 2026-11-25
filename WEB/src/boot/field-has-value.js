import { boot } from "quasar/wrappers";

// A field failing its rules is red while it is EMPTY (the answer is missing) and the warning colour once
// it holds a value (custom.scss). Quasar has no class for "has a value", so its field components are
// given one here from their own model: a bare q-input gets it as much as an App* field does.
const FIELDS = new Set(["QInput", "QSelect", "QFile", "QField"]);

const filled = (value) => {
  if (Array.isArray(value)) return value.length > 0;
  if (typeof value === "string") return value.trim() !== "";
  return value !== null && value !== undefined;
};

function mark (vm) {
  if (!FIELDS.has(vm.$options.name)) return;
  vm.$el?.classList?.toggle("app-field--has-value", filled(vm.modelValue));
}

export default boot(({ app }) => {
  // Vue rewrites the root's class on every render, so the mark is put back after each one.
  app.mixin({
    mounted () { mark(this); },
    updated () { mark(this); }
  });
});
