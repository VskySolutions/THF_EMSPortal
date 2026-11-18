<template>
  <q-page padding>
    <app-detail-header
      :items="[
        { label: 'Home', icon: 'o_home', to: '/' },
        { label: 'Tenant Settings' },
        { label: 'Option Sets', to: { name: 'option_sets' } },
        { label: set ? set.name : 'Option List' }
      ]"
    />

    <q-inner-loading :showing="loading && !set" />

    <template v-if="set">
      <q-banner v-if="!set.isEditable" dense rounded class="bg-teal-1 text-primary q-mb-md">
        <template #avatar><q-icon name="o_lock" color="primary" /></template>
        This is a standard platform list and is read-only. Create your own list to customise values.
      </q-banner>

      <!-- Settings: one labelled row. -->
      <q-card flat bordered class="q-px-md q-py-sm q-mb-md">
        <div class="row items-start q-col-gutter-x-lg q-col-gutter-y-sm">
          <div class="col-12 col-sm-6 col-md-3">
            <app-field-label label="List Name" />
            <div class="text-weight-medium os-value">{{ set.name }}</div>
          </div>
          <div class="col-12 col-sm-6 col-md-3">
            <app-field-label label="Key" info="The name the application reads this list by. It cannot be changed." />
            <div class="os-value os-key">{{ set.key }}</div>
          </div>
          <div class="col-6 col-sm-4 col-md-2">
            <app-field-label label="Depends On" />
            <div class="os-value">{{ parentName || "—" }}</div>
          </div>
          <div class="col-6 col-sm-2 col-md-1">
            <app-field-label label="Type" />
            <div class="os-value">
              <q-badge :color="set.isSystem ? 'grey-6' : 'primary'" :label="set.isSystem ? 'System' : 'Custom'">
                <q-tooltip v-if="set.isSystem">The application reads this list, so it cannot be deleted. Its values can be edited.</q-tooltip>
              </q-badge>
            </div>
          </div>
          <div class="col-8 col-sm-4 col-md-2">
            <app-select
              v-model="sortMode"
              :options="sortModeOptions"
              label="Item Order"
              :clearable="false"
              :readonly="!set.isEditable"
              @update:model-value="saveSettings"
            />
          </div>
          <div class="col-4 col-sm-2 col-md-1">
            <app-field-label label="Status" />
            <q-toggle
              v-model="isActive" dense :label="isActive ? 'Active' : 'Inactive'" :disable="!set.isEditable"
              class="os-value" @update:model-value="saveSettings"
            />
          </div>
        </div>
      </q-card>

      <!-- Values: every column is in the Columns menu, the important few show by default. -->
      <app-data-table
        class="q-mb-md"
        page-key="option_set_values"
        row-key="id"
        title="Values"
        :rows="shownItems"
        :columns="columns"
        :loading="loading"
        client-sort
        default-sort-by=""
        :default-descending="false"
        @update:pagination="sortedBy = $event.sortBy || ''"
        @refresh="reloadItems"
      >
        <template #actions>
          <q-input v-model="search" dense outlined clearable debounce="200" placeholder="Search values" class="os-search">
            <template #prepend><q-icon name="o_search" /></template>
          </q-input>
          <q-btn v-if="set.isEditable" unelevated no-caps color="primary" icon="o_add" label="Add value" @click="openCreateItem" />
        </template>
        <template #body-cell-label="cell">
          <q-td :props="cell">
            <!-- The value with its configured display colours when set. -->
            <q-chip
              v-if="cell.row.backgroundColor || cell.row.textColor"
              dense
              :style="{ backgroundColor: cell.row.backgroundColor || '#e0e0e0', color: cell.row.textColor || '#212121' }"
              :label="cell.row.label"
            />
            <span v-else>{{ cell.row.label }}</span>
          </q-td>
        </template>
        <template #body-cell-maconomyNumber="cell">
          <q-td :props="cell">
            <!-- Plain text until clicked; then a box that saves on Enter or on leaving it, and is text again. -->
            <q-input
              v-if="editingMaconomyId === cell.row.id"
              :model-value="maconomyDraft(cell.row)"
              dense outlined autofocus maxlength="64" class="os-inline"
              :loading="savingMaconomyId === cell.row.id"
              :aria-label="`Maconomy number for ${cell.row.label}`"
              @update:model-value="maconomyDrafts[cell.row.id] = $event ?? ''"
              @blur="saveMaconomyNumber(cell.row)"
              @keyup.enter="$event.target.blur()"
              @keyup.esc="cancelMaconomyEdit(cell.row)"
            />
            <div
              v-else-if="set.isEditable" class="os-inline-text" tabindex="0" role="button"
              :aria-label="`Edit Maconomy number for ${cell.row.label}`"
              @click="editingMaconomyId = cell.row.id" @keyup.enter="editingMaconomyId = cell.row.id"
            >
              <span v-if="cell.row.maconomyNumber">{{ cell.row.maconomyNumber }}</span>
              <span v-else class="text-grey-5">—</span>
              <q-icon name="o_edit" size="14px" class="os-inline-text__icon" />
              <q-tooltip :delay="400">Click to edit</q-tooltip>
            </div>
            <span v-else>{{ cell.row.maconomyNumber }}</span>
          </q-td>
        </template>
        <template #body-cell-isActive="cell">
          <q-td :props="cell">
            <q-badge :color="cell.row.isActive ? 'positive' : 'grey-5'" :label="cell.row.isActive ? 'Active' : 'Inactive'" />
          </q-td>
        </template>
        <template #body-cell-isDefault="cell">
          <q-td :props="cell">
            <q-badge v-if="cell.row.isDefault" color="teal" label="Default" />
          </q-td>
        </template>
        <template #body-cell-isSystem="cell">
          <q-td :props="cell">
            <q-badge v-if="cell.row.isSystem" color="grey-6" label="System">
              <q-tooltip>The application uses this value, so it cannot be deleted or re-coded.</q-tooltip>
            </q-badge>
          </q-td>
        </template>
        <template #body-cell-colours="cell">
          <q-td :props="cell">
            <span v-if="cell.row.backgroundColor" class="os-swatch" :style="{ backgroundColor: cell.row.backgroundColor }" />
            {{ [cell.row.backgroundColor, cell.row.textColor].filter(Boolean).join(" / ") }}
          </q-td>
        </template>
        <template #body-cell-icon="cell">
          <q-td :props="cell">
            <q-icon v-if="cell.row.icon" :name="cell.row.icon" size="18px" class="q-mr-xs" />{{ cell.row.icon }}
          </q-td>
        </template>
        <template #body-cell-actions="cell">
          <q-td :props="cell">
            <!-- Custom order only, and only while the table shows that order. -->
            <template v-if="canMove">
              <q-btn flat round dense color="grey-7" icon="o_arrow_upward" :disable="indexOf(cell.row) === 0" @click="move(cell.row, -1)">
                <q-tooltip>Move up</q-tooltip>
              </q-btn>
              <q-btn flat round dense color="grey-7" icon="o_arrow_downward" :disable="indexOf(cell.row) === items.length - 1" @click="move(cell.row, 1)">
                <q-tooltip>Move down</q-tooltip>
              </q-btn>
            </template>
            <q-btn flat round dense color="primary" icon="o_edit" @click="openEditItem(cell.row)">
              <q-tooltip>Edit</q-tooltip>
            </q-btn>
            <q-btn flat round dense color="negative" icon="o_delete" @click="removeItem(cell.row)">
              <q-tooltip>Delete</q-tooltip>
            </q-btn>
          </q-td>
        </template>
      </app-data-table>

      <app-record-audit :audit="set.audit" />

      <option-item-dialog
        v-model="itemDialogOpen"
        :set-id="set.id"
        :item="editingItem"
        :parent-options="parentOptions"
        :parent-name="parentName"
        :set-is-closed="!!set.isClosed"
        @saved="reloadItems"
      />
    </template>
  </q-page>
