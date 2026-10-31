<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'REMS Approvals' }]"
      :search="search"
      show-search
      search-placeholder="Search REMS number or client"
      show-filters
      :filter-count="filterChips.length"
      show-back
      @update:search="search = $event"
      @filters="filterOpen = true"
      @back="$router.back()"
    >
      <template #note>
        <template v-if="seesTenant">
          Every request of <strong>{{ tenantName }}</strong> that has been sent for approval — one row each,
          on the round it is on now, and nothing from any other tenant.
          <strong>Approval Status</strong> is where the whole request stands, and reads Approved only once
          every approver has signed; <strong>Approvers</strong> shows where each of them stands.
          Open a row to read the packet; a task is decided only by the approver who holds it.
          <template v-if="canScopeTenant">To read another tenant's approvals, pick it in the tenant selector.</template>
        </template>
        <template v-else>
          The requests routed to you to review — one row each.
          <strong>Approval Status</strong> is where the whole request stands, and reads Approved only once
          every approver has signed.
          <strong>Your Decision</strong> is your own signature on it.
          You only ever see your own tasks; a decision is final once made.
        </template>
      </template>
    </app-list-header>

    <!-- No chip row: the quick-filter bar under the table's title says how many filters are on and clears them. -->
    <app-filter-drawer
      v-model="filterOpen" :chips="filterChips" :show-chips="false" @remove="removeFilter" @clear="clearFilters"
    >
      <app-column-filters v-model="filters" :columns="filterableColumns" />
    </app-filter-drawer>

    <!-- Keyed on the TASK: on the tenant-wide inbox one request has a row per approver. -->
    <app-data-table
      :page-key="pageKey"
      row-key="taskId"
      :title="title"
      :rows="rows"
      :columns="columns"
      :loading="loading"
      :total-records="totalRecords"
      :pagination="pagination"
      default-sort-by="updatedOnUtc"
      @request="onRequest"
      @refresh="load"
      @row-click="(_, row) => openTask(row)"
    >
      <!-- Counted shortcuts over the two status filters: a click here IS the drawer's filter. -->
      <template #quick-filters>
        <quick-filter-bar :active-count="filterChips.length" @clear="clearFilters">
          <quick-filter-group
            v-model="filters.roundStatus" label="Approval Status" :options="roundQuick"
            :counts="quickCounts?.roundStatus"
          />
          <!-- A decision is one approver's; the tenant-wide inbox, read per request, has no such group. -->
          <quick-filter-group
            v-if="!seesTenant"
            v-model="filters.status" label="Your Decision" :options="decisionQuick" :counts="quickCounts?.status"
          />
        </quick-filter-bar>
      </template>

      <!-- Flagged only where it says something. -->
      <template #body-cell-remsNumber="cell">
        <q-td :props="cell">
          <div class="text-weight-medium">{{ cell.row.remsNumber || "—" }}</div>
          <div v-if="cell.row.roundNumber > 1" class="text-caption text-orange-9">Resubmitted</div>
        </q-td>
      </template>

      <!-- Where the REQUEST's approval stands — not where the reader's own signature does. -->
      <!-- The particle after the name and in bold: on an approver's inbox the name is how a request is
           recognised, and two clients called John Smith differ by nothing else. -->
      <template #body-cell-client="cell">
        <q-td :props="cell">
          <app-name-with-suffix :name="cell.row.clientName" :suffix="cell.row.clientNameSuffix" />
        </q-td>
      </template>

      <template #body-cell-roundStatus="cell">
        <q-td :props="cell">
          <app-option-badge :option="roundMeta(cell.row)" />
        </q-td>
      </template>

      <!-- The reader's OWN decision, named as theirs. -->
      <template #body-cell-status="cell">
        <q-td :props="cell">
          <app-option-badge :option="approvalStatusOption(cell.row.status)" />
        </q-td>
      </template>

      <!-- Where each approver on the round stands — the tenant-wide inbox, read per request. -->
      <template #body-cell-approvers="cell">
        <q-td :props="cell">
          <div class="row items-center q-gutter-xs">
            <q-chip
              v-for="a in cell.row.approvers || []" :key="a.taskId" dense square size="sm"
              :style="decisionStyle(a.status)" :icon="approvalStatusOption(a.status).icon || undefined"
            >
              {{ a.approver?.name || "—" }}
              <q-tooltip :delay="300">{{ approverHint(a) }}</q-tooltip>
            </q-chip>
          </div>
        </q-td>
      </template>

      <!-- How far the whole ROUND has got, not just this task. -->
      <template #body-cell-approvals="cell">
        <q-td :props="cell">
          <div class="row items-center no-wrap">
            <!-- Grey until somebody has actually approved: green on a 0 reads as progress. -->
            <q-badge :color="cell.row.approvedCount ? 'positive' : 'grey-6'">
              <q-icon name="o_check" size="12px" class="q-mr-xs" />{{ cell.row.approvedCount || 0 }}
            </q-badge>
            <q-badge v-if="cell.row.rejectedCount" color="negative" class="q-ml-xs">
              <q-icon name="o_close" size="12px" class="q-mr-xs" />{{ cell.row.rejectedCount }}
            </q-badge>
            <span class="text-caption text-grey-7 q-ml-sm">of {{ cell.row.approverCount || 0 }}</span>
          </div>
          <q-tooltip>{{ progressHint(cell.row) }}</q-tooltip>
        </q-td>
      </template>

      <template #body-cell-sentOnUtc="cell">
        <q-td :props="cell">{{ fmt.formatDateTime(cell.row.sentOnUtc) }}</q-td>
      </template>

      <!-- Whether the approved client has reached Maconomy. Blank until the request is approved and has a
           brand-new client to post. Once it has, the cell is the customer number alone — what somebody
           will type into Maconomy — with when it got there on the tooltip; the badge is kept for the
           states on the way. -->
      <template #body-cell-maconomySync="cell">
        <q-td :props="cell">
          <q-badge v-if="syncedNumber(cell.row)" color="positive" text-color="white" class="text-weight-medium">
            <q-icon name="o_cloud_done" size="13px" class="q-mr-xs" />{{ syncedNumber(cell.row) }}
            <q-tooltip :delay="300">{{ syncedTooltip(cell.row) }}</q-tooltip>
          </q-badge>
          <maconomy-sync-chip v-else-if="cell.row.maconomySync" :sync="cell.row.maconomySync" />
          <template v-else>—</template>
        </q-td>
      </template>

      <!-- Whether the approved engagement has become a Maconomy job: the job number once it has, the state
           on the way otherwise. Blank until the request is approved. -->
      <template #body-cell-maconomyJobSync="cell">
        <q-td :props="cell">
          <maconomy-sync-chip v-if="cell.row.maconomyJobSync" kind="job" :sync="cell.row.maconomyJobSync" />
          <template v-else>—</template>
        </q-td>
      </template>

      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <!-- A LINK, not a click handler: the task is a place, so the button is written as a route and
               renders as a real <a href> — which is what makes middle-click and "open in new tab" work. -->
          <q-btn flat round dense color="primary" icon="o_visibility" :to="taskRoute(cell.row)">
            <q-tooltip>{{ seesTenant ? "Open" : "Open for review" }}</q-tooltip>
          </q-btn>
          <q-btn type="a" flat round dense color="primary" icon="o_forum" @click.stop="openConversation(cell.row)">
            <q-tooltip>Conversation</q-tooltip>
          </q-btn>
          <!-- Sync now: post the approved client to Maconomy. Offered once the request is approved, while
               the client is not in Maconomy yet or the last attempt failed, and only with the sync on. -->
          <q-btn
            v-if="cell.row.maconomySync?.canSyncNow" type="a" flat round dense color="primary" icon="o_cloud_upload"
            :loading="syncingRems === cell.row.remsId" :disable="!!syncingRems" @click.stop="syncNow(cell.row)"
          >
            <q-tooltip>Sync now — post this client to Maconomy</q-tooltip>
          </q-btn>
          <!-- Sync job now: post the approved engagement to Maconomy as a job. Offered only while the job
               sync is on and the request is approved without a job; blocked, with the reason, until the
               client is in Maconomy and a template job resolves. -->
          <q-btn
            v-if="cell.row.maconomyJobSync?.canSyncNow || cell.row.maconomyJobSync?.blockedReason" type="a"
            flat round dense color="primary" icon="o_work"
            :loading="syncingJobRems === cell.row.remsId" :disable="!cell.row.maconomyJobSync?.canSyncNow || !!syncingJobRems"
            @click.stop="syncJobNow(cell.row)"
          >
            <q-tooltip max-width="320px">
              {{ cell.row.maconomyJobSync?.canSyncNow ? "Sync job now — create this engagement's job in Maconomy" : cell.row.maconomyJobSync?.blockedReason }}
            </q-tooltip>
          </q-btn>
        </q-td>
      </template>

      <template #no-data>
        <div class="full-width column flex-center q-pa-xl text-grey-6">
          <q-icon name="o_task_alt" size="40px" class="q-mb-sm" />
          <div class="text-subtitle1 q-mb-xs">No approvals</div>
          <div v-if="seesTenant">No request in this tenant has been routed for approval yet.</div>
          <div v-else>You have no requests awaiting your review right now.</div>
        </div>
      </template>
    </app-data-table>

    <conversation-dialog v-model="conversationOpen" :request-id="conversationId" :subtitle="conversationSubtitle" />
  </q-page>
