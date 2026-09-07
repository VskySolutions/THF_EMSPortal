<template>
  <app-form-drawer
    ref="drawerRef"
    v-model="open"
    :title="groupId ? 'Edit Permission Group' : 'Create Permission Group'"
    save-label="Save"
    :saving="saving"
    draft-key="permission-group-form"
    :draft="draftView"
    @submit="onSubmit"
    @cancel="resetForm"
    @restore-draft="restoreDraft"
  >
    <q-form ref="formRef" greedy>
      <app-select
        v-if="canChooseTenant" v-model="form.tenantId" :options="tenantOptions" label="Tenant *"
        :loading="loadingTenants" :clearable="false" class="q-mb-md" :readonly="!!groupId"
        :rules="[(v) => !!v || 'Tenant is required']"
      />

      <app-text-field
        v-model="form.name" label="Name *" class="q-mb-md"
        :rules="[(v) => !!v || 'Name is required']"
      />
      <app-rich-text-field v-model="form.description" label="Description" class="q-mb-md" />

      <!-- Capacity limit (WO-119): optional cap on distinct active members; blank = unlimited. -->
      <app-text-field
        v-model="form.capacityLimit" label="Capacity Limit" type="number" class="q-mb-md"
        clearable placeholder="Unlimited"
        hint="Maximum distinct active members allowed. Leave blank for unlimited."
        :rules="[(v) => v == null || v === '' || Number(v) >= 0 || 'Capacity cannot be negative']"
      />

      <!-- Permission keys: grouped by category, live-searchable, with a real-time count badge. -->
      <div class="row items-center q-mb-sm">
        <div class="section-subhead">Permission Keys</div>
        <q-space />
        <q-badge color="primary" class="count-badge" data-test="key-count">{{ selectedKeys.length }} selected</q-badge>
      </div>

      <app-text-field
        v-model="keySearch" label="Filter keys" clearable class="q-mb-sm"
      >
        <template #prepend><q-icon name="o_search" /></template>
      </app-text-field>

      <div v-if="loadingCatalog" class="row flex-center q-pa-md"><q-spinner color="primary" size="28px" /></div>

      <q-list v-else bordered class="rounded-borders">
        <q-expansion-item
          v-for="group in visibleCategories"
          :key="group.category"
          default-opened
          dense
          header-class="pg-group__header"
        >
          <!-- The whole category in one tick. -->
          <template #header>
            <q-item-section side>
              <q-checkbox
                :model-value="groupState(group.keys)"
                :disable="!selectableIn(group.keys).length"
                dense
                @click.stop
                @update:model-value="(value) => toggleGroup(group.keys, value)"
              >
                <q-tooltip>Select every permission in {{ group.category }}</q-tooltip>
              </q-checkbox>
            </q-item-section>
            <q-item-section>
              <q-item-label class="pg-group__title">{{ group.category }}</q-item-label>
              <q-item-label caption>
                {{ countSelectedIn(group.keys) }} / {{ group.keys.length }} selected
              </q-item-label>
            </q-item-section>
          </template>

          <q-list dense class="pg-group__keys">
            <q-item v-for="key in group.keys" :key="key" tag="label" :disable="isDisabled(key)">
              <q-item-section side top>
                <q-checkbox v-model="selectedKeys" :val="key" :disable="isDisabled(key)" dense />
              </q-item-section>
              <q-item-section>
                <q-item-label :class="{ 'text-grey-5': isDisabled(key) }">
                  {{ humanizeKey(key) }}
                  <q-tooltip>{{ key }}</q-tooltip>
                </q-item-label>
              </q-item-section>
              <q-item-section v-if="isDisabled(key)" side>
                <q-icon name="o_lock" color="grey-5" size="16px">
                  <q-tooltip>Not available in your tenant</q-tooltip>
                </q-icon>
              </q-item-section>
            </q-item>
          </q-list>
        </q-expansion-item>
        <q-item v-if="!visibleCategories.length">
          <q-item-section class="text-grey-6">No permission keys match your search.</q-item-section>
        </q-item>
      </q-list>
    </q-form>
  </app-form-drawer>
</template>