</template>

<script setup>
import { ref, computed, onMounted } from "vue";
import { useRoute } from "vue-router";
import { optionSetApi, getApiErrorMessage, OptionItemSortMode } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import AppDetailHeader from "components/common/AppDetailHeader.vue";
import AppSelect from "components/common/AppSelect.vue";
import AppFieldLabel from "components/common/AppFieldLabel.vue";
import AppRecordAudit from "components/common/AppRecordAudit.vue";
import AppDataTable from "components/common/AppDataTable.vue";
import OptionItemDialog from "modules/option-set/components/OptionItemDialog.vue";

const route = useRoute();
const notify = useNotify();
const { confirm } = useConfirm();

const setId = route.params.id;

const set = ref(null);
const items = ref([]);
const loading = ref(false);

const sortMode = ref(OptionItemSortMode.AlphabeticalAsc);
const isActive = ref(true);

const parentOptions = ref([]);
const parentLabelMap = ref({});
const parentName = ref("");
const parentLabelsOf = (item) =>
  (item.parentItemIds || []).map((id) => parentLabelMap.value[id]).filter(Boolean).join(", ");

const sortModeOptions = [
  { label: "Alphabetical (A → Z)", value: OptionItemSortMode.AlphabeticalAsc },
  { label: "Alphabetical (Z → A)", value: OptionItemSortMode.AlphabeticalDesc },
  { label: "Custom (move up / down)", value: OptionItemSortMode.Custom }
];