</template>

<script setup>
// The task-isolated REMS Approval Inbox (WO-117 Part B, AC-REMS-019): the REQUESTS routed to the caller,
// one row each. A Tenant Admin or Super Admin reads the whole tenant instead — one row per approver on
// the round each request is on now, with an Approver column and filter.
import { ref, computed, watch, toRef, onMounted } from "vue";
import { debounce } from "quasar";
import { useRouter } from "vue-router";
import { remsApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useListTable } from "composables/useListTable";
import { useColumnFilters } from "composables/useColumnFilters";
import { useFilterMemory } from "composables/useFilterMemory";
import { useDateFormat } from "composables/useDateFormat";
import { useAuditColumns } from "composables/useAuditColumns";
import { useRemsMeta, REMS_ROUND_PARTIALLY_APPROVED } from "modules/rems/useRemsMeta";
import { useAuthStore } from "stores/auth";
import { useTenantStore } from "stores/tenant";
import { useTenantScope } from "composables/useTenantScope";

import AppListHeader from "components/common/AppListHeader.vue";
import AppOptionBadge from "components/common/AppOptionBadge.vue";
import AppNameWithSuffix from "components/common/AppNameWithSuffix.vue";
import AppFilterDrawer from "components/common/AppFilterDrawer.vue";
import AppColumnFilters from "components/common/AppColumnFilters.vue";
import AppDataTable from "components/common/AppDataTable.vue";
import QuickFilterGroup from "components/common/QuickFilterGroup.vue";
import QuickFilterBar from "components/common/QuickFilterBar.vue";
import ConversationDialog from "modules/rems/components/ConversationDialog.vue";
import MaconomySyncChip from "modules/rems/components/MaconomySyncChip.vue";
import { maconomySyncLabel, maconomyJobSyncLabel } from "modules/rems/maconomySync";
import { useMaconomyJobSync } from "modules/rems/useMaconomyJobSync";

