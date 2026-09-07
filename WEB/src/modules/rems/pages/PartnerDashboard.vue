<template>
  <q-page padding>
    <acting-as-banner />

    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'My Requests' }]"
      :search="search"
      show-search
      search-placeholder="Search client name"
      show-filters
      :filter-count="allChips.length"
      :show-add="canCreate"
      add-label="New REMS Request"
      show-back
      @update:search="search = $event"
      @filters="filterOpen = true"
      @add="openCreate"
      @back="$router.back()"
    />

    <app-filter-drawer v-model="filterOpen" :chips="allChips" @remove="onRemoveFilter" @clear="onClearFilters">
      <app-column-filters v-model="filters" :columns="filterableColumns" />
      <!-- Server filters with no column of their own: contact matches email or mobile at once, which no
           single column stands for, and a created range is two controls, not one. -->
      <app-text-field v-model="extras.contact" label="Contact (email or mobile)" clearable :dense="false" />
      <app-date-field v-model="extras.createdFrom" label="Created From" :dense="false" />
      <app-date-field v-model="extras.createdTo" label="Created To" :dense="false" />
      <div v-if="invalidRange" class="text-caption text-negative">
        “Created From” is after “Created To” — no request can match both.
      </div>
      <q-toggle
        v-if="canManageDeleted" v-model="showDeleted" label="Show deleted?" dense class="q-mt-md"
      />
    </app-filter-drawer>

    <app-data-table
      page-key="rems-partner"
      row-key="id"
      title="My REMS requests"
      :rows="rows"
      :columns="columns"
      :loading="loading"
      :total-records="totalRecords"
      :pagination="pagination"
      default-sort-by="updatedOnUtc"
      :pinned-row-keys="pinnedRowKeys"
      :row-colours="rowColours"
      @request="onRequest"
      @refresh="load"
    >
      <!-- The admins' second reading of this list, beside the column picker where EMS Review keeps the same
           pair. -->
      <template v-if="isRemsAdmin" #actions>
        <q-btn-toggle
          v-model="ownership"
          no-caps unelevated dense
          toggle-color="primary" color="grey-3" text-color="grey-8"
          :options="OWNERSHIP_FILTERS"
        />
      </template>

      <!-- The pin the reader put on this row, beside the number rather than only on the button that set it:
           the button is at the far right and the reason the row is at the top is here. -->
      <template #body-cell-remsNumber="cell">
        <q-td :props="cell">
          <entity-pinned-mark :pinned="isPinned(cell.row.id)" />
          {{ cell.row.remsNumber }}
        </q-td>
      </template>

      <!-- The particle after the name and in bold: a column of "John Smith" rows is told apart by the "Jr."
           and the "III" alone. -->
      <template #body-cell-clientName="cell">
        <q-td :props="cell">
          <app-name-with-suffix :name="cell.row.clientName" :suffix="cell.row.clientNameSuffix" />
        </q-td>
      </template>

      <!-- No icon per row — a column of them is noise — but the explanation is still a hover away. -->
      <template #body-cell-type="cell">
        <q-td :props="cell">
          {{ typeLabel(cell.row.type) }}
          <q-tooltip v-if="typeHint(cell.row.type)" max-width="320px" :delay="300">
            {{ typeHint(cell.row.type) }}
          </q-tooltip>
        </q-td>
      </template>

      <!-- The stage, and what that stage means — the tooltip is the status option's own Description,
           maintained in Administration → Option Sets. -->
      <template #body-cell-status="cell">
        <q-td :props="cell">
          <app-option-badge :option="requestStatusOption(cell.row)" />
        </q-td>
      </template>

      <template #body-cell-emsFormState="cell">
        <q-td :props="cell">
          <app-option-badge :option="formStatusOption(cell.row.emsFormState)" />
        </q-td>
      </template>

      <!-- What kind of entity the client is, as the badge Related Entities draws for the same value — the
           tenant's word for it in the tenant's colour, rather than the stored code. -->
      <template #body-cell-entityType="cell">
        <q-td :props="cell">
          <app-option-badge v-if="cell.row.entityType" :option="entityTypeOption(cell.row.entityType)" />
          <template v-else>—</template>
        </q-td>
      </template>

      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <!-- View and Edit are the same page in two modes. -->
          <q-btn flat round dense color="primary" icon="o_visibility" :to="viewRoute(cell.row)">
            <q-tooltip>View</q-tooltip>
          </q-btn>
          <q-btn
            v-if="cell.row.actions?.canEdit"
            flat round dense color="primary" icon="o_edit" :to="editRoute(cell.row)"
          >
            <q-tooltip>Edit</q-tooltip>
          </q-btn>
          <!-- What has been emailed to the client about this request, and the way to chase them again. -->
          <q-btn
            v-if="canReadEmailLog && emsFormActivity(cell.row)" type="a"
            flat round dense color="primary" icon="o_mark_email_read" @click="openEmailLog(cell.row)"
          >
            <q-tooltip>Email log</q-tooltip>
          </q-btn>
          <q-btn type="a" flat round dense color="primary" icon="o_forum" @click="openConversation(cell.row)">
            <q-tooltip>Conversation</q-tooltip>
          </q-btn>
          <!-- The reader's own marks on this row, sitting with the actions rather than apart from them:
               everything before them acts on the REQUEST, and these two are private to whoever is looking. -->
          <entity-row-marks
            v-if="canMarkRows"
            :pinned="isPinned(cell.row.id)"
            :colour="colourOf(cell.row.id)"
            :palette="markPalette"
            :limit-reached="pinLimitReached"
            :limit="MAX_PINS_PER_TYPE"
            :busy="markBusyId === cell.row.id"
            @toggle-pin="togglePin(cell.row.id, cell.row.remsNumber)"
            @set-colour="applyColour(cell.row.id, $event, cell.row.remsNumber)"
          />
        </q-td>
      </template>

      <template #no-data>
        <div class="full-width column flex-center q-pa-xl text-grey-6">
          <q-icon name="o_assignment" size="40px" class="q-mb-sm" />
          <div class="text-subtitle1 q-mb-xs">No REMS requests yet</div>
          <div class="q-mb-md">
            {{ isRemsAdmin && ownership === "mine"
              ? "Create your first request, or switch to All to see everybody else's."
              : "Create your first request to start onboarding a client." }}
          </div>
          <q-btn v-if="canCreate" unelevated no-caps color="primary" icon="o_add" label="New REMS Request" @click="openCreate" />
        </div>
      </template>
    </app-data-table>

    <deleted-records-panel
      v-if="canManageDeleted" :entity-type="EntityType.Rems" :show="showDeleted" @restored="load"
    />

    <conversation-dialog v-model="conversationOpen" :request-id="conversationId" :subtitle="conversationSubtitle" />

    <!-- A sent reminder adds an email event and nothing else, but the row's EMS State and Updated On are
         what the list shows of it, so it reloads on the way out. -->
    <email-log-dialog v-model="emailLogOpen" :rems-id="emailLogId" :subtitle="emailLogSubtitle" @sent="load" />
  </q-page>