const hydrate = (data) => {
  set.value = data;
  items.value = data.items || [];
  sortMode.value = data.itemSortMode;
  isActive.value = data.isActive;
};

const load = async () => {
  loading.value = true;
  try {
    const data = await optionSetApi.get(setId);
    hydrate(data);
    await loadParent();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loading.value = false;
  }
};

// Re-fetch just the items (after add/edit/delete) while keeping the loaded settings.
const reloadItems = async () => {
  try {
    const data = await optionSetApi.get(setId);
    hydrate(data);
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// For a dependency list, load the parent set so we can show + pick parent items.
const loadParent = async () => {
  if (!set.value?.parentSetId) {
    parentOptions.value = [];
    parentLabelMap.value = {};
    parentName.value = "";
    return;
  }
  try {
    const parent = await optionSetApi.get(set.value.parentSetId);
    const parentItems = parent.items || [];
    parentName.value = parent.name;
    parentOptions.value = parentItems.map((i) => ({ label: i.label, value: i.id }));
    parentLabelMap.value = Object.fromEntries(parentItems.map((i) => [i.id, i.label]));
  } catch {
    parentOptions.value = [];
    parentLabelMap.value = {};
  }
};

const saveSettings = async () => {
  try {
    const updated = await optionSetApi.update(setId, {
      name: set.value.name,
      itemSortMode: sortMode.value,
      isActive: isActive.value,
      // Sent back as it is: leaving it out would clear the dependency.
      parentSetId: set.value.parentSetId || null
    });
    hydrate(updated);
    notify.success("Saved.");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
    load();
  }
};

// ---- item dialog ----
const itemDialogOpen = ref(false);
const editingItem = ref(null);
const openCreateItem = () => { editingItem.value = null; itemDialogOpen.value = true; };
const openEditItem = (item) => { editingItem.value = item; itemDialogOpen.value = true; };

const removeItem = async (item) => {
  const ok = await confirm({
    title: "Delete value",
    message: `Delete "${item.label}"?`,
    confirmLabel: "Delete",
    type: "danger"
  });
  if (!ok) return;
  try {
    await optionSetApi.removeItem(setId, item.id);
    notify.success("Value deleted.");
    reloadItems();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// ---- values table ----
// The few that matter show by default; the rest are in the Columns menu.
const columns = computed(() => [
  { name: "label", label: "Label", field: "label", align: "left", sortable: true, default: true },
  { name: "value", label: "Code", field: "value", align: "left", sortable: true, default: true },
  ...(set.value?.parentSetId
    ? [{ name: "validUnder", label: `Valid under${parentName.value ? ` (${parentName.value})` : ""}`, field: parentLabelsOf, align: "left", sortable: true, default: true }]
    : []),
  { name: "maconomyNumber", label: "Maconomy number", field: "maconomyNumber", align: "left", sortable: true, default: true },
  { name: "isActive", label: "Status", field: "isActive", align: "left", sortable: true, default: true },
  { name: "description", label: "Description", field: "description", align: "left", sortable: true },
  { name: "isDefault", label: "Default", field: "isDefault", align: "left", sortable: true },
  { name: "isSystem", label: "Type", field: "isSystem", align: "left", sortable: true },
  { name: "sortOrder", label: "Position", field: "sortOrder", align: "left", sortable: true },
  { name: "colours", label: "Colours", field: "backgroundColor", align: "left" },
  { name: "icon", label: "Icon", field: "icon", align: "left", sortable: true },
  { name: "metadataJson", label: "Metadata", field: "metadataJson", align: "left" },
  ...(set.value?.isEditable ? [{ name: "actions", label: "Actions", field: "actions", align: "left" }] : [])
]);

// Quick search over every column of a value, shown or not.
const search = ref("");
const shownItems = computed(() => {
  const needle = (search.value || "").trim().toLowerCase();
  if (!needle) return items.value;
  return items.value.filter((i) =>
    [i.label, i.value, i.description, i.maconomyNumber, i.icon, i.metadataJson, parentLabelsOf(i)]
      .some((v) => String(v || "").toLowerCase().includes(needle)));
});

// ---- Maconomy number, edited on the row ----
// What is typed and not yet saved, by value id.
const maconomyDrafts = ref({});
const savingMaconomyId = ref(null);
// The one row whose number is open as a box.
const editingMaconomyId = ref(null);
const closeMaconomyEdit = (item) => {
  delete maconomyDrafts.value[item.id];
  if (editingMaconomyId.value === item.id) editingMaconomyId.value = null;
};
// Esc: the draft is dropped first, so the blur that follows saves nothing.
const cancelMaconomyEdit = closeMaconomyEdit;
const maconomyDraft = (item) => maconomyDrafts.value[item.id] ?? item.maconomyNumber ?? "";

const saveMaconomyNumber = async (item) => {
  if (!(item.id in maconomyDrafts.value)) return closeMaconomyEdit(item);
  const next = String(maconomyDrafts.value[item.id]).trim() || null;
  if (next === (item.maconomyNumber || null)) return closeMaconomyEdit(item);
  savingMaconomyId.value = item.id;
  try {
    // The update replaces the whole value, so everything else goes back as it is.
    const saved = await optionSetApi.updateItem(setId, item.id, {
      label: item.label,
      value: item.value,
      description: item.description || null,
      maconomyNumber: next,
      parentItemIds: item.parentItemIds || [],
      isDefault: item.isDefault,
      isActive: item.isActive,
      backgroundColor: item.backgroundColor || null,
      textColor: item.textColor || null,
      icon: item.icon || null,
      metadataJson: item.metadataJson || null
    });
    item.maconomyNumber = saved?.maconomyNumber ?? next;
    closeMaconomyEdit(item);
    notify.success(`Maconomy number saved for ${item.label}.`);
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    savingMaconomyId.value = null;
  }
};

// ---- custom order ----
// Up / down on each row, offered while no column sort or search is hiding the list's own order.
const sortedBy = ref("");
const canMove = computed(() =>
  !!set.value?.isEditable && sortMode.value === OptionItemSortMode.Custom && !sortedBy.value && !(search.value || "").trim());
const indexOf = (item) => items.value.findIndex((x) => x.id === item.id);

const move = async (item, step) => {
  const from = indexOf(item);
  const to = from + step;
  if (from < 0 || to < 0 || to >= items.value.length) return;

  const next = [...items.value];
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  items.value = next;

  try {
    await optionSetApi.reorderItems(setId, next.map((x) => x.id));
  } catch (err) {
    notify.error(getApiErrorMessage(err));
    reloadItems();
  }
};

onMounted(load);
</script>

<style scoped>
.os-inline { width: 180px; max-width: 100%; }
.os-inline-text { display: inline-flex; align-items: center; gap: 6px; min-height: 28px; min-width: 80px; padding: 2px 6px; margin-left: -6px; border-radius: 4px; cursor: text; }
.os-inline-text:hover, .os-inline-text:focus-visible { background: rgba(0, 0, 0, 0.05); outline: none; }
.os-inline-text__icon { color: #9e9e9e; visibility: hidden; }
.os-inline-text:hover .os-inline-text__icon, .os-inline-text:focus-visible .os-inline-text__icon { visibility: visible; }
.os-value { min-height: 32px; display: flex; align-items: center; }
.os-key { font-family: monospace; font-size: 12px; word-break: break-all; }
.os-search { width: 220px; max-width: 100%; }
.os-swatch { display: inline-block; width: 12px; height: 12px; border-radius: 3px; margin-right: 6px; vertical-align: middle; border: 1px solid rgba(0, 0, 0, 0.12); }
</style>