const router = useRouter();
const notify = useNotify();
const fmt = useDateFormat();
const auditColumns = useAuditColumns();
const auth = useAuthStore();
const {
  approvalStatusOption, approvalStatusFilterOptions, roundStatusOption, roundStatusFilterOptions, approverRoleLabel
} = useRemsMeta();

// Whose inbox this is: a Tenant Admin or Super Admin reads every approver's tasks in the tenant, and the
// API scopes the list the same way from the roles on the token.
const seesTenant = computed(() => auth.roles.includes("SuperAdmin") || auth.roles.includes("TenantAdmin"));
// The tenant the rows belong to, named in the title so the scope is never in doubt: a Tenant Admin's own
// tenant, or the one a Super Admin has picked in the tenant selector (the API follows the same pick).
const tenantStore = useTenantStore();
const { canScopeTenant, isScoped, scopedTenantName } = useTenantScope();
const tenantName = computed(() =>
  (isScoped.value && scopedTenantName.value) || tenantStore.activeTenant?.name || "your tenant");
const title = computed(() => (seesTenant.value ? `Approvals · ${tenantName.value}` : "My Approvals"));
// The two views keep separate column and page-size preferences: a column list saved on the personal inbox
// would otherwise hide the Approver column the tenant-wide one adds.
const pageKey = seesTenant.value ? "rems-approvals-tenant" : "rems-approvals";