<script setup>
import { ref, reactive, computed, watch } from "vue";
import { permissionGroupApi, roleApi, getApiErrorMessage, getApiErrorCode, ApiErrorCodes } from "services/api";
import { useTenantOptions } from "composables/useTenantOptions";
import { useNotify } from "composables/useNotify";
import {
  humanizeKey, groupKeysByCategory, categoryForKey, ELEVATED_KEYS
} from "composables/usePermissionCategories";

import AppFormDrawer from "components/common/AppFormDrawer.vue";
import AppSelect from "components/common/AppSelect.vue";
import AppTextField from "components/common/AppTextField.vue";
import AppRichTextField from "components/common/AppRichTextField.vue";

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  // Optional pre-selected tenant (super admin's chosen list scope).
  tenantId: { type: String, default: null },
  // When set, the drawer edits the existing group; otherwise it creates a new one.
  groupId: { type: String, default: null },
  // Optional template to pre-populate from on open: { name, description, permissionKeys[] }.
  template: { type: Object, default: null }
});
const emit = defineEmits(["update:modelValue", "saved"]);

const notify = useNotify();
const { canChooseTenant, activeTenantId, tenantOptions, loadingTenants, loadTenants } = useTenantOptions();

const open = computed({
  get: () => props.modelValue,
  set: (val) => emit("update:modelValue", val)
});

const blankForm = () => ({ tenantId: null, name: "", description: "", capacityLimit: null });
const form = reactive(blankForm());
const selectedKeys = ref([]);
const formRef = ref(null);
const drawerRef = ref(null);
const saving = ref(false);

// ---- Permission catalogue ----
const catalog = ref([]);
const loadingCatalog = ref(false);
const keySearch = ref("");

const loadCatalog = async () => {
  if (catalog.value.length) return;
  loadingCatalog.value = true;
  try {
    catalog.value = await permissionGroupApi.permissionCatalog() || [];
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loadingCatalog.value = false;
  }
};

// Categorised + filtered catalogue for rendering.
const categorized = computed(() => groupKeysByCategory(catalog.value));
const visibleCategories = computed(() => {
  const needle = (keySearch.value || "").toLowerCase().trim();
  if (!needle) return categorized.value;
  return categorized.value
    .map((g) => ({
      ...g,
      keys: g.keys.filter((k) => k.toLowerCase().includes(needle) || humanizeKey(k).toLowerCase().includes(needle))
    }))
    .filter((g) => g.keys.length);
});

const countSelectedIn = (keys) => keys.filter((k) => selectedKeys.value.includes(k)).length;

// The keys in a category this caller may actually tick — the ceiling greys the rest out, and a
// select-all that reached them would be a way around it.
const selectableIn = (keys) => keys.filter((k) => !isDisabled(k));

// Tri-state for the category checkbox: all of them, none of them, or null for some (which Quasar renders
// indeterminate).
const groupState = (keys) => {
  const selectable = selectableIn(keys);
  if (!selectable.length) return false;
  const chosen = selectable.filter((k) => selectedKeys.value.includes(k)).length;
  if (chosen === 0) return false;
  return chosen === selectable.length ? true : null;
};

const toggleGroup = (keys, next) => {
  const selectable = selectableIn(keys);
  if (next) {
    selectedKeys.value = [...new Set([...selectedKeys.value, ...selectable])];
    return;
  }
  const dropped = new Set(selectable);
  selectedKeys.value = selectedKeys.value.filter((k) => !dropped.has(k));
};

// ---- Tenant ceiling (best-effort, never blocks submit) ----
// Super Admins see no ceiling.
const ceiling = ref(null); // null → no restriction (super admin)

const computeCeiling = async () => {
  if (canChooseTenant.value) { ceiling.value = null; return; }
  try {
    const roles = await roleApi.list({});
    const union = new Set();
    for (const r of roles || []) {
      const full = await roleApi.get(r.id);
      (full?.permissions || []).forEach((k) => union.add(k));
      (full?.effectivePermissions || []).forEach((k) => union.add(k));
    }
    ceiling.value = union.size ? union : new Set(catalog.value.filter((k) => !ELEVATED_KEYS.includes(k)));
  } catch {
    // best-effort fallback
    ceiling.value = new Set(catalog.value.filter((k) => !ELEVATED_KEYS.includes(k)));
  }
};

