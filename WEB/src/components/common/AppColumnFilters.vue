<template>
  <div class="column app-column-filters">
    <template v-for="col in columns" :key="col.name">
      <app-select
        v-if="col.filterOptions"
        v-model="filters[col.name]"
        :options="col.filterOptions"
        :label="col.label"
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
// Renders one filter control per filterable column (select when the column declares filterOptions,
// otherwise a text "contains" box).
import AppSelect from "components/common/AppSelect.vue";
import AppTextField from "components/common/AppTextField.vue";

// Shared via v-model; nested writes go back to the caller's reactive filters object.
const filters = defineModel({ type: Object, required: true });

defineProps({
  columns: { type: Array, default: () => [] }
});
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