</template>

<script setup>
import { ref, reactive, computed, watch, onMounted } from "vue";
import { debounce } from "quasar";
import { useRouter } from "vue-router";
import { remsApi, getApiErrorMessage, EntityType } from "services/api";
import { useAuthStore } from "stores/auth";
import { usePermissions, Permissions } from "composables/usePermissions";
import { useNotify } from "composables/useNotify";
import { useListTable } from "composables/useListTable";
import { useColumnFilters } from "composables/useColumnFilters";
import { useDeletedRecords } from "composables/useDeletedRecords";
import { useDateFormat } from "composables/useDateFormat";
import { useAuditColumns } from "composables/useAuditColumns";
import { useRowPersonalisation, MAX_PINS_PER_TYPE } from "composables/uf/useRowPersonalisation";
import { useRemsMeta } from "modules/rems/useRemsMeta";
import { REMS_STATUS } from "modules/rems/remsStatus";

import AppListHeader from "components/common/AppListHeader.vue";
import AppOptionBadge from "components/common/AppOptionBadge.vue";
import AppNameWithSuffix from "components/common/AppNameWithSuffix.vue";
import ActingAsBanner from "modules/rems/components/ActingAsBanner.vue";
import AppFilterDrawer from "components/common/AppFilterDrawer.vue";
import AppColumnFilters from "components/common/AppColumnFilters.vue";
import AppTextField from "components/common/AppTextField.vue";
import AppDateField from "components/common/AppDateField.vue";
import AppDataTable from "components/common/AppDataTable.vue";
import DeletedRecordsPanel from "components/universal/DeletedRecordsPanel.vue";
import EntityPinnedMark from "components/universal/EntityPinnedMark.vue";
import EntityRowMarks from "components/universal/EntityRowMarks.vue";
import ConversationDialog from "modules/rems/components/ConversationDialog.vue";
import EmailLogDialog from "modules/rems/components/EmailLogDialog.vue";