const isDisabled = (key) => ceiling.value != null && !ceiling.value.has(key);

const resetForm = () => {
  Object.assign(form, blankForm());
  selectedKeys.value = [];
  keySearch.value = "";
};

const restoreDraft = (saved) => {
  Object.assign(form, blankForm(), saved);
  if (Array.isArray(saved?.permissionKeys)) selectedKeys.value = [...saved.permissionKeys];
};

const applyTemplate = (template) => {
  if (!template) return;
  form.name = template.name || "";
  form.description = template.description || "";
  selectedKeys.value = [...(template.permissionKeys || [])];
};

const loadGroup = async (id) => {
  try {
    const g = await permissionGroupApi.get(id);
    form.tenantId = g.tenantId;
    form.name = g.name;
    form.description = g.description || "";
    form.capacityLimit = g.capacityLimit ?? null;
    selectedKeys.value = [...(g.permissionKeys || [])];
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// Prepare the form whenever the drawer opens.
watch(() => props.modelValue, async (isOpen) => {
  if (!isOpen) return;
  resetForm();
  await loadCatalog();
  if (canChooseTenant.value) {
    await loadTenants();
    form.tenantId = props.tenantId || activeTenantId.value;
  }
  await computeCeiling();
  if (props.groupId) {
    await loadGroup(props.groupId);
  } else if (props.template) {
    applyTemplate(props.template);
  }
}, { immediate: true });

// The auto-saved draft snapshot includes the selected keys so they survive a tenant-switch guard.
const draftView = computed(() => ({ ...form, permissionKeys: selectedKeys.value }));

const onSubmit = async ({ clearDraft } = {}) => {
  if (!(await formRef.value?.validate())) return;
  saving.value = true;
  try {
    // Blank capacity → null (unlimited); otherwise coerce to a number.
    const capacityLimit = (form.capacityLimit === "" || form.capacityLimit == null) ? null : Number(form.capacityLimit);
    const payload = {
      name: form.name,
      description: form.description || null,
      capacityLimit,
      permissionKeys: selectedKeys.value
    };
    if (props.groupId) {
      await permissionGroupApi.update(props.groupId, payload);
    } else {
      if (canChooseTenant.value && form.tenantId) payload.tenantId = form.tenantId;
      await permissionGroupApi.create(payload);
    }
    clearDraft?.();
    resetForm();
    notify.success("Permission group saved.");
    emit("saved");
  } catch (err) {
    const code = getApiErrorCode(err);
    if (code === ApiErrorCodes.PermissionCeilingExceeded) {
      notify.error(getApiErrorMessage(err, "One or more keys are outside your tenant's permission ceiling."));
    } else if (code === ApiErrorCodes.DuplicateGroupName) {
      notify.error("A group with that name already exists.");
    } else if (code === ApiErrorCodes.CapacityBelowUsage) {
      // Server message states how many members must be removed before lowering the limit (AC-PG-003.4).
      notify.error(getApiErrorMessage(err, "The capacity limit is below the group's current usage."));
    } else if (code === ApiErrorCodes.CapacityLimitReached) {
      notify.error(getApiErrorMessage(err, "The group is at its capacity limit."));
    } else {
      notify.error(getApiErrorMessage(err));
    }
  } finally {
    saving.value = false;
  }
};

// Exposed for the parent list/detail (and unit tests).
defineExpose({ form, selectedKeys, visibleCategories, categoryForKey });
</script>

<style scoped>
.section-subhead {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--q-primary);
}
.count-badge {
  font-size: 12px;
}

/* A category row is a heading, not another option — and since it grew a checkbox of its own it needed
   telling apart from the keys under it by more than colour. A tinted band with a rule beneath it, so a
   long list reads as sections rather than as one unbroken run of checkboxes. */
.pg-group__header {
  background-color: #e0f2f1;
  border-bottom: 1px solid rgba(0, 0, 0, 0.08);
}
.pg-group__title {
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--q-primary);
}
/* The keys sit in from their heading, so the grouping survives a scroll past the band. */
.pg-group__keys :deep(.q-item) {
  padding-left: 24px;
}
</style>
