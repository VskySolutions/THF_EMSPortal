<template>
  <div v-if="show" class="uf-deleted q-mt-md">
    <app-data-table
      :title="`Deleted ${pluralLabel}`"
      page-key="uf_deleted"
      row-key="entityId"
      selectable
      :rows="rows"
      :columns="columns"
      :loading="loading"
      :total-records="totalRecords"
      :pagination="pagination"
      default-sort-by="deletedOnUtc"
      @request="onRequest"
      @refresh="load"
      @update:selected="selected = $event"
    >
      <template #actions>
        <q-badge v-if="overdueCount" color="orange-8" :label="`${overdueCount} overdue`" />
      </template>

      <template #bulk-actions="{ selected: sel }">
        <q-btn flat dense no-caps color="primary" icon="o_restore" label="Restore Selected" @click="restoreBulk(sel)" />
        <q-btn flat dense no-caps color="negative" icon="o_delete_forever" label="Permanently Delete Selected" @click="hardDeleteBulk(sel)" />
      </template>

      <template #body-cell-identity="cell">
        <q-td :props="cell">
          <span class="text-strike text-grey-7">{{ cell.row.identity }}</span>
          <q-badge color="grey-6" label="Deleted" class="q-ml-xs" />
          <q-chip v-if="cell.row.isRetentionOverdue" dense square color="orange-3" text-color="orange-9" label="Overdue" class="q-ml-xs" />
        </q-td>
      </template>
      <template #body-cell-deletedOnUtc="cell">
        <q-td :props="cell">{{ formatDateTime(cell.row.deletedOnUtc) }}</q-td>
      </template>
      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <!-- One button per action, both of them on the row. -->
          <q-btn type="a" flat round dense color="primary" icon="o_restore" @click="restore(cell.row)">
            <q-tooltip>Restore</q-tooltip>
          </q-btn>
          <q-btn type="a" flat round dense color="negative" icon="o_delete_forever" @click="hardDelete(cell.row)">
            <q-tooltip>Permanently Delete</q-tooltip>
          </q-btn>
        </q-td>
      </template>
    </app-data-table>

    <!-- Two-step permanent delete dialog -->
    <q-dialog v-model="confirmOpen">
      <q-card style="width: 360px; max-width: 92vw;">
        <q-card-section class="text-h6 text-negative">Permanently delete</q-card-section>
        <q-card-section class="q-pt-none">
          <p>This permanently deletes <strong>{{ target?.identity }}</strong> and all its related data. This cannot be undone.</p>
          <p class="text-grey-7">Type <strong>DELETE</strong> to confirm.</p>
          <q-input v-model="confirmText" outlined dense autofocus placeholder="DELETE" />
        </q-card-section>
        <q-card-actions align="right">
          <q-btn v-close-popup flat no-caps label="Cancel" />
          <q-btn unelevated no-caps color="negative" label="Permanently Delete" :disable="confirmText !== 'DELETE'" :loading="deleting" @click="confirmHardDelete" />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </div>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { ufDeletedApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useDateFormat } from "composables/useDateFormat";
import { useEntityMeta } from "composables/uf/useEntityMeta";
import AppDataTable from "components/common/AppDataTable.vue";

const props = defineProps({
  entityType: { type: Number, required: true },
  // Controlled by the "Show deleted" toggle in the list page's AppFilterDrawer.
  show: { type: Boolean, default: false }
});
const emit = defineEmits(["restored"]);

const notify = useNotify();
const { confirm } = useConfirm();
const { formatDateTime } = useDateFormat();
const meta = useEntityMeta();
const pluralLabel = computed(() => `${meta.labelFor(props.entityType)}s`);

const rows = ref([]);
const loading = ref(false);
const totalRecords = ref(0);
const selected = ref([]);
const overdueCount = ref(0);
// Newest deletion first, and ordered by the SERVER: this panel is paged, so ordering the twenty rows it
// holds would order the page rather than the record of what was deleted.
const pagination = ref({ page: 1, rowsPerPage: 20, sortBy: "deletedOnUtc", descending: true, rowsNumber: 0 });

