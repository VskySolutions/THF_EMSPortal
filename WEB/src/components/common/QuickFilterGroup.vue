<template>
  <!-- One group of quick filters: a label, and a button per value with how many rows it would list. -->
  <div class="qf-group">
    <!-- The star every required field carries: a group that always has one side on is a choice the
         reader is making whether they touch it or not. -->
    <span v-if="label" class="qf-group__label">
      {{ label }}<span v-if="!clearable" class="qf-group__star" aria-hidden="true">*</span>
    </span>
    <q-btn-toggle
      :model-value="modelValue ?? null"
      no-caps unelevated dense :clearable="clearable"
      toggle-color="primary" color="white" text-color="grey-9"
      class="qf-group__toggle"
      :options="toggleOptions"
      @update:model-value="$emit('update:modelValue', $event)"
    >
      <template v-for="opt in options" :key="opt.value" #[slotName(opt)]>
        <span class="qf-group__btn">
          {{ opt.label }}
          <q-badge v-if="countOf(opt) !== null" :label="countOf(opt)" rounded class="qf-group__count" />
          <!-- The way out, on the chosen button only. The button already clears itself when clicked again,
               so the cross is a cue rather than a second control. -->
          <q-icon
            v-if="clearable && isOn(opt)" name="o_close" size="18px" class="qf-group__clear" aria-hidden="true"
            @click.stop="$emit('update:modelValue', null)"
          />
        </span>
      </template>
    </q-btn-toggle>
  </div>
</template>

<script setup>
// A row of counted shortcuts over a list's own filters. The value written back is the filter's own, so a
// click here and a pick in the filter drawer are the same thing — and the drawer's chip removes both.
import { computed } from "vue";

const props = defineProps({
  modelValue: { type: String, default: null },
  label: { type: String, default: "" },
  // [{ value, label }] — the filter's own option list, or the subset worth a button.
  options: { type: Array, default: () => [] },
  // value → how many rows the button would list. Absent until the first count comes back.
  counts: { type: Object, default: null },
  // Clicking the active button again clears the filter. Off for a pair that always has one side on.
  clearable: { type: Boolean, default: true }
});
defineEmits(["update:modelValue"]);

const slotName = (opt) => `qf-${String(opt.value).replace(/[^A-Za-z0-9_-]/g, "_")}`;
const toggleOptions = computed(() => props.options.map((opt) => ({ value: opt.value, slot: slotName(opt) })));
const countOf = (opt) => {
  const n = props.counts?.[opt.value];
  return Number.isFinite(n) ? n : null;
};
const isOn = (opt) => props.modelValue != null && opt.value === props.modelValue;
</script>

<style scoped>
.qf-group {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.qf-group__label {
  font-size: 12px;
  font-weight: 700;
  color: var(--ink-700);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
/* White, bordered buttons so every option — the one not chosen included — reads as a choice on the bar
   rather than fading into it; the chosen one is filled in the primary colour and set a weight heavier. */
.qf-group__toggle {
  border: 1px solid var(--line);
  border-radius: 8px;
  overflow: hidden;
}
.qf-group__toggle :deep(.q-btn) {
  font-weight: 500;
  padding: 2px 12px;
}
.qf-group__toggle :deep(.q-btn + .q-btn) {
  border-left: 1px solid var(--line);
}
.qf-group__toggle :deep(.q-btn.bg-primary) {
  font-weight: 600;
}
/* The same star, in the same red, that AppFieldLabel puts on a required field. */
.qf-group__star {
  margin-left: 3px;
  color: #e53935;
  font-size: 16px;
  font-weight: 700;
  line-height: 0;
}
.qf-group__btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
/* The count reads as a tally on the button, in the button's own text colour, not as a second button. */
.qf-group__count {
  background: rgba(0, 0, 0, 0.12) !important;
  color: inherit !important;
  font-size: 11px;
  padding: 1px 6px;
}
/* On the chosen button the tally flips: white on the primary fill, lettered in the primary colour. */
.qf-group__toggle :deep(.q-btn.bg-primary) .qf-group__count {
  background: #fff !important;
  color: var(--q-primary) !important;
}
.qf-group__clear {
  opacity: 0.8;
  cursor: pointer;
}
.qf-group__clear:hover {
  opacity: 1;
}
</style>
