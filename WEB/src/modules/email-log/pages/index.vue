<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'Email Log' }]"
      :search="search"
      show-search
      search-placeholder="Search recipient or subject"
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
      page-key="email-log"
      row-key="id"
      title="Emails sent by this site"
      :rows="rows"
      :columns="columns"
      :loading="loading"
      :total-records="totalRecords"
      :pagination="pagination"
      @request="onRequest"
      @refresh="load"
    >
      <template #body-cell-status="cell">
        <q-td :props="cell">
          <q-badge :color="STATUS[cell.row.status]?.color || 'grey'">{{ STATUS[cell.row.status]?.label || cell.row.status }}</q-badge>
          <q-icon v-if="cell.row.failureReason" name="o_info" size="16px" color="grey-7" class="q-ml-xs">
            <q-tooltip max-width="360px">{{ cell.row.failureReason }}</q-tooltip>
          </q-icon>
        </q-td>
      </template>

      <template #body-cell-opened="cell">
        <q-td :props="cell">
          <template v-if="cell.row.openCount">
            <q-badge color="positive">Opened</q-badge>
            <span class="text-caption text-grey-7 q-ml-xs">
              {{ formatDateTime(cell.row.firstOpenedOnUtc) }}<template v-if="cell.row.openCount > 1"> · {{ cell.row.openCount }} times</template>
            </span>
          </template>
          <span v-else class="text-grey-6">{{ cell.row.status === "Sent" ? "Not yet" : "—" }}</span>
        </q-td>
      </template>

      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <q-btn flat round dense color="primary" icon="o_visibility" @click="view(cell.row)">
            <q-tooltip>View email</q-tooltip>
          </q-btn>
        </q-td>
      </template>
    </app-data-table>

    <q-dialog v-model="viewOpen">
      <q-card style="width: 720px; max-width: 92vw;">
        <q-card-section class="row items-center bg-primary text-white">
          <q-icon name="o_mail" size="22px" />
          <div class="text-h6 q-ml-sm">Email</div>
          <q-space />
          <q-btn flat round dense color="white" icon="o_close" @click="viewOpen = false" />
        </q-card-section>
        <q-separator />

        <q-card-section v-if="viewing">
          <div class="row q-col-gutter-md q-mb-md">
            <div class="col-12 col-sm-6">
              <div class="text-caption text-grey-7">To</div>
              <div class="text-weight-medium">{{ viewing.recipientEmail }}</div>
            </div>
            <div class="col-12 col-sm-6">
              <div class="text-caption text-grey-7">Sent on</div>
              <div class="text-weight-medium">{{ formatDateTime(viewing.sentOnUtc) }}</div>
            </div>
            <div class="col-12">
              <div class="text-caption text-grey-7">Subject</div>
              <div class="text-weight-medium">{{ viewing.subject || "—" }}</div>
            </div>
            <div v-if="viewing.failureReason" class="col-12">
              <div class="text-caption text-grey-7">{{ viewing.status === "Skipped" ? "Why it was not sent" : "Why it failed" }}</div>
              <div class="text-negative">{{ viewing.failureReason }}</div>
            </div>
          </div>
          <q-inner-loading :showing="loadingBody" />
          <!-- Sandboxed: the email is shown as it was sent, and nothing in it can run or reach this page. -->
          <iframe v-if="body" class="email-body rounded-borders" sandbox="" :srcdoc="body" title="Email body" />
          <div v-else-if="!loadingBody" class="text-grey-6">Nothing was written: the email was held back before it was composed.</div>
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
import { emailLogApi, getApiErrorMessage } from "services/api";
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

// "Sent" is the mail server's acceptance; plain SMTP says nothing about the inbox.
const STATUS = {
  Sent: { label: "Sent", color: "positive" },
  Failed: { label: "Failed", color: "negative" },
  Skipped: { label: "Not sent", color: "grey" }
};
const statusOptions = Object.entries(STATUS).map(([value, s]) => ({ label: s.label, value }));

// Newest first, as the API orders it; the columns are not sortable.
const columns = computed(() => [
  { name: "sentOnUtc", label: "Date", field: (r) => formatDateTime(r.sentOnUtc), align: "left", default: true, filterable: false },
  { name: "templateName", label: "Email", field: "templateName", align: "left", default: true, filterable: false },
  { name: "recipientEmail", label: "Recipient", field: "recipientEmail", align: "left", default: true, filterable: false },
  { name: "subject", label: "Subject", field: "subject", align: "left", default: true, filterable: false },
  { name: "status", label: "Status", field: "status", align: "left", default: true, filterOptions: statusOptions },
  {
    name: "opened",
    label: "Opened",
    field: (r) => r.openCount > 0,
    align: "left",
    default: true,
    filterOptions: [{ label: "Opened", value: true }, { label: "Not opened", value: false }]
  },
  { name: "actions", label: "Actions", field: "actions", align: "left" }
]);

const { rows, loading, totalRecords, search, filterOpen, pagination, load, onRequest } = useListTable({
  pageKey: "email-log",
  fetcher: ({ page, limit }) =>
    emailLogApi.list({
      page,
      limit,
      search: search.value || undefined,
      status: filters.status || undefined,
      opened: typeof filters.opened === "boolean" ? filters.opened : undefined
    }).then((r) => ({ data: r?.data, total: r?.meta?.totalRecords })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

const { filters, filterableColumns, filterChips, removeFilter, clearFilters } = useColumnFilters(columns, rows, { server: true });
const reload = debounce(() => { pagination.value.page = 1; load(); }, 300);
watch([search, filters], reload, { deep: true });

// ---- View one email ----
const viewOpen = ref(false);
const viewing = ref(null);
const body = ref("");
const loadingBody = ref(false);

const view = async (row) => {
  viewing.value = row;
  body.value = "";
  viewOpen.value = true;
  loadingBody.value = true;
  try {
    const detail = await emailLogApi.get(row.id);
    body.value = detail?.body || "";
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loadingBody.value = false;
  }
};
</script>

<style scoped>
.email-body {
  width: 100%;
  height: 45vh;
  border: 1px solid var(--q-primary);
  background: #fff;
}
</style>