const columns = [
  { name: "identity", label: "Record", field: "identity", align: "left", sortable: true, default: true },
  { name: "deletedByName", label: "Deleted By", field: "deletedByName", align: "left", default: true },
  { name: "deletedOnUtc", label: "Deleted On", field: "deletedOnUtc", align: "left", sortable: true, default: true },
  { name: "actions", label: "Actions", field: "actions", align: "left" }
];

const load = async () => {
  loading.value = true;
  try {
    const res = await ufDeletedApi.list({
      entityType: props.entityType,
      page: pagination.value.page,
      limit: pagination.value.rowsPerPage,
      sortBy: pagination.value.sortBy,
      descending: pagination.value.descending
    });
    rows.value = res?.data || [];
    totalRecords.value = res?.meta?.totalRecords || rows.value.length;
    overdueCount.value = rows.value.filter((r) => r.isRetentionOverdue).length;
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loading.value = false;
  }
};

// React to the "Show deleted" toggle (lives in the list page's AppFilterDrawer).
watch(() => props.show, (val) => {
  if (val) load();
  else { rows.value = []; selected.value = []; }
}, { immediate: true });

const onRequest = (pag) => {
  pagination.value = { ...pagination.value, ...pag };
  load();
};

const restore = async (row) => {
  const ok = await confirm({ title: "Restore record", message: `Restore "${row.identity}"?`, confirmLabel: "Restore" });
  if (!ok) return;
  try {
    await ufDeletedApi.restore({ entityType: props.entityType, entityId: row.entityId });
    notify.success("Record restored.");
    emit("restored");
    await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

const restoreBulk = async (sel) => {
  const ok = await confirm({ title: "Restore selected", message: `Restore ${sel.length} record(s)?`, confirmLabel: "Restore" });
  if (!ok) return;
  try {
    await ufDeletedApi.restoreBulk({ entityType: props.entityType, entityIds: sel.map((r) => r.entityId) });
    notify.success("Records restored.");
    emit("restored");
    selected.value = [];
    await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// ---- Permanent delete (two-step) ----
const confirmOpen = ref(false);
const confirmText = ref("");
const deleting = ref(false);
const target = ref(null);

const hardDelete = (row) => {
  target.value = row;
  confirmText.value = "";
  confirmOpen.value = true;
};

const confirmHardDelete = async () => {
  if (!target.value) return;
  deleting.value = true;
  try {
    // The backend validates the confirmation token against the record's identity.
    await ufDeletedApi.hardDelete({
      entityType: props.entityType,
      entityId: target.value.entityId,
      confirmationToken: target.value.identity
    });
    notify.success("Record permanently deleted.");
    confirmOpen.value = false;
    await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    deleting.value = false;
  }
};

const hardDeleteBulk = async (sel) => {
  const ok = await confirm({
    title: "Permanently delete selected",
    message: `Permanently delete ${sel.length} record(s)? This cannot be undone.`,
    confirmLabel: "Permanently Delete",
    type: "danger"
  });
  if (!ok) return;
  try {
    await ufDeletedApi.hardDeleteBulk({
      entityType: props.entityType,
      entityIds: sel.map((r) => r.entityId),
      confirmationCount: sel.length
    });
    notify.success("Records permanently deleted.");
    selected.value = [];
    await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

defineExpose({ refresh: () => props.show && load() });
</script>

<style scoped>
/* Red accent marks this as the destructive "deleted records" area. */
/* Match the list's data table (the AppDataTable is the card); just tint it red. */
.uf-deleted :deep(.app-data-table) {
  border-left: 3px solid var(--q-negative);
}
.uf-deleted :deep(.app-data-table .text-subtitle1) {
  color: var(--q-negative);
}
</style>
