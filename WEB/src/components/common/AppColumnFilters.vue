<template>
  <div class="column app-column-filters">
    <template v-for="col in columns" :key="col.name">
      <!-- A date column filters as a range: two pickers, either end optional, each bounding the other so
           the calendars offer only days the range can hold. -->
      <template v-if="col.filterType === 'dateRange'">
        <app-date-field
          :model-value="rangeOf(col).from" :label="`${col.label} From`" :dense="false"
          :max-date="rangeOf(col).to"
          @update:model-value="setBound(col, 'from', $event)"
        />
        <app-date-field
          :model-value="rangeOf(col).to" :label="`${col.label} To`" :dense="false"
          :min-date="rangeOf(col).from"
          @update:model-value="setBound(col, 'to', $event)"
        />
        <div v-if="invalidRange(col)" class="text-caption text-negative">
          “{{ col.label }} From” is after “{{ col.label }} To” — nothing can match both.
        </div>
      </template>
      <!-- Any-of when the column says so: the selection is held as an array. -->
      <app-select
        v-else-if="col.filterOptions"
        v-model="filters[col.name]"
        :options="col.filterOptions"
        :label="col.label"
        :multiple="!!col.filterMultiple"
      />
      <app-text-field
        v-else
        v-model="filters[col.name]"
        :label="col.label"
        clearable
        :dense="false"
      />
    </template>
  </div>
</template>

<script setup>
// Renders one filter control per filterable column: a select when the column declares filterOptions (a
// multi-select when it also says filterMultiple), a From/To pair of dates for filterType "dateRange",
// otherwise a text "contains" box.
import AppSelect from "components/common/AppSelect.vue";
import AppTextField from "components/common/AppTextField.vue";
import AppDateField from "components/common/AppDateField.vue";

// Shared via v-model; nested writes go back to the caller's reactive filters object.
const filters = defineModel({ type: Object, required: true });

defineProps({
  columns: { type: Array, default: () => [] }
});

const rangeOf = (col) => filters.value[col.name] || {};
// Either end may be cleared; with both empty the column holds no filter at all.
const setBound = (col, edge, value) => {
  const next = { ...rangeOf(col), [edge]: value || "" };
  filters.value[col.name] = next.from || next.to ? next : null;
};
const invalidRange = (col) => {
  const { from, to } = rangeOf(col);
  return !!from && !!to && from > to;
};
</script>

<style scoped>
/* Spaced with flex `gap`, not q-gutter-*. This list always renders inside AppFilterDrawer, whose slot
   container is itself a q-gutter-sm: a nested gutter class has its own negative margin overridden by
   the parent's `> *` rule (same specificity, declared later), so it never cancels — which left every
   control here sitting 8px right of the drawer's other filters. `gap` spaces without any margin. */
.app-column-filters {
  gap: 8px;
}
</style>
