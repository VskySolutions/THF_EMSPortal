<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'Sticky Notes' }]"
      show-add
      add-label="New team note"
      show-back
      @add="openCreate"
      @back="$router.back()"
    />
    <q-toggle
      v-if="canManageDeleted" v-model="showDeleted" label="Show deleted?" dense class="q-mb-md"
    />

    <app-data-table
      page-key="uf_sticky_admin"
      row-key="id"
      :rows="rows"
      :columns="columns"
      :loading="loading"
      :total-records="totalRecords"
      :pagination="pagination"
      default-sort-by="updatedOnUtc"
      @request="onRequest"
      @refresh="load"
    >
      <template #body-cell-expiresAtUtc="cell">
        <q-td :props="cell">{{ cell.row.expiresAtUtc ? formatDateTime(cell.row.expiresAtUtc) : "—" }}</q-td>
      </template>
      <template #body-cell-createdOnUtc="cell">
        <q-td :props="cell">{{ formatDateTime(cell.row.createdOnUtc) }}</q-td>
      </template>
      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <q-btn type="a" flat round dense icon="o_delete" color="negative" @click="remove(cell.row)" />
        </q-td>
      </template>
    </app-data-table>

    <deleted-records-panel
      v-if="canManageDeleted" :entity-type="EntityType.StickyNote" :show="showDeleted" @restored="load"
    />

    <q-dialog v-model="createOpen">
      <q-card style="min-width: 340px;">
        <q-card-section class="text-h6">New team sticky note</q-card-section>
        <q-card-section class="q-pt-none column q-gutter-sm">
          <app-text-field v-model="form.title" label="Title" />
          <app-text-field v-model="form.body" label="Note *" type="textarea" autogrow />
          <app-text-field v-model="form.expiresAt" type="datetime-local" label="Expires (optional)" />
        </q-card-section>
        <q-card-actions align="right">
          <q-btn v-close-popup flat no-caps label="Cancel" />
          <q-btn unelevated no-caps color="primary" label="Create" :disable="!form.body.trim()" :loading="creating" @click="create" />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup>
import { ref, reactive } from "vue";
import { ufStickyNoteApi, getApiErrorMessage, EntityType } from "services/api";
import { useNotify } from "composables/useNotify";
import { useDeletedRecords } from "composables/useDeletedRecords";
import { useConfirm } from "composables/useConfirm";
import { useDateFormat } from "composables/useDateFormat";
import { useAuditColumns } from "composables/useAuditColumns";
import { useListTable } from "composables/useListTable";
import AppDataTable from "components/common/AppDataTable.vue";
import DeletedRecordsPanel from "components/universal/DeletedRecordsPanel.vue";
import AppListHeader from "components/common/AppListHeader.vue";
import AppTextField from "components/common/AppTextField.vue";

const auditColumns = useAuditColumns();
const { showDeleted, canManageDeleted } = useDeletedRecords();
const notify = useNotify();
const { confirm } = useConfirm();
const { formatDateTime } = useDateFormat();

// Ordering is the server's, like every other list.
const { rows, loading, totalRecords, pagination, load, onRequest } = useListTable({
  pageKey: "uf_sticky_admin",
  fetcher: ({ sortBy, descending }) =>
    ufStickyNoteApi.adminList({ sortBy, descending })
      .then((r) => ({ data: r || [], total: (r || []).length })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

const columns = [
  { name: "title", label: "Title", field: "title", align: "left", sortable: true, default: true },
  { name: "scope", label: "Scope", field: "scope", align: "left", sortable: true, default: true },
  // Created On is NOT defined here: the shared audit set below carries it, and two columns of the same
  // name is one the visibility map cannot tell from the other.
  { name: "expiresAtUtc", label: "Expires", field: "expiresAtUtc", align: "left", sortable: true, default: true },
  { name: "dismissalCount", label: "Dismissals", field: "dismissalCount", align: "left", sortable: true, default: true },
  ...auditColumns(),
  { name: "actions", label: "Actions", field: "actions", align: "left" }
];

const createOpen = ref(false);
const creating = ref(false);
const form = reactive({ title: "", body: "", expiresAt: "" });

const openCreate = () => {
  form.title = "";
  form.body = "";
  form.expiresAt = "";
  createOpen.value = true;
};

const create = async () => {
  creating.value = true;
  try {
    await ufStickyNoteApi.create({
      title: form.title || null,
      body: form.body.trim(),
      colour: "#fff9c4",
      scope: "global",
      isPersonal: false,
      expiresAtUtc: form.expiresAt ? new Date(form.expiresAt).toISOString() : null
    });
    createOpen.value = false;
    notify.success("Team note created.");
    await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    creating.value = false;
  }
};

const remove = async (row) => {
  const ok = await confirm({ title: "Delete note", message: "Delete this team sticky note for everyone?", confirmLabel: "Delete", type: "danger" });
  if (!ok) return;
  try {
    await ufStickyNoteApi.remove(row.id);
    notify.success("Note deleted.");
    await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};
</script>
