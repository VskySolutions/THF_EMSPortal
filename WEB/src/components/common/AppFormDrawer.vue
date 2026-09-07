<template>
  <q-drawer
    v-model="open"
    side="right"
    overlay
    bordered
    :width="currentWidth"
    class="app-form-drawer column no-wrap"
  >
    <!-- Drag handle: resize the drawer; the chosen width persists until logout. -->
    <div class="app-form-drawer__resizer" @mousedown="startResize" @dblclick="resetWidth" />

    <!-- Fixed header -->
    <div class="row items-center q-pa-md bg-primary text-white">
      <div class="text-h6">{{ title }}</div>
      <q-space />
      <q-btn flat round dense color="white" icon="o_close" @click="onCancel" />
    </div>
    <q-separator />

    <!-- Scrollable body -->
    <q-scroll-area class="col">
      <div class="q-pa-md">
        <slot />
      </div>
    </q-scroll-area>

    <!-- Fixed footer -->
    <q-separator />
    <div class="row justify-end items-center q-gutter-sm q-pa-md bg-grey-1">
      <q-btn flat no-caps color="grey-8" label="Cancel" @click="onCancel" />
      <!-- Optional extra actions (e.g. "Save as Draft") sit beside the primary action, same row. -->
      <slot name="footer-actions" />
      <q-btn
        v-if="!hideSave"
        unelevated
        no-caps
        color="primary"
        :label="saveLabel"
        :loading="saving"
        :disable="saving"
        @click="onSubmit"
      />
    </div>
  </q-drawer>
</template>

<script setup>
import { computed, watch } from "vue";
import { useTenantStore } from "stores/tenant";
import { usePreferences } from "composables/usePreferences";
import { useDrawerResize, viewportWidth } from "composables/useDrawerResize";

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  title: { type: String, default: "" },
  saveLabel: { type: String, default: "Save" },
  saving: { type: Boolean, default: false },
  // Drops the primary action, leaving Cancel. For a drawer that only shows a record the viewer has no
  // right to change — a Save they cannot use reads as a bug, not as a boundary.
  hideSave: { type: Boolean, default: false },
  // When set, the form's draft is auto-persisted under this key.
  draftKey: { type: String, default: "" },
  draft: { type: Object, default: null }
});

const emit = defineEmits(["update:modelValue", "submit", "cancel", "restore-draft"]);

const tenantStore = useTenantStore();
const prefs = props.draftKey ? usePreferences(props.draftKey) : null;

// ---- Resizable width (viewport-relative: 50% default, 30% min) ----
const { width: currentWidth, startResize, resetWidth } = useDrawerResize({
  storageKey: "appDrawerWidth",
  getDefault: () => Math.round(viewportWidth() * 0.50),
  getMin: () => Math.round(viewportWidth() * 0.30),
  getMax: () => Math.round(viewportWidth() * 0.95)
});

// Any user-initiated close (Cancel, X, backdrop click, ESC) routes through this setter, so the draft is
// always cleared and `cancel` always fires — letting parents reset their form state.
const open = computed({
  get: () => props.modelValue,
  set: (val) => {
    emit("update:modelValue", val);
    if (!val) handleClose();
  }
});

const handleClose = () => {
  clearDraft();
  emit("cancel");
};

let draftTimer = null;

// Auto-save the draft (debounced 2s) and flag unsaved state for the tenant-switch guard.
watch(() => props.draft, (value) => {
  if (!prefs || !props.modelValue || value == null) {
    return;
  }
  tenantStore.setUnsavedForm(true);
  clearTimeout(draftTimer);
  draftTimer = setTimeout(() => prefs.set("formDraft", value), 2000);
}, { deep: true });

// Restore any saved draft when the drawer opens.
watch(() => props.modelValue, (isOpen) => {
  if (isOpen && prefs) {
    const saved = prefs.get("formDraft", null);
    if (saved) {
      emit("restore-draft", saved);
    }
  } else if (!isOpen) {
    tenantStore.setUnsavedForm(false);
  }
});

const clearDraft = () => {
  clearTimeout(draftTimer);
  if (prefs) {
    prefs.remove("formDraft");
  }
  tenantStore.setUnsavedForm(false);
};

const onSubmit = () => {
  if (props.saving) {
    return; // double-click prevention
  }
  emit("submit", { clearDraft });
};

const onCancel = () => {
  open.value = false; // setter runs handleClose() → clearDraft + emit("cancel")
};

defineExpose({ clearDraft });
</script>

<style scoped>
.app-form-drawer {
  height: 100%;
}
.app-form-drawer__resizer {
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
.app-form-drawer__resizer:hover {
  background: var(--q-primary);
  opacity: 0.4;
}
</style>
