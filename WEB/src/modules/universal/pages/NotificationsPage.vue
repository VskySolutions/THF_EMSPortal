<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'Notifications' }]"
      :search="search"
      show-search
      search-placeholder="Search notifications"
      show-filters
      :filter-count="allChips.length"
      show-back
      @update:search="search = $event"
      @filters="filterOpen = true"
      @back="$router.back()"
    >
      <template #actions>
        <q-btn
          unelevated no-caps color="primary" icon="o_done_all" label="Mark all read"
          :disable="!rows.length" @click="markAllRead"
        />
        <q-btn
          outline no-caps color="primary" icon="o_tune" label="Preferences" class="q-ml-sm"
          :to="{ name: 'uf_notification_preferences' }"
        />
      </template>
    </app-list-header>

    <app-filter-drawer v-model="filterOpen" :chips="allChips" @remove="onRemoveFilter" @clear="onClearFilters">
      <app-column-filters v-model="filters" :columns="filterableColumns" />
      <!-- A received range has no column of its own: it is two controls, not one. -->
      <app-date-field v-model="extras.createdFrom" label="Received From" :dense="false" :max-date="extras.createdTo" />
      <app-date-field v-model="extras.createdTo" label="Received To" :dense="false" :min-date="extras.createdFrom" />
      <div v-if="invalidRange" class="text-caption text-negative">
        “Received From” is after “Received To” — no notification can match both.
      </div>
    </app-filter-drawer>

    <app-data-table
      page-key="uf_notifications"
      row-key="id"
      title="My notifications"
      :rows="rows"
      :columns="columns"
      :loading="loading"
      :total-records="totalRecords"
      :pagination="pagination"
      default-sort-by="createdOnUtc"
      @request="onRequest"
      @refresh="load"
    >
      <template #body-cell-type="cell">
        <q-td :props="cell">
          <q-icon :name="metaFor(cell.row.type).icon" :color="metaFor(cell.row.type).color" size="20px" class="q-mr-xs" />
          {{ metaFor(cell.row.type).label }}
        </q-td>
      </template>

      <!-- Unread rows carry the weight; the whole cell opens the record so the row reads as one target. -->
      <template #body-cell-notification="cell">
        <q-td :props="cell" class="cursor-pointer" @click="open(cell.row)">
          <div :class="cell.row.isRead ? '' : 'text-weight-medium'">{{ cell.row.title }}</div>
          <div class="text-grey-7 fs-12 ellipsis-2-lines">{{ cell.row.body }}</div>
        </q-td>
      </template>

      <template #body-cell-createdOnUtc="cell">
        <q-td :props="cell">{{ fmt.formatDateTime(cell.row.createdOnUtc) }}</q-td>
      </template>

      <template #body-cell-status="cell">
        <q-td :props="cell">
          <q-badge :color="cell.row.isRead ? 'grey-5' : 'primary'" :label="cell.row.isRead ? 'Read' : 'Unread'" />
        </q-td>
      </template>

      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <!-- Only offered where there is somewhere to go: a notification may carry no linked record. -->
          <q-btn
            v-if="cell.row.entityType && cell.row.entityId" type="a"
            flat round dense color="primary" icon="o_open_in_new" @click="open(cell.row)"
          >
            <q-tooltip>Open record</q-tooltip>
          </q-btn>
          <q-btn
            v-if="!cell.row.isRead" type="a" flat round dense color="primary" icon="o_mark_email_read"
            @click="markRead(cell.row)"
          >
            <q-tooltip>Mark read</q-tooltip>
          </q-btn>
        </q-td>
      </template>

      <template #no-data>
        <div class="full-width column flex-center q-pa-xl text-grey-6">
          <q-icon name="o_notifications_none" size="40px" class="q-mb-sm" />
          <div class="text-subtitle1 q-mb-xs">{{ allChips.length || search ? "No matching notifications" : "No notifications yet" }}</div>
          <div>
            {{ allChips.length || search
              ? "Try clearing the filters or the search."
              : "You'll be notified here when something needs your attention." }}
          </div>
        </div>
      </template>
    </app-data-table>
  </q-page>
</template>

<script setup>
import { reactive, computed, watch } from "vue";
import { useRouter } from "vue-router";
import { debounce } from "quasar";
import { ufNotificationApi, getApiErrorMessage } from "services/api";
import { useListTable } from "composables/useListTable";
import { useColumnFilters } from "composables/useColumnFilters";
import { useNotify } from "composables/useNotify";
import { useDateFormat } from "composables/useDateFormat";
import { useNotificationMeta } from "composables/uf/useNotificationMeta";
import { useNotificationRoute } from "composables/uf/useNotificationRoute";