// Where the whole ROUND stands, from the counts every row carries — the REMS.ApprovalRoundStatus value,
// which is "Partially Approved" while some but not all approvers have signed.
const roundMeta = (row) =>
  roundStatusOption(row?.roundStatus, row?.approvedCount || 0, row?.approverCount || 0);

// ---- Maconomy ----
// Declared BEFORE the columns: a column's field function reads these the moment the table is set up.
// The customer number once the client is in Maconomy (created or linked), else null.
const syncedNumber = (row) => {
  const s = row?.maconomySync;
  return s && (s.status === "synced" || s.status === "linked") && s.customerNumber ? s.customerNumber : null;
};
const syncedTooltip = (row) => {
  const when = row?.maconomySync?.syncedOnUtc;
  return when ? `Synced on ${fmt.formatDateTime(when)}` : "Synced";
};

// The CSE filter offers the CSEs on the caller's inbox: an approver may hold no REMS permission, so the
// tenant's CSE directory is not theirs to read.
const cseFilterOptions = ref([]);
// The Client filter likewise offers the clients on the inbox — any of them at once.
const clientFilterOptions = ref([]);
// And the Approvers filter the approvers on it — a tenant-wide inbox only, since on their own it is them —
// which narrows to the requests one approver is on.
const approverFilterOptions = ref([]);
const toOptions = (rows) => (rows || []).map((u) => ({ label: u.name, value: u.id }));
onMounted(async () => {
  try {
    const [cses, clients, approvers] = await Promise.all([
      remsApi.myApprovalCses(),
      remsApi.myApprovalClients(),
      seesTenant.value ? remsApi.myApprovalApprovers() : Promise.resolve([])
    ]);
    cseFilterOptions.value = toOptions(cses);
    clientFilterOptions.value = toOptions(clients);
    approverFilterOptions.value = toOptions(approvers);
  } catch {
    // A filter nobody can populate simply stays empty; the list itself is unaffected.
  }
});

