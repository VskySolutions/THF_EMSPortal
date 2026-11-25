<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'Background Jobs' }]"
      show-back
      @back="$router.back()"
    />

    <q-tabs v-model="state" dense no-caps align="left" active-color="primary" indicator-color="primary" class="text-grey-8 q-mb-sm">
      <q-tab v-for="t in tabs" :key="t.value" :name="t.value">
        <div class="row items-center no-wrap">
          {{ t.label }}
          <q-badge v-if="counts[t.key]" rounded :color="t.value === 'Failed' ? 'negative' : 'grey-6'" class="q-ml-xs" :label="counts[t.key]" />
        </div>
      </q-tab>
    </q-tabs>

    <app-data-table
      page-key="background-jobs"
      row-key="id"
      :title="title"
      :rows="rows"
      :columns="columns"
      :loading="loading"
      :total-records="totalRecords"
      :pagination="pagination"
      @request="onRequest"
      @refresh="refresh"
    >
      <template #actions>
        <q-btn
          v-if="state === 'Failed'"
          outline no-caps color="primary" icon="o_replay" label="Retry all failed"
          :disable="!counts.failed" :loading="working" @click="retryFailed"
        />
        <q-btn
          v-if="state === 'Enqueued' || state === 'Scheduled'"
          outline no-caps color="negative" icon="o_delete_sweep" label="Clear queue"
          :disable="!waiting" :loading="working" @click="clearQueue"
        />
      </template>

      <template #body-cell-name="cell">
        <q-td :props="cell">
          <div class="text-weight-medium">{{ cell.row.name }}</div>
          <div v-if="cell.row.detail" class="text-caption text-grey-7 ellipsis job-detail">{{ cell.row.detail }}</div>
        </q-td>
      </template>

      <template #body-cell-reason="cell">
        <q-td :props="cell">
          <div class="ellipsis job-detail">
            {{ cell.row.reason }}
            <q-tooltip v-if="cell.row.reason" max-width="480px">{{ cell.row.reason }}</q-tooltip>
          </div>
        </q-td>
      </template>

      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <q-btn v-if="canRetry" flat round dense color="primary" icon="o_replay" @click="retry(cell.row)">
            <q-tooltip>Retry</q-tooltip>
          </q-btn>
          <q-btn v-if="canDelete" flat round dense color="negative" icon="o_delete" @click="remove(cell.row)">
            <q-tooltip>Delete</q-tooltip>
          </q-btn>
        </q-td>
      </template>
    </app-data-table>

    <q-card flat bordered class="q-mt-md">
      <q-card-section class="q-pb-none">
        <div class="text-subtitle1 text-weight-medium">Recurring jobs</div>
        <div class="text-caption text-grey-7">They run on a timer for every site.</div>
      </q-card-section>
      <q-card-section>
        <q-markup-table flat dense wrap-cells>
          <thead>
            <tr>
              <th class="text-left">Job</th>
              <th class="text-left">Last run</th>
              <th class="text-left">Last result</th>
              <th class="text-left">Next run</th>
              <th class="text-left">Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="job in recurring" :key="job.id">
              <td>{{ job.name }}</td>
              <td>{{ job.lastRunUtc ? formatDateTime(job.lastRunUtc) : "Never" }}</td>
              <td>
                <q-badge v-if="job.lastState" :color="job.lastState === 'Succeeded' ? 'positive' : 'negative'">{{ job.lastState }}</q-badge>
                <span v-if="job.error" class="text-negative q-ml-xs">{{ job.error }}</span>
              </td>
              <td>{{ job.nextRunUtc ? formatDateTime(job.nextRunUtc) : "—" }}</td>
              <td>
                <q-btn flat round dense color="primary" icon="o_play_arrow" @click="runNow(job)">
                  <q-tooltip>Run now</q-tooltip>
                </q-btn>
              </td>
            </tr>
            <tr v-if="!recurring.length">
              <td colspan="5" class="text-grey-7">No recurring jobs.</td>
            </tr>
          </tbody>
        </q-markup-table>
      </q-card-section>
    </q-card>
  </q-page>
</template>

<script setup>
import { ref, reactive, computed, watch, onMounted } from "vue";
import { backgroundJobsApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useListTable } from "composables/useListTable";
import { useDateFormat } from "composables/useDateFormat";

import AppDataTable from "components/common/AppDataTable.vue";
import AppListHeader from "components/common/AppListHeader.vue";

const notify = useNotify();
const { confirm } = useConfirm();
const { formatDateTime } = useDateFormat();