import AppListHeader from "components/common/AppListHeader.vue";
import AppFilterDrawer from "components/common/AppFilterDrawer.vue";
import AppColumnFilters from "components/common/AppColumnFilters.vue";
import AppDateField from "components/common/AppDateField.vue";
import AppDataTable from "components/common/AppDataTable.vue";

const router = useRouter();
const notify = useNotify();
const fmt = useDateFormat();
const { metaFor, allTypes } = useNotificationMeta();
const { routeForNotification } = useNotificationRoute();

// Read/unread is a bool server-side but a filter value is always a string, so it is sent as one and
// parsed back on the way out — the same shape the REMS "form submitted" filter uses.
const READ_OPTIONS = [
  { label: "Unread", value: "false" },
  { label: "Read", value: "true" }
];

const columns = computed(() => [
  {
    name: "type",
    label: "Type",
    field: "type",
    align: "left",
    sortable: true,
    default: true,
    filterOptions: allTypes.map((t) => ({ label: t.label, value: t.value }))
  },
  { name: "notification", label: "Notification", field: "title", align: "left", sortable: true, default: true, filterable: false },
  { name: "createdOnUtc", label: "Received", field: "createdOnUtc", align: "left", sortable: true, default: true, filterable: false },
  { name: "status", label: "Status", field: "isRead", align: "left", sortable: true, default: true, filterOptions: READ_OPTIONS },
  { name: "actions", label: "Actions", field: "actions", align: "left" }
]);

// Server filters with no column to hang off.
const extras = reactive({ createdFrom: "", createdTo: "" });

const invalidRange = computed(() =>
  !!extras.createdFrom && !!extras.createdTo && extras.createdFrom > extras.createdTo);

const { rows, loading, totalRecords, search, filterOpen, pagination, load, onRequest } = useListTable({
  pageKey: "uf_notifications",
  // A notification is an event: Received is its default order, and it has no "updated" anything.
  defaultSortBy: "createdOnUtc",
  fetcher: ({ page, limit, sortBy, descending }) =>
    ufNotificationApi.list({
      page,
      limit,
      sortBy,
      descending,
      search: search.value || undefined,
      type: filters.type ?? undefined,
      isRead: filters.status == null ? undefined : filters.status === "true",
      // Date-only pickers read in the tenant's zone; CreatedOnUtc is a UTC instant. Both ends are
      // converted to that day's real boundaries, so "to" includes its own day.
      createdFrom: fmt.zonedDayBoundaryUtc(extras.createdFrom, "start"),
      createdTo: fmt.zonedDayBoundaryUtc(extras.createdTo, "end")
    }).then((r) => ({ data: r?.data, total: r?.meta?.totalRecords })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

const { filters, filterableColumns, filterChips, removeFilter, clearFilters } = useColumnFilters(columns, rows, { server: true });

const extraChips = [
  { key: "createdFrom", label: "Received From" },
  { key: "createdTo", label: "Received To" }
];
const allChips = computed(() => [
  ...filterChips.value,
  ...extraChips.filter((c) => extras[c.key]).map((c) => ({ key: c.key, label: `${c.label}: ${extras[c.key]}` }))
]);
const onRemoveFilter = (key) => {
  if (key in extras) extras[key] = "";
  else removeFilter(key);
};
const onClearFilters = () => {
  clearFilters();
  extraChips.forEach((c) => { extras[c.key] = ""; });
};

const reload = debounce(() => { pagination.value.page = 1; load(); }, 300);
watch([search, filters, extras], reload, { deep: true });

// Opening marks it read and follows the deep link. A notification with no linked record has nowhere to
// go, so it just settles as read — hence the reload rather than a navigation.
const open = async (row) => {
  if (!row.isRead) {
    try { await ufNotificationApi.markRead(row.id); } catch { /* the navigation matters more */ }
  }
  if (row.entityType && row.entityId) router.push(await routeForNotification(row));
  else load();
};

const markRead = async (row) => {
  try {
    await ufNotificationApi.markRead(row.id);
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// Marks every notification read, not just the filtered page — which is what the endpoint does, so the
// confirmation says so rather than implying the current view was the scope.
const markAllRead = async () => {
  try {
    await ufNotificationApi.markAllRead();
    notify.success("All notifications marked read.");
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};
</script>

<style scoped>
.ellipsis-2-lines { display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
</style>
