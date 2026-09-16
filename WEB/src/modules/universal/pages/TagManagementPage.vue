<template>
  <q-page padding>
    <div class="row items-center q-mb-md">
      <div class="text-h6">Tag Management</div>
      <q-space />
      <q-btn unelevated no-caps color="primary" icon="o_add" label="Add Tag" @click="openCreate" />
    </div>
    <q-toggle
      v-if="canManageDeleted" v-model="showDeleted" label="Show deleted?" dense class="q-mb-md"
    />

    <app-data-table
      page-key="uf_tags"
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
      <template #body-cell-colour="cell">
        <q-td :props="cell">
          <q-badge :style="{ backgroundColor: cell.row.colour || '#607d8b' }" text-color="white" :label="cell.row.colour || '—'" />
        </q-td>
      </template>
      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <q-btn type="a" flat round dense icon="o_edit" @click="openEdit(cell.row)" />
          <q-btn type="a" flat round dense icon="o_delete" color="negative" @click="remove(cell.row)" />
        </q-td>
      </template>
    </app-data-table>

    <deleted-records-panel
      v-if="canManageDeleted" :entity-type="EntityType.Tag" :show="showDeleted" @restored="load"
    />

    <app-form-drawer v-model="drawerOpen" :title="editing ? 'Edit Tag' : 'Add Tag'" :saving="saving" @submit="save" @cancel="drawerOpen = false">
      <q-form ref="formRef">
        <app-text-field v-model="form.name" label="Name *" :rules="[(v) => !!v || 'Name is required']" />
        <app-text-field v-model="form.category" label="Category" hint="Optional grouping for the tag picker" />
        <div class="row items-center q-gutter-xs q-mt-sm">
          <span class="text-grey-7 q-mr-sm">Colour</span>
          <div
            v-for="c in palette"
            :key="c"
            class="uf-swatch cursor-pointer"
            :style="{ backgroundColor: c, outline: c === form.colour ? '2px solid #1976d2' : 'none' }"
            @click="form.colour = c"
          />
        </div>
      </q-form>
    </app-form-drawer>
  </q-page>
</template>

<script setup>
import { ref, reactive } from "vue";
import { ufTagsApi, getApiErrorMessage, EntityType } from "services/api";
import { useNotify } from "composables/useNotify";
import { useDeletedRecords } from "composables/useDeletedRecords";
import { useConfirm } from "composables/useConfirm";
import { useAuditColumns } from "composables/useAuditColumns";
import { useListTable } from "composables/useListTable";
import AppDataTable from "components/common/AppDataTable.vue";
import DeletedRecordsPanel from "components/universal/DeletedRecordsPanel.vue";
import AppFormDrawer from "components/common/AppFormDrawer.vue";
import AppTextField from "components/common/AppTextField.vue";

const auditColumns = useAuditColumns();
const { showDeleted, canManageDeleted } = useDeletedRecords();
const notify = useNotify();
const { confirm } = useConfirm();
const palette = ["#ef5350", "#ec407a", "#ab47bc", "#5c6bc0", "#42a5f5", "#26a69a", "#9ccc65", "#ffa726", "#607d8b"];

// Ordering is the server's, like every other list.
const { rows, loading, totalRecords, pagination, load, onRequest } = useListTable({
  pageKey: "uf_tags",
  fetcher: ({ sortBy, descending }) =>
    ufTagsApi.list({ sortBy, descending }).then((r) => ({ data: r || [], total: (r || []).length })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

const columns = [
  { name: "name", label: "Name", field: "name", align: "left", sortable: true, default: true },
  { name: "colour", label: "Colour", field: "colour", align: "left", sortable: true, default: true },
  { name: "category", label: "Category", field: "category", align: "left", sortable: true, default: true },
  { name: "usageCount", label: "Usage", field: "usageCount", align: "left", sortable: true, default: true },
  ...auditColumns(),
  { name: "actions", label: "Actions", field: "actions", align: "left" }
];

const drawerOpen = ref(false);
const saving = ref(false);
const editing = ref(null);
const formRef = ref(null);
const form = reactive({ name: "", category: "", colour: palette[0] });

const openCreate = () => {
  editing.value = null;
  form.name = "";
  form.category = "";
  form.colour = palette[0];
  drawerOpen.value = true;
};

const openEdit = (tag) => {
  editing.value = tag;
  form.name = tag.name;
  form.category = tag.category || "";
  form.colour = tag.colour || palette[0];
  drawerOpen.value = true;
};

const save = async () => {
  const valid = await formRef.value?.validate();
  if (valid === false) return;
  saving.value = true;
  try {
    const payload = { name: form.name.trim(), category: form.category?.trim() || null, colour: form.colour };
    if (editing.value) {
      await ufTagsApi.update(editing.value.id, payload);
    } else {
      await ufTagsApi.create(payload);
    }
    drawerOpen.value = false;
    notify.success("Tag saved.");
    await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    saving.value = false;
  }
};

const remove = async (tag) => {
  const ok = await confirm({
    title: "Delete tag",
    message: `Delete "${tag.name}"? It is applied to ${tag.usageCount} record(s) and will be removed from all of them.`,
    confirmLabel: "Delete",
    type: "danger"
  });
  if (!ok) return;
  try {
    await ufTagsApi.remove(tag.id);
    notify.success("Tag deleted.");
    await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

</script>

<style scoped>
.uf-swatch { width: 26px; height: 26px; border-radius: 6px; }
</style>
