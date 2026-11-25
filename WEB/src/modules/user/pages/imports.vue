<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'Users', to: '/users' }, { label: 'Import History' }]"
      show-back
      @back="$router.back()"
    />

    <app-data-table
      page-key="user-imports"
      row-key="id"
      title="User imports"
      :rows="rows"
      :columns="columns"
      :loading="loading"
      :total-records="totalRecords"
      :pagination="pagination"
      @request="onRequest"
      @refresh="load"
    >
      <template #body-cell-failedCount="cell">
        <q-td :props="cell">
          <q-badge v-if="cell.value" color="negative">{{ cell.value }}</q-badge>
          <span v-else class="text-grey-6">0</span>
        </q-td>
      </template>

      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <q-btn flat round dense color="primary" icon="o_visibility" @click="view(cell.row)">
            <q-tooltip>View rows</q-tooltip>
          </q-btn>
        </q-td>
      </template>
    </app-data-table>

    <q-dialog v-model="viewOpen" :maximized="$q.screen.lt.md">
      <q-card class="import-view">
        <q-card-section class="row items-center bg-primary text-white">
          <q-icon name="o_upload_file" size="22px" />
          <div class="text-h6 q-ml-sm">{{ viewing?.fileName }}</div>
          <q-space />
          <q-btn flat round dense color="white" icon="o_close" @click="viewOpen = false" />
        </q-card-section>
        <q-separator />

        <q-card-section class="import-view__body">
          <div v-if="viewing" class="text-body2 text-grey-8 q-mb-md">
            Imported by {{ viewing.importedBy || "—" }} on {{ formatDateTime(viewing.importedOnUtc) }} ·
            {{ viewing.createdCount }} created, {{ viewing.updatedCount || 0 }} updated, {{ viewing.failedCount }} failed
          </div>
          <user-import-rows :rows="viewRows" :file-name="viewing?.fileName" />
          <q-inner-loading :showing="loadingRows" />
        </q-card-section>

        <q-separator />
        <q-card-actions align="right" class="bg-grey-1">
          <q-btn flat no-caps color="grey-8" label="Close" @click="viewOpen = false" />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup>
// Every file of users loaded into this tenant, read-only: who loaded it, when, and what became of each line.
import { ref, computed } from "vue";
import { useQuasar } from "quasar";
import { userApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useListTable } from "composables/useListTable";
import { useDateFormat } from "composables/useDateFormat";

import AppDataTable from "components/common/AppDataTable.vue";
import AppListHeader from "components/common/AppListHeader.vue";
import UserImportRows from "components/user/UserImportRows.vue";

const $q = useQuasar();
const notify = useNotify();
const { formatDateTime } = useDateFormat();

// Newest first, as the API orders it; the columns are not sortable.
const columns = computed(() => [
  { name: "importedOnUtc", label: "Date", field: (r) => formatDateTime(r.importedOnUtc), align: "left", default: true },
  { name: "fileName", label: "File", field: "fileName", align: "left", default: true },
  { name: "importedBy", label: "Imported By", field: "importedBy", align: "left", default: true },
  { name: "totalCount", label: "Lines", field: "totalCount", align: "left", default: true },
  { name: "createdCount", label: "Created", field: "createdCount", align: "left", default: true },
  { name: "updatedCount", label: "Updated", field: (r) => r.updatedCount || 0, align: "left", default: true },
  { name: "failedCount", label: "Failed", field: "failedCount", align: "left", default: true },
  { name: "actions", label: "Actions", field: "actions", align: "left" }
]);

const { rows, loading, totalRecords, pagination, load, onRequest } = useListTable({
  pageKey: "user-imports",
  fetcher: ({ page, limit }) =>
    userApi.imports({ page, limit }).then((r) => ({ data: r?.data, total: r?.meta?.totalRecords })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

// ---- One import's rows ----
const viewOpen = ref(false);
const viewing = ref(null);
const viewRows = ref([]);
const loadingRows = ref(false);

const view = async (row) => {
  viewing.value = row;
  viewRows.value = [];
  viewOpen.value = true;
  loadingRows.value = true;
  try {
    const detail = await userApi.getImport(row.id);
    viewRows.value = detail?.rows || [];
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loadingRows.value = false;
  }
};
</script>

<style scoped>
.import-view {
  width: 1100px;
  max-width: 96vw;
}
.import-view__body {
  position: relative;
  min-height: 200px;
}
</style>
