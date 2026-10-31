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

      <!-- Settings card -->
      <q-card flat bordered class="q-pa-md q-mb-md">
        <div class="row items-center q-col-gutter-md">
          <div class="col-12 col-sm-4">
            <div class="text-caption text-grey-6">Name</div>
            <div class="text-weight-medium">{{ set.name }}</div>
            <div class="fs-12 text-grey-6">{{ set.key }}</div>
          </div>
          <div class="col-12 col-sm-4">
            <div class="text-caption text-grey-6">Applies to</div>
            <div><q-icon :name="iconFor(set.entityType)" color="primary" class="q-mr-xs" />{{ labelFor(set.entityType) }}</div>
          </div>
          <div class="col-12 col-sm-4">
            <app-select
              v-model="sortMode"
              :options="sortModeOptions"
              label="Item order"
              :clearable="false"
              :readonly="!set.isEditable"
              @update:model-value="saveSettings"
            />
          </div>
        </div>
        <div v-if="set.isEditable" class="q-mt-sm">
          <q-toggle v-model="isActive" label="Active" @update:model-value="saveSettings" />
        </div>
      </q-card>

      <!-- Values card -->
      <q-card flat bordered class="q-mb-md">
        <q-card-section class="row items-center">
          <div class="text-subtitle1 text-weight-medium">Values</div>
          <q-space />
          <q-btn v-if="set.isEditable" unelevated no-caps color="primary" icon="o_add" label="Add value" @click="openCreateItem" />
        </q-card-section>
        <q-separator />

        <div v-if="!items.length" class="text-grey-6 q-pa-lg text-center">No values yet.</div>

        <q-list v-else separator>
          <q-item
            v-for="(item, index) in items"
            :key="item.id"
            :draggable="canDrag"
            :class="{ 'uf-row--over': overIndex === index, 'uf-row--drag': dragIndex === index }"
            @dragstart="onDragStart(index, $event)"
            @dragover.prevent="onDragOver(index)"
            @drop.prevent="onDrop(index)"
            @dragend="onDragEnd"
          >
            <q-item-section v-if="canDrag" side class="uf-handle">
              <q-icon name="o_drag_indicator" class="text-grey-6 cursor-move" />
            </q-item-section>
            <q-item-section>
              <q-item-label>
                <!-- Render the value with its configured display colours when set. -->
                <q-chip
                  v-if="item.backgroundColor || item.textColor"
                  dense
                  :style="{ backgroundColor: item.backgroundColor || '#e0e0e0', color: item.textColor || '#212121' }"
                  :label="item.label"
                />
                <span v-else>{{ item.label }}</span>
                <q-badge v-if="item.isDefault" color="teal" label="Default" class="q-ml-sm" />
                <q-badge v-if="!item.isActive" color="grey-5" label="Inactive" class="q-ml-xs" />
              </q-item-label>
              <q-item-label caption>
                {{ item.value }}
                <!-- The number Maconomy files this value under, where the Maconomy sync uses one. -->
                <span v-if="item.maconomyNumber"> · Maconomy {{ item.maconomyNumber }}</span>
                <span v-if="parentLabelMap[item.parentItemId]"> · under {{ parentLabelMap[item.parentItemId] }}</span>
              </q-item-label>
            </q-item-section>
            <q-item-section v-if="set.isEditable" side>
              <div class="row no-wrap">
                <q-btn flat round dense color="primary" icon="o_edit" @click="openEditItem(item)" />
                <q-btn flat round dense color="negative" icon="o_delete" @click="removeItem(item)" />
              </div>
            </q-item-section>
          </q-item>
        </q-list>
      </q-card>

      <app-record-audit :audit="set.audit" />

      <option-item-dialog
        v-model="itemDialogOpen"
        :set-id="set.id"
        :item="editingItem"
        :parent-options="parentOptions"
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
import { useEntityMeta } from "composables/uf/useEntityMeta";
import AppDetailHeader from "components/common/AppDetailHeader.vue";
import AppSelect from "components/common/AppSelect.vue";
import AppRecordAudit from "components/common/AppRecordAudit.vue";
import OptionItemDialog from "modules/option-set/components/OptionItemDialog.vue";

const route = useRoute();
const notify = useNotify();
const { confirm } = useConfirm();
const { labelFor, iconFor } = useEntityMeta();

const setId = route.params.id;

const set = ref(null);
const items = ref([]);
const loading = ref(false);

const sortMode = ref(OptionItemSortMode.Custom);
const isActive = ref(true);

const parentOptions = ref([]);
const parentLabelMap = ref({});

const sortModeOptions = [
  { label: "Alphabetical (A → Z)", value: OptionItemSortMode.AlphabeticalAsc },
  { label: "Alphabetical (Z → A)", value: OptionItemSortMode.AlphabeticalDesc },
  { label: "Custom (drag to order)", value: OptionItemSortMode.Custom }
];

// Drag-reorder is only meaningful for an editable, custom-ordered list.
const canDrag = computed(() => !!set.value?.isEditable && sortMode.value === OptionItemSortMode.Custom);

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
    return;
  }
  try {
    const parent = await optionSetApi.get(set.value.parentSetId);
    const parentItems = parent.items || [];
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
      isActive: isActive.value
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

// ---- native drag reorder (same pattern as AppDataTable column reorder) ----
const dragIndex = ref(null);
const overIndex = ref(null);

const onDragStart = (i, e) => {
  if (!canDrag.value) return;
  dragIndex.value = i;
  if (e.dataTransfer) e.dataTransfer.effectAllowed = "move";
};
const onDragOver = (i) => { if (canDrag.value) overIndex.value = i; };
const onDragEnd = () => { dragIndex.value = null; overIndex.value = null; };

const onDrop = async (i) => {
  const from = dragIndex.value;
  dragIndex.value = null;
  overIndex.value = null;
  if (from === null || from === i) return;

  const next = [...items.value];
  const [moved] = next.splice(from, 1);
  next.splice(i, 0, moved);
  items.value = next;

  try {
    await optionSetApi.reorderItems(setId, next.map((x) => x.id));
    notify.success("Order saved.");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
    reloadItems();
  }
};

onMounted(load);
</script>

<style scoped>
.uf-row--over { outline: 2px dashed var(--q-primary); outline-offset: -2px; }
.uf-row--drag { opacity: 0.5; }
.uf-handle { min-width: 28px; }
</style>
