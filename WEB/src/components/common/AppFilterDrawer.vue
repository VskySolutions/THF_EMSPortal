<template>
  <div>
    <!-- Active filter chips (above the table; the Filters trigger lives in AppListHeader). -->
    <div v-if="chips.length" class="row items-center q-gutter-sm q-mb-sm">
      <q-chip
        v-for="chip in chips"
        :key="chip.key"
        removable
        color="teal-1"
        text-color="primary"
        @remove="$emit('remove', chip.key)"
      >
        {{ chip.label }}
      </q-chip>
      <q-btn flat dense no-caps color="grey-7" label="Clear all" @click="$emit('clear')" />
    </div>

    <q-drawer v-model="open" side="right" overlay bordered :width="width" class="app-filter-drawer column no-wrap">
      <!-- Drag handle: resize the filter drawer (double-click resets); width persists until logout. -->
      <div class="app-filter-drawer__resizer" @mousedown="startResize" @dblclick="resetWidth" />

      <div class="row items-center q-pa-md bg-primary text-white">
        <div class="text-h6">Filters</div>
        <q-space />
        <q-btn flat round dense color="white" icon="o_close" @click="open = false" />
      </div>
      <q-separator />
      <q-scroll-area class="col">
        <!-- Consistent, compact vertical spacing between all filter inputs, on every page. -->
        <div class="q-pa-md column q-gutter-sm">
          <slot />
        </div>
      </q-scroll-area>
      <q-separator />
      <div class="row justify-end q-gutter-sm q-pa-md bg-grey-1">
        <q-btn flat no-caps color="grey-8" label="Clear all" @click="$emit('clear')" />
        <q-btn unelevated no-caps color="primary" label="Done" @click="open = false" />
      </div>
    </q-drawer>
  </div>
</template>

<script setup>
import { computed } from "vue";
import { useDrawerResize, viewportWidth } from "composables/useDrawerResize";

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  // [{ key, label }] for each active filter; drives the chips.
  chips: { type: Array, default: () => [] }
});

const emit = defineEmits(["update:modelValue", "remove", "clear"]);

const open = computed({
  get: () => props.modelValue,
  set: (val) => emit("update:modelValue", val)
});

// ---- Resizable width (compact by default; drag the left edge to widen) ----
const { width, startResize, resetWidth } = useDrawerResize({
  storageKey: "appFilterDrawerWidth",
  getDefault: () => 360,
  getMin: () => 300,
  // A phone has no width to spare for the backdrop beside it: below sm the drawer takes almost the whole
  // screen, which is also the only width these inputs are usable at.
  getMax: () => Math.round(viewportWidth() * (viewportWidth() < 600 ? 0.92 : 0.6))
});
</script>

<style scoped>
.app-filter-drawer__resizer {
  position: absolute;
  top: 0;
  left: 0;
  width: 6px;
  height: 100%;
  cursor: ew-resize;
  z-index: 10;
  background: transparent;
  transition: background 0.15s ease;
}
.app-filter-drawer__resizer:hover {
  background: var(--q-primary);
  opacity: 0.4;
}
</style>