const router = useRouter();
const auth = useAuthStore();
const { showDeleted, canManageDeleted } = useDeletedRecords();
const notify = useNotify();
const { has } = usePermissions();
const fmt = useDateFormat();
const auditColumns = useAuditColumns();
// The *Option helpers hand back the whole value — label, description, colour, icon — which is what
// AppOptionBadge renders. submissionStateLabel is the label-only form.
const {
  typeLabel, typeHint, requestStatusOption, formStatusOption, submissionStateLabel, emsFormActivity,
  entityTypeLabel, entityTypeOption, statusFilterOptions, typeOptions
} = useRemsMeta();

const canCreate = computed(() => has(Permissions.RemsRequestsCreate));
const canReadEmailLog = computed(() => has(Permissions.RemsEmailLogRead));

// Who gets the two views.
const isRemsAdmin = computed(() =>
  auth.roles.includes("SuperAdmin") || auth.roles.includes("Admin"));

// Which of the two readings of this list is on screen.
const OWNERSHIP_FILTERS = [
  { label: "Created By Me", value: "mine" },
  { label: "All", value: "all" }
];
const ownership = ref("mine");

// The Assigned Admin filter needs the admin list, so it is offered only to callers who may read it —
// which is the same right as reading requests.
const canSeeAdmins = computed(() => has(Permissions.RemsRequestsRead));
const adminFilterOptions = ref([]);
onMounted(async () => {
  if (!canSeeAdmins.value) return;
  try {
    const admins = await remsApi.admins();
    adminFilterOptions.value = (admins || []).map((a) => ({ label: a.name, value: a.id }));
  } catch {
    // A filter nobody can populate simply stays empty; the list itself is unaffected, and the table's
    // own error toast is the one worth showing.
  }
});

// Ordered as the list reads: what the request is, then who it is for, then where it has got to, then the
// trail behind it.
const columns = computed(() => [
  { name: "remsNumber", label: "Request ID", field: "remsNumber", align: "left", sortable: true, default: true, filterable: false },
  { name: "type", label: "Type", field: "type", align: "left", default: true, filterOptions: typeOptions.value },
  { name: "clientName", label: "Client", field: "clientName", align: "left", sortable: true, default: true, filterable: false },
  { name: "status", label: "Status", field: "status", align: "left", sortable: true, default: true, filterOptions: statusFilterOptions.value },
  // Only offered to callers who may read the admin list; without it the picker would be empty, which
  // reads as "nobody is assigned" rather than "you cannot see who is".
  {
    name: "assignedAdmin",
    label: "Assigned Admin",
    // Says which of the two silences an empty cell is: nobody has taken the request yet, versus there
    // being no admin stage in sight because it is still with its initiator or the client.
    field: (r) => r.assignedAdmin?.name || (r.status === REMS_STATUS.ADMIN_REVIEW ? "Waiting for pickup" : "—"),
    align: "left",
    default: true,
    ...(canSeeAdmins.value ? { filterOptions: adminFilterOptions.value } : { filterable: false })
  },
  // On by default. The CSE is who to ask about a request, and every list that shows a request now says
  // so without the reader opening it.
  { name: "cse", label: "CSE", field: (r) => r.cse?.name || "—", align: "left", default: true, filterable: false },
  { name: "emsFormState", label: "EMS State", field: "emsFormState", align: "left", default: true, filterable: false },
  // Off by default, but every field the row carries is offered in the Columns menu rather than being
  // unreachable.
  { name: "customerEmail", label: "Client Email", field: (r) => r.customerEmail || "—", align: "left", default: false, filterable: false },
  { name: "customerMobileNumber", label: "Client Phone Number", field: (r) => r.customerMobileNumber || "—", align: "left", default: false, filterable: false },
  // The cell draws the badge; the field is the LABEL so the column reads as its wording rather than as
  // the stored code (`not_for_profit`) wherever the field is what is read.
  { name: "entityType", label: "Entity Type", field: (r) => entityTypeLabel(r.entityType), align: "left", default: false, filterable: false },
  { name: "clientSubmissionState", label: "Client Submission", field: (r) => submissionStateLabel(r.clientSubmissionState), align: "left", default: false, filterable: false },
  // All four from the shared set: Updated By / Updated On visible and last, the created pair a click
  // away. None is filterable — the created range is the From/To pair in the drawer, not a text box.
  ...auditColumns(),
  { name: "actions", label: "Actions", field: "actions", align: "left" }
]);

