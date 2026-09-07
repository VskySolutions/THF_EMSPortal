<template>
  <div class="dg">
    <div v-for="row in visibleRows" :key="row.label" class="dg__item" :class="{ 'dg__item--wide': row.wide }">
      <div class="dg__label">{{ row.label }}</div>
      <!-- Rich text (the partner's message) arrives as markup and has to render as such. -->
      <!-- eslint-disable-next-line vue/no-v-html -->
      <div v-if="row.html" class="dg__value dg__value--rich" v-html="renderRichText(row.value)" />
      <!-- A row that names somebody. -->
      <div v-else-if="row.suffix !== undefined" class="dg__value">
        <app-name-with-suffix :name="String(row.value ?? '')" :suffix="row.suffix" />
      </div>
      <div v-else class="dg__value">{{ display(row.value) }}</div>
    </div>
  </div>
</template>

<script setup>
// Read-only presentation for the REMS form's View mode: label above value, not a disabled input.
import { computed } from "vue";
import { renderRichText } from "utils/richText";
import AppNameWithSuffix from "components/common/AppNameWithSuffix.vue";

const props = defineProps({
  // [{ label, value, wide?, html?, hideWhenEmpty?, suffix? }] `suffix` marks the row as a NAME: present
  // (even as "") it renders through AppNameWithSuffix, which puts the particle after the name and in bold.
  rows: { type: Array, default: () => [] }
});

// A field nobody filled in still matters on a record — "no mobile number" is information — so an empty
// value shows as a dash rather than vanishing.
const visibleRows = computed(() =>
  props.rows.filter((r) => !(r.hideWhenEmpty && !hasValue(r.value))));

const hasValue = (v) => !(v === null || v === undefined || v === "" || (Array.isArray(v) && !v.length));

const display = (v) => {
  if (!hasValue(v)) return "—";
  if (Array.isArray(v)) return v.join(", ");
  if (typeof v === "boolean") return v ? "Yes" : "No";
  return v;
};
</script>

<style scoped>
.dg {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 16px 28px;
}
.dg__item--wide {
  grid-column: 1 / -1;
}
.dg__label {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--ink-500);
  margin-bottom: 3px;
}
.dg__value {
  font-size: 14.5px;
  color: var(--ink-900);
  word-break: break-word;
}
.dg__value--rich :deep(p) {
  margin: 0 0 6px;
}
.dg__value--rich :deep(p:last-child) {
  margin-bottom: 0;
}
</style>