// The REMS number is covered by the quick search, so it gets no duplicate filter box; the client is a
// dropdown of the clients on the inbox, and every date column filters as a From/To range. Approvals is a
// count read off the round, which the Approval Status filter already stands for.
// Computed: the CSE and client options arrive after the page does.
const columns = computed(() => [
  { name: "remsNumber", label: "Request ID", field: "remsNumber", align: "left", sortable: true, default: true, filterable: false },
  { name: "client", label: "Client", field: "clientName", align: "left", sortable: true, default: true, filterOptions: clientFilterOptions.value, filterMultiple: true },
  // On by default: an approver deciding on a round needs to know who to ask about it, and the CSE is that
  // person.
  { name: "cse", label: "CSE", field: (r) => r.cse?.name || "—", align: "left", sortable: true, default: true, filterOptions: cseFilterOptions.value },
  // Whether the approved client has reached Maconomy; Sync now sits in the Actions column beside it.
  { name: "maconomySync", label: "Maconomy", field: (r) => syncedNumber(r) || maconomySyncLabel(r.maconomySync?.status), align: "left", sortable: false, default: true, filterable: false },
  // The engagement as a Maconomy job, once approved; Sync job now sits in the Actions column beside it.
  { name: "maconomyJobSync", label: "Maconomy job", field: (r) => r.maconomyJobSync?.jobNumber || maconomyJobSyncLabel(r.maconomyJobSync?.status), align: "left", sortable: false, default: true, filterable: false },
  // The REQUEST's approval, shown by default — it is the answer to "where does this stand?", which the
  // reader's own decision below is not.
  {
    name: "roundStatus",
    label: "Approval Status",
    field: (r) => roundMeta(r).label,
    align: "left",
    sortable: true,
    default: true,
    filterOptions: roundStatusFilterOptions.value
  },
  // Everyone on the round and where they stand — the tenant-wide inbox, whose row is a request. Not
  // sortable: a row has several.
  ...(seesTenant.value
    ? [{ name: "approvers", label: "Approvers", field: (r) => (r.approvers || []).map((a) => a.approver?.name).filter(Boolean).join(", "), align: "left", sortable: false, default: true, filterOptions: approverFilterOptions.value }]
    : []),
  // Whose signature, said in the heading — an approver's own inbox only; a request row has no decision.
  ...(seesTenant.value
    ? []
    : [{ name: "status", label: "Your Decision", field: "status", align: "left", sortable: true, default: true, filterOptions: approvalStatusFilterOptions.value }]),
  // Not sortable: how much of the round is outstanding is counted from the tasks loaded with each row.
  {
    name: "approvals",
    label: "Approvals",
    field: (r) => (r.approverCount || 0) - (r.approvedCount || 0),
    align: "left",
    sortable: true,
    default: true,
    filterable: false
  },
  { name: "sentOnUtc", label: "Sent", field: "sentOnUtc", align: "left", sortable: true, default: true, filterType: "dateRange" },
  // Off by default, but offered in the Columns menu so nothing the row returns is unreachable.
  // On a request row this is when its round closed, which the server cannot order by.
  { name: "decidedOnUtc", label: "Decided", field: (r) => (r.decidedOnUtc ? fmt.formatDateTime(r.decidedOnUtc) : "—"), align: "left", sortable: !seesTenant.value, default: false, filterType: "dateRange" },
  ...auditColumns({ dateRanges: true }),
  { name: "actions", label: "Actions", field: "actions", align: "left" }
]);

// Paged and filtered SERVER-side, like every other REMS list: loading an approver's whole history and
// searching it in the browser stops scaling.
// Everything the list is narrowed by, in the shape the API takes. Shared with the counts so the two can
// never drift.
const listFilters = () => {
  // The pickers are date-only and read in the tenant's zone; each column is a UTC instant, so both ends
  // become that day's real boundaries and "to" includes its own day.
  const sent = rangeBounds("sentOnUtc", fmt.zonedDayBoundaryUtc);
  const decided = rangeBounds("decidedOnUtc", fmt.zonedDayBoundaryUtc);
  const created = rangeBounds("createdOnUtc", fmt.zonedDayBoundaryUtc);
  const updated = rangeBounds("updatedOnUtc", fmt.zonedDayBoundaryUtc);
  return {
    search: search.value || undefined,
    status: seesTenant.value ? undefined : filters.status || undefined,
    roundStatus: filters.roundStatus || undefined,
    cseUserId: filters.cse || undefined,
    approverUserId: filters.approvers || undefined,
    clientPersonIds: filters.client?.length ? filters.client : undefined,
    sentFrom: sent.from,
    sentTo: sent.to,
    decidedFrom: decided.from,
    decidedTo: decided.to,
    createdFrom: created.from,
    createdTo: created.to,
    updatedFrom: updated.from,
    updatedTo: updated.to
  };
};

// ---- The counted shortcuts ----
const quickCounts = ref(null);
const refreshQuickCounts = async () => {
  try {
    quickCounts.value = await remsApi.myApprovalQuickCounts(listFilters());
  } catch {
    // The buttons still work without their numbers; the list's own error toast is the one worth showing.
  }
};
const pickOptions = (options, values) => values.map((v) => options.find((o) => o.value === v)).filter(Boolean);
const roundQuick = computed(() => pickOptions(
  roundStatusFilterOptions.value, ["Pending", REMS_ROUND_PARTIALLY_APPROVED, "Approved", "Rejected"]));
const decisionQuick = computed(() => pickOptions(approvalStatusFilterOptions.value, ["Pending", "Approved"]));