// `value` is the job queue's own name for the state; `key` is its field in the counts.
const tabs = [
  { value: "Enqueued", key: "queued", label: "Queued", when: "Queued on" },
  { value: "Scheduled", key: "scheduled", label: "Scheduled", when: "Runs on" },
  { value: "Processing", key: "processing", label: "Processing", when: "Started on" },
  { value: "Succeeded", key: "succeeded", label: "Succeeded", when: "Finished on" },
  { value: "Failed", key: "failed", label: "Failed", when: "Failed on" },
  { value: "Deleted", key: "deleted", label: "Deleted", when: "Deleted on" }
];
const state = ref("Enqueued");
const tab = computed(() => tabs.find((t) => t.value === state.value));
const title = computed(() => `${tab.value.label} jobs`);

const counts = reactive({ queued: 0, scheduled: 0, processing: 0, succeeded: 0, failed: 0, deleted: 0 });
const waiting = computed(() => counts.queued + counts.scheduled);

const canRetry = computed(() => ["Failed", "Deleted"].includes(state.value));
const canDelete = computed(() => ["Enqueued", "Scheduled", "Failed"].includes(state.value));

// As the queue orders them; nothing here is sortable.
const columns = computed(() => [
  { name: "id", label: "Job ID", field: "id", align: "left", default: true },
  { name: "name", label: "Job", field: "name", align: "left", default: true },
  { name: "tenantName", label: "Site", field: (r) => r.tenantName || "System", align: "left", default: true },
  { name: "atUtc", label: tab.value.when, field: (r) => (r.atUtc ? formatDateTime(r.atUtc) : ""), align: "left", default: true },
  ...(state.value === "Failed" ? [{ name: "reason", label: "Reason", field: "reason", align: "left", default: true }] : []),
  ...(canRetry.value || canDelete.value ? [{ name: "actions", label: "Actions", field: "actions", align: "left" }] : [])
]);

const { rows, loading, totalRecords, pagination, load, onRequest } = useListTable({
  pageKey: "background-jobs",
  defaultSortBy: null,
  fetcher: ({ page, limit }) =>
    backgroundJobsApi.list({ state: state.value, page, limit })
      .then((r) => ({ data: r?.data, total: r?.meta?.totalRecords })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

const recurring = ref([]);
const loadSummary = async () => {
  try {
    const [c, r] = await Promise.all([backgroundJobsApi.counts(), backgroundJobsApi.recurring()]);
    Object.assign(counts, c || {});
    recurring.value = r || [];
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};
const refresh = () => { load(); loadSummary(); };

watch(state, () => { pagination.value.page = 1; load(); });
onMounted(loadSummary);

// ---- Actions ----
const working = ref(false);
const act = async (call) => {
  working.value = true;
  try {
    const message = await call();
    notify.success(message);
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    working.value = false;
    refresh();
  }
};

// A job that got part-way before failing does that part again.
const RETRY_NOTE = "Anything it already did is done again: an email that did go out is sent a second time.";

const retry = async (row) => {
  if (!await confirm({ title: "Retry this job?", message: `${row.name} is put back in the queue. ${RETRY_NOTE}`, confirmLabel: "Retry" })) return;
  act(() => backgroundJobsApi.retry(row.id).then(() => "Job queued again."));
};

const remove = async (row) => {
  if (!await confirm({ title: "Delete this job?", message: `${row.name} will not run. It stays under Deleted for a while, where it can be retried.`, confirmLabel: "Delete", type: "danger" })) return;
  act(() => backgroundJobsApi.remove(row.id).then(() => "Job deleted."));
};

const retryFailed = async () => {
  if (!await confirm({ title: "Retry all failed jobs?", message: `${counts.failed} failed job(s) are put back in the queue. ${RETRY_NOTE}`, confirmLabel: "Retry all" })) return;
  act(() => backgroundJobsApi.retryFailed().then((r) => `${r?.count ?? 0} job(s) queued again.`));
};

const clearQueue = async () => {
  if (!await confirm({
    title: "Clear the queue?",
    message: `${waiting.value} queued and scheduled job(s) will not run: emails waiting to go out are not sent. Failed jobs are left alone.`,
    confirmLabel: "Clear queue",
    type: "danger"
  })) return;
  act(() => backgroundJobsApi.clearQueue().then((r) => `${r?.count ?? 0} job(s) removed from the queue.`));
};

const runNow = async (job) => {
  if (!await confirm({ title: "Run this job now?", message: `${job.name} runs once straight away, for every site.`, confirmLabel: "Run now" })) return;
  act(() => backgroundJobsApi.runRecurring(job.id).then(() => "Job started."));
};
</script>

<style scoped>
.job-detail {
  max-width: 46vw;
}
</style>