// Server filters with no column to hang off. Kept in one reactive object so the chips, the reset and
// the watcher below each have a single thing to read.
const extras = reactive({ contact: "", createdFrom: "", createdTo: "" });

const invalidRange = computed(() =>
  !!extras.createdFrom && !!extras.createdTo && extras.createdFrom > extras.createdTo);

const { rows, loading, totalRecords, search, filterOpen, pagination, load, onRequest } = useListTable({
  pageKey: "rems-partner",
  fetcher: ({ page, limit, sortBy, descending }) =>
    remsApi.list({
      sortBy,
      descending,
      scope: "partner",
      // Only the admins choose.
      ownership: isRemsAdmin.value ? ownership.value : "all",
      page,
      limit,
      clientName: search.value || undefined,
      status: filters.status || undefined,
      type: filters.type || undefined,
      assignedAdminUserId: filters.assignedAdmin || undefined,
      contact: extras.contact || undefined,
      // The pickers are date-only and read in the tenant's zone; the column they filter is a UTC
      // instant. Both ends are converted to that day's real boundaries, so "to" includes its own day.
      createdFrom: fmt.zonedDayBoundaryUtc(extras.createdFrom, "start"),
      createdTo: fmt.zonedDayBoundaryUtc(extras.createdTo, "end")
    }).then((r) => ({ data: r?.data, total: r?.meta?.totalRecords })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

const { filters, filterableColumns, filterChips, removeFilter, clearFilters } = useColumnFilters(columns, rows, { server: true });

// The column chips plus one per standalone filter, so everything narrowing the list is visible in the
// same place and removable the same way.
const extraChips = [
  { key: "contact", label: "Contact" },
  { key: "createdFrom", label: "Created From" },
  { key: "createdTo", label: "Created To" }
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
watch([search, filters, extras, ownership], reload, { deep: true });

// ---- The reader's own marks on these rows ----
// A pin floats a row to the top of the page and a colour tints.
const canMarkRows = computed(() => has(Permissions.RemsRequestsRead));

const {
  palette: markPalette, pinnedRowKeys, pinLimitReached, isPinned, togglePin,
  colours: rowColours, colourOf, applyColour, busyId: markBusyId, sync: syncMarks
} = useRowPersonalisation(EntityType.Rems);

// After every load, never per row: the colours come back for the whole page in one read, and the pins
// are the user's own small set, fetched once.
watch(rows, (list) => {
  if (canMarkRows.value) syncMarks(list.map((r) => r.id));
});

// Straight to the form.
const viewRoute = (row) => ({ name: "rems_request", params: { id: row.id } });
const editRoute = (row) => ({ name: "rems_request_edit", params: { id: row.id } });

// ---- Create ----
// The same page as Edit, on its own path.
const openCreate = () => {
  router.push({ name: "rems_request_new" });
};

// ---- Conversation ----
const conversationOpen = ref(false);
const conversationId = ref(null);
const conversationSubtitle = ref("");
const openConversation = (row) => {
  conversationId.value = row.id;
  // The client, not a title: a request has no title of its own any more — it is identified by who it is for.
  conversationSubtitle.value = rowLabel(row);
  conversationOpen.value = true;
};

// ---- Email log ----
// The client's side of the correspondence: every intake-form email sent for this request and what the
// provider reported back, with Send Reminder on it for a client who has not answered yet.
const emailLogOpen = ref(false);
const emailLogId = ref(null);
const emailLogSubtitle = ref("");
const openEmailLog = (row) => {
  emailLogId.value = row.id;
  emailLogSubtitle.value = rowLabel(row);
  emailLogOpen.value = true;
};

// How a request names itself in a dialog title bar.
const rowLabel = (row) => [row.remsNumber, row.clientName].filter(Boolean).join(" — ");
</script>