const { rows, loading, totalRecords, search, filterOpen, pagination, load, onRequest } = useListTable({
  pageKey,
  fetcher: ({ page, limit, sortBy, descending }) => {
    // The counts ride along with every read of the list, so the buttons describe the list on screen.
    refreshQuickCounts();
    return remsApi.myApprovalTasks({ ...listFilters(), page, limit, sortBy, descending })
      .then((r) => ({ data: r?.data, total: r?.meta?.totalRecords }));
  },
  onError: (err) => notify.error(getApiErrorMessage(err))
});

const {
  filters, filterableColumns, filterChips, removeFilter, clearFilters, rangeBounds
} = useColumnFilters(columns, rows, { server: true });
useFilterMemory(pageKey, seesTenant.value
  ? { roundStatus: toRef(filters, "roundStatus") }
  : { roundStatus: toRef(filters, "roundStatus"), status: toRef(filters, "status") });
const reload = debounce(() => { pagination.value.page = 1; load(); }, 300);
watch([search, filters], reload, { deep: true });

// Round progress. A rejection ENDS the round, so the undecided tasks never decide — "awaiting" is only
// meaningful while the round is still open, and the counts must not imply otherwise.
const pendingOf = (row) => Math.max(0, (row.approverCount || 0) - (row.approvedCount || 0) - (row.rejectedCount || 0));

const progressHint = (row) => {
  const parts = [`${row.approvedCount || 0} of ${row.approverCount || 0} approved`];
  if (row.rejectedCount > 0) parts.push(`${row.rejectedCount} rejected — no further approvals needed`);
  else if (pendingOf(row) > 0) parts.push(`${pendingOf(row)} still to decide`);
  return parts.join(" · ");
};

// One approver's chip on a request row: the decision's own colours, the role and the date on the tooltip.
const decisionStyle = (status) => {
  const option = approvalStatusOption(status);
  return { backgroundColor: option.backgroundColor || "#9e9e9e", color: option.textColor || "#ffffff" };
};
const approverHint = (a) => {
  const decided = a.decidedOnUtc ? ` on ${fmt.formatDateTime(a.decidedOnUtc)}` : "";
  return `${approverRoleLabel(a.role)} — ${approvalStatusOption(a.status).label}${decided}`;
};

const taskRoute = (row) => ({ name: "rems_approval_task", params: { taskId: row.taskId } });
// Still a push for the ROW click, which has nowhere to hang an href.
const openTask = (row) => router.push(taskRoute(row));

// ---- Sync now ----
// One attempt, inline. A failed post is a result with a reason, not an error; only a refusal (sync off,
// not eligible, already running) throws. The list is re-read either way, so the row says what happened.
const syncingRems = ref(null);
const syncNow = async (row) => {
  syncingRems.value = row.remsId;
  try {
    const result = await remsApi.maconomySyncNow(row.remsId);
    if (result?.status === "failed") {
      notify.error(`${row.remsNumber}: Maconomy sync failed. ${result.failureReason || ""}`.trim());
    } else if (result?.status === "linked") {
      notify.success(`${row.remsNumber}: linked to the existing Maconomy customer ${result.customerNumber}.`);
    } else {
      notify.success(`${row.remsNumber}: Maconomy customer ${result?.customerNumber} created.`);
    }
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    syncingRems.value = null;
    load();
  }
};

// Sync job now: the engagement posted to Maconomy as a job, with a confirmation naming what is sent.
const { syncingJobRems, syncJobNow } = useMaconomyJobSync({ afterwards: () => load() });

// ---- Conversation ----
// The REQUEST's thread, the same one the task detail shows under the checklist — so an approver can raise
// a question from the inbox and have the partner and CSE see it, without opening the task first.
const conversationOpen = ref(false);
const conversationId = ref(null);
const conversationSubtitle = ref("");
const openConversation = (row) => {
  conversationId.value = row.remsId;
  conversationSubtitle.value = `${row.remsNumber} — ${row.clientName || ""}`.trim();
  conversationOpen.value = true;
};
</script>
