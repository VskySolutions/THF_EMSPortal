<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'Error Log' }]"
      :search="search"
      show-search
      search-placeholder="Search message or exception"
      show-filters
      :filter-count="filterChips.length"
      show-back
      @update:search="search = $event"
      @filters="filterOpen = true"
      @back="$router.back()"
    />

    <app-filter-drawer v-model="filterOpen" :chips="filterChips" @remove="removeFilter" @clear="clearFilters">
      <app-column-filters v-model="filters" :columns="filterableColumns" />
    </app-filter-drawer>

    <app-data-table
      page-key="error-log"
      row-key="id"
      title="Errors logged for this site"
      :rows="rows"
      :columns="columns"
      :loading="loading"
      :total-records="totalRecords"
      :pagination="pagination"
      @request="onRequest"
      @refresh="load"
    >
      <template #body-cell-level="cell">
        <q-td :props="cell">
          <q-badge color="negative">{{ cell.row.level }}</q-badge>
          <q-badge v-if="cell.row.isSystem" color="grey" class="q-ml-xs">
            System
            <q-tooltip>Belongs to no site: logged outside a signed-in request.</q-tooltip>
          </q-badge>
        </q-td>
      </template>

      <template #body-cell-message="cell">
        <q-td :props="cell">
          <div class="ellipsis log-message">{{ cell.row.message }}</div>
        </q-td>
      </template>

      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <q-btn flat round dense color="primary" icon="o_visibility" @click="view(cell.row)">
            <q-tooltip>View details</q-tooltip>
          </q-btn>
        </q-td>
      </template>
    </app-data-table>

    <q-dialog v-model="viewOpen">
      <q-card style="width: 860px; max-width: 94vw;">
        <q-card-section class="row items-center bg-primary text-white">
          <q-icon name="o_bug_report" size="22px" />
          <div class="text-h6 q-ml-sm">Log entry</div>
          <q-space />
          <q-btn flat round dense color="white" icon="o_close" @click="viewOpen = false" />
        </q-card-section>
        <q-separator />

        <q-card-section v-if="viewing" class="scroll" style="max-height: 70vh;">
          <div class="row q-col-gutter-md">
            <div class="col-12 col-sm-6">
              <div class="text-caption text-grey-7">Logged on</div>
              <div class="text-weight-medium">{{ formatDateTime(viewing.loggedOnUtc) }}</div>
            </div>
            <div class="col-12 col-sm-6">
              <div class="text-caption text-grey-7">Level</div>
              <div class="text-weight-medium">{{ viewing.level }}<template v-if="viewing.isSystem"> · System</template></div>
            </div>
            <div class="col-12">
              <div class="text-caption text-grey-7">Message</div>
              <div class="log-text">{{ detail?.log?.message || viewing.message }}</div>
            </div>
            <div v-if="detail?.exception" class="col-12">
              <div class="text-caption text-grey-7">Exception</div>
              <pre class="log-pre rounded-borders q-ma-none q-pa-sm">{{ detail.exception }}</pre>
            </div>
            <div v-if="detail?.properties?.length" class="col-12">
              <div class="text-caption text-grey-7 q-mb-xs">Properties</div>
              <q-markup-table flat bordered dense wrap-cells>
                <tbody>
                  <tr v-for="p in detail.properties" :key="p.name">
                    <td class="text-grey-8 log-prop-name">{{ p.name }}</td>
                    <td class="log-text">{{ p.value }}</td>
                  </tr>
                </tbody>
              </q-markup-table>
            </div>
          </div>
          <q-inner-loading :showing="loadingDetail" />
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
import { ref, computed, watch } from "vue";
import { debounce } from "quasar";
import { errorLogApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useListTable } from "composables/useListTable";
import { useColumnFilters } from "composables/useColumnFilters";
import { useDateFormat } from "composables/useDateFormat";

import AppDataTable from "components/common/AppDataTable.vue";
import AppFilterDrawer from "components/common/AppFilterDrawer.vue";
import AppColumnFilters from "components/common/AppColumnFilters.vue";
import AppListHeader from "components/common/AppListHeader.vue";

const notify = useNotify();
const { formatDateTime } = useDateFormat();

// The table takes these two levels only.
const levelOptions = ["Error", "Fatal"].map((value) => ({ label: value, value }));

// Newest first, as the API orders it; the columns are not sortable.
const columns = computed(() => [
  { name: "loggedOnUtc", label: "Date", field: (r) => formatDateTime(r.loggedOnUtc), align: "left", default: true, filterable: false },
  { name: "level", label: "Level", field: "level", align: "left", default: true, filterOptions: levelOptions },
  { name: "message", label: "Message", field: "message", align: "left", default: true, filterable: false },
  { name: "actions", label: "Actions", field: "actions", align: "left" }
]);

const { rows, loading, totalRecords, search, filterOpen, pagination, load, onRequest } = useListTable({
  pageKey: "error-log",
  fetcher: ({ page, limit }) =>
    errorLogApi.list({
      page,
      limit,
      search: search.value || undefined,
      level: filters.level || undefined
    }).then((r) => ({ data: r?.data, total: r?.meta?.totalRecords })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

const { filters, filterableColumns, filterChips, removeFilter, clearFilters } = useColumnFilters(columns, rows, { server: true });
const reload = debounce(() => { pagination.value.page = 1; load(); }, 300);
watch([search, filters], reload, { deep: true });

// ---- View one entry ----
const viewOpen = ref(false);
const viewing = ref(null);
const detail = ref(null);
const loadingDetail = ref(false);

const view = async (row) => {
  viewing.value = row;
  detail.value = null;
  viewOpen.value = true;
  loadingDetail.value = true;
  try {
    detail.value = await errorLogApi.get(row.id);
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loadingDetail.value = false;
  }
};
</script>

<style scoped>
.log-message {
  max-width: 60vw;
}
.log-text {
  white-space: pre-wrap;
  word-break: break-word;
}
.log-pre {
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 40vh;
  overflow: auto;
  font-size: 12px;
  background: #f5f5f5;
}
.log-prop-name {
  width: 180px;
  vertical-align: top;
}
</style>
