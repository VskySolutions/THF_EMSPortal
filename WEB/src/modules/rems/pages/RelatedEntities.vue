<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'Related Entities' }]"
      :search="search"
      show-search
      search-placeholder="Search REMS number, client or related client"
      show-filters
      :filter-count="allChips.length"
      show-back
      @update:search="search = $event"
      @filters="filterOpen = true"
      @back="$router.back()"
    />

    <app-filter-drawer v-model="filterOpen" :chips="allChips" @remove="onRemoveFilter" @clear="onClearFilters">
      <app-column-filters v-model="filters" :columns="filterableColumns" />
      <!-- A server filter with no column of its own: the statuses live INSIDE the nested cell, one per
           related client, so there is no column for the drawer to hang a picker off. -->
      <app-select
        v-model="extras.relatedStatus"
        label="Related Client Status"
        :options="relatedEntityStatusOptions"
        clearable
        :dense="false"
      />
    </app-filter-drawer>

    <div class="text-body2 text-grey-8 q-mb-md">
      Submitted client forms that named somebody <strong>alongside</strong> the client — a spouse, a child
      or another individual on an individual's return, and the other businesses every other entity type
      declared. <strong>Parent &amp; Related Clients</strong> is the resulting linking; the status on each
      row is kept <strong>by hand</strong>, by whoever is chasing it. Nothing in the workflow moves it.
    </div>

    <app-data-table
      page-key="rems-related-entities"
      row-key="remsId"
      title="Related entities"
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
      <!-- The number opens the parent request, which is where the client's own answers are. -->
      <template #body-cell-remsNumber="cell">
        <q-td :props="cell">
          <!-- The pin the reader put on this row, beside the number rather than only on the button that set
               it: the button is at the far right and the reason the row is at the top is here. -->
          <entity-pinned-mark :pinned="isPinned(cell.row.remsId)" />
          <q-btn
            flat dense no-caps color="primary" class="text-weight-medium"
            :label="cell.row.remsNumber" :to="requestRoute(cell.row)"
          >
            <q-tooltip>Open the request</q-tooltip>
          </q-btn>
        </q-td>
      </template>

      <!-- Name over email. -->
      <template #body-cell-clientName="cell">
        <q-td :props="cell">
          <div class="text-weight-medium">
            <app-name-with-suffix :name="cell.row.clientName" :suffix="cell.row.clientNameSuffix" />
          </div>
          <div v-if="cell.row.clientEmail" class="text-caption text-primary ellipsis">
            {{ cell.row.clientEmail }}
          </div>
        </q-td>
      </template>

      <!-- A badge, not a word: the entity type is what decided which question this client was asked — an
           individual's "Spouse & More Individuals". -->
      <template #body-cell-entityType="cell">
        <q-td :props="cell">
          <app-option-badge :option="entityTypeOption(cell.row.entityType)" />
        </q-td>
      </template>

      <template #body-cell-submittedOnUtc="cell">
        <q-td :props="cell">{{ cell.row.submittedOnUtc ? fmt.formatDate(cell.row.submittedOnUtc) : "—" }}</q-td>
      </template>

      <!-- The column this list exists for. -->
      <template #body-cell-relatedClients="cell">
        <q-td :props="cell">
          <related-clients-cell
            :entity-type="cell.row.entityType"
            :parent="cell.row.parent"
            :rows="cell.row.relatedClients"
            :status-options="relatedEntityStatusOptions"
            :status-option="relatedEntityStatusOption"
            :saving-key="savingKey"
            @set-status="(child, status) => setStatus(cell.row, child, status)"
          />
        </q-td>
      </template>

      <!-- Where the PARENT request stands. -->
      <template #body-cell-requestStatus="cell">
        <q-td :props="cell">
          <app-option-badge :option="requestStatusOption(statusRow(cell.row))" />
        </q-td>
      </template>

      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <!-- The same four a REMS request carries on every other list, in the same order and worded the
               same way: read it, work it, see what the client has been told, talk about it. -->
          <q-btn flat round dense color="primary" icon="o_visibility" :to="requestRoute(cell.row)">
            <q-tooltip>View</q-tooltip>
          </q-btn>
          <q-btn
            v-if="cell.row.canEdit"
            flat round dense color="primary" icon="o_edit" :to="editRoute(cell.row)"
          >
            <q-tooltip>Edit</q-tooltip>
          </q-btn>
          <!-- Every row here has a submitted form, so there is always a log to read — unlike My Requests,
               where the action waits until something has actually been sent. -->
          <q-btn
            v-if="canReadEmailLog" type="a"
            flat round dense color="primary" icon="o_mark_email_read" @click.stop="openEmailLog(cell.row)"
          >
            <q-tooltip>Email log</q-tooltip>
          </q-btn>
          <q-btn type="a" flat round dense color="primary" icon="o_forum" @click.stop="openConversation(cell.row)">
            <q-tooltip>Conversation</q-tooltip>
          </q-btn>
          <!-- The reader's own marks on this row, sitting with the actions rather than apart from them:
               everything before them acts on the REQUEST, and these two are private to whoever is looking. -->
          <entity-row-marks
            v-if="canMarkRows"
            :pinned="isPinned(cell.row.remsId)"
            :colour="colourOf(cell.row.remsId)"
            :palette="markPalette"
            :limit-reached="pinLimitReached"
            :limit="MAX_PINS_PER_TYPE"
            :busy="markBusyId === cell.row.remsId"
            @toggle-pin="togglePin(cell.row.remsId, cell.row.remsNumber)"
            @set-colour="applyColour(cell.row.remsId, $event, cell.row.remsNumber)"
          />
        </q-td>
      </template>

      <template #no-data>
        <div class="full-width column flex-center q-pa-xl text-grey-6">
          <q-icon name="o_account_tree" size="40px" class="q-mb-sm" />
          <div class="text-subtitle1 q-mb-xs">No related entities yet</div>
          <div>
            A request appears here once its client has sent their intake form back and named somebody
            alongside themselves.
          </div>
        </div>
      </template>
    </app-data-table>

    <conversation-dialog v-model="conversationOpen" :request-id="conversationId" :subtitle="conversationSubtitle" />

    <!-- A sent reminder adds an email event and nothing else, but the row's Updated On is what this list
         shows of it, so it reloads on the way out. -->
    <email-log-dialog v-model="emailLogOpen" :rems-id="emailLogId" :subtitle="emailLogSubtitle" @sent="load" />
  </q-page>
</template>

<script setup>
// Related Entities — the shared board of the clients a client brought with them.
import { ref, reactive, computed, watch } from "vue";
import { debounce } from "quasar";
import { remsApi, getApiErrorMessage, EntityType } from "services/api";
import { useNotify } from "composables/useNotify";
import { useListTable } from "composables/useListTable";
import { useColumnFilters } from "composables/useColumnFilters";
import { useDateFormat } from "composables/useDateFormat";
import { useAuditColumns } from "composables/useAuditColumns";
import { usePermissions, Permissions } from "composables/usePermissions";
import { useRowPersonalisation, MAX_PINS_PER_TYPE } from "composables/uf/useRowPersonalisation";
import { useRemsMeta } from "modules/rems/useRemsMeta";

import AppListHeader from "components/common/AppListHeader.vue";
import AppOptionBadge from "components/common/AppOptionBadge.vue";
import AppNameWithSuffix from "components/common/AppNameWithSuffix.vue";
import AppFilterDrawer from "components/common/AppFilterDrawer.vue";
import AppColumnFilters from "components/common/AppColumnFilters.vue";
import AppSelect from "components/common/AppSelect.vue";
import AppDataTable from "components/common/AppDataTable.vue";
import EntityPinnedMark from "components/universal/EntityPinnedMark.vue";
import EntityRowMarks from "components/universal/EntityRowMarks.vue";
import ConversationDialog from "modules/rems/components/ConversationDialog.vue";
import EmailLogDialog from "modules/rems/components/EmailLogDialog.vue";
import RelatedClientsCell from "modules/rems/components/RelatedClientsCell.vue";

const notify = useNotify();
const fmt = useDateFormat();
const auditColumns = useAuditColumns();
const {
  entityTypeLabel, entityTypeOption, entityTypeOptions, requestStatusOption,
  relatedEntityStatusOption, relatedEntityStatusOptions
} = useRemsMeta();

// requestStatusOption reads a REQUEST shape (`status` + `assignedAdmin`); this list calls the first field
// `requestStatus`, because the request's stage is context on a row that is about its related clients.
const statusRow = (row) => ({ status: row?.requestStatus, assignedAdmin: row?.assignedAdmin });

// REMS number, client and related-client names are all covered by the quick search, so none of them gets
// a duplicate filter box; the date and count columns the server cannot narrow on opt out entirely.
const columns = computed(() => [
  { name: "remsNumber", label: "REMS ID", field: "remsNumber", align: "left", sortable: true, default: true, filterable: false },
  { name: "clientName", label: "Client Name", field: "clientName", align: "left", sortable: true, default: true, filterable: false },
  { name: "entityType", label: "Entity Type", field: (r) => entityTypeLabel(r.entityType), align: "left", sortable: true, default: true, filterOptions: entityTypeOptions.value },
  { name: "submittedOnUtc", label: "Submitted On", field: "submittedOnUtc", align: "left", sortable: true, default: true, filterable: false },
  // Deliberately not sortable: it is a table, not a value. Related Clients below is the count of it, which
  // is what a reader would have wanted to sort on anyway.
  { name: "relatedClients", label: "Parent & Related Clients", field: "relatedClients", align: "left", default: true, sortable: false, filterable: false },
  { name: "relatedCount", label: "Related Clients", field: "relatedCount", align: "left", sortable: true, default: false, filterable: false },
  { name: "requestStatus", label: "Request Status", field: "requestStatus", align: "left", default: false, filterable: false },
  ...auditColumns(),
  { name: "actions", label: "Actions", field: "actions", align: "left" }
]);

// The one server filter with no column to hang off — see the drawer. Kept in a reactive object so the
// chips, the reset and the watcher below each have a single thing to read.
const extras = reactive({ relatedStatus: "" });

const { rows, loading, totalRecords, search, filterOpen, pagination, load, onRequest } = useListTable({
  pageKey: "rems-related-entities",
  fetcher: ({ page, limit, sortBy, descending }) =>
    remsApi.relatedEntities({
      page,
      limit,
      sortBy,
      descending,
      search: search.value || undefined,
      entityType: filters.entityType || undefined,
      relatedStatus: extras.relatedStatus || undefined
    }).then((r) => ({ data: r?.data, total: r?.meta?.totalRecords })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

// Server-side, like every other REMS list: the pager counts the whole filtered set, not the loaded page.
const { filters, filterableColumns, filterChips, removeFilter, clearFilters } = useColumnFilters(columns, rows, { server: true });

// The column chips plus one for the standalone filter, so everything narrowing the list is visible in the
// same place and removable the same way.
const allChips = computed(() => {
  if (!extras.relatedStatus) return filterChips.value;
  const label = relatedEntityStatusOption(extras.relatedStatus).label;
  return [...filterChips.value, { key: "relatedStatus", label: `Related Client Status: ${label}` }];
});
const onRemoveFilter = (key) => {
  if (key === "relatedStatus") extras.relatedStatus = "";
  else removeFilter(key);
};
const onClearFilters = () => {
  clearFilters();
  extras.relatedStatus = "";
};

const reload = debounce(() => { pagination.value.page = 1; load(); }, 300);
watch([search, filters, extras], reload, { deep: true });

// ---- The reader's own marks on these rows ----
// A pin floats a row to the top and a colour tints it, both stored against the USER — nobody else sees
// either.
const { has } = usePermissions();
const canMarkRows = computed(() => has(Permissions.RemsRequestsRead));

const {
  palette: markPalette, pinnedRowKeys, pinLimitReached, isPinned, togglePin,
  colours: rowColours, colourOf, applyColour, busyId: markBusyId, sync: syncMarks
} = useRowPersonalisation(EntityType.Rems);

// After every load, never per row: the colours come back for the whole page in one read, and the pins are
// the user's own small set, fetched once.
watch(rows, (list) => {
  if (canMarkRows.value) syncMarks(list.map((r) => r.remsId));
});

// ---- Moving a related client along ----
// The only write on this list.
const savingKey = ref("");
const setStatus = async (row, child, status) => {
  savingKey.value = `${child.kind}:${child.id}`;
  try {
    const updated = await remsApi.setRelatedEntityStatus(child.kind, child.id, status);
    // Patched in place rather than reloaded: the reader is looking at this row, and pulling the whole page
    // out from under them to change one badge loses their scroll position for nothing.
    Object.assign(child, updated);
    notify.success(`${child.name} is now ${relatedEntityStatusOption(updated.status).label}.`);
    // Unless the list is narrowed BY that status, in which case this row's membership genuinely turned on
    // what was just changed and leaving it on screen would be showing a row that no longer matches.
    if (extras.relatedStatus) await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
    // The control is bound to the row, so it is already showing the old value again; what the reader
    // cannot know without a reload is whether somebody else changed it meanwhile.
    await load();
  } finally {
    savingKey.value = "";
  }
};

// ---- The request itself ----
// View and Edit are the same page on two paths — a record to read, or a form to change.
const requestRoute = (row) => ({ name: "rems_request", params: { id: row.remsId } });
const editRoute = (row) => ({ name: "rems_request_edit", params: { id: row.remsId } });

// ---- Email log ----
// The client's side of the correspondence: every intake-form email sent for this request and what the
// provider reported back.
const canReadEmailLog = computed(() => has(Permissions.RemsEmailLogRead));
const emailLogOpen = ref(false);
const emailLogId = ref(null);
const emailLogSubtitle = ref("");
const openEmailLog = (row) => {
  emailLogId.value = row.remsId;
  emailLogSubtitle.value = rowLabel(row);
  emailLogOpen.value = true;
};

// ---- Conversation ----
// The REQUEST's thread, the same one every other REMS surface opens — so a message left here reaches the
// initiator, the admins and the approvers rather than being visible only from this list.
const conversationOpen = ref(false);
const conversationId = ref(null);
const conversationSubtitle = ref("");
const openConversation = (row) => {
  conversationId.value = row.remsId;
  conversationSubtitle.value = rowLabel(row);
  conversationOpen.value = true;
};

// How a request names itself in a dialog title bar — the same line on both, as on every other REMS list.
const rowLabel = (row) => [row.remsNumber, row.clientName].filter(Boolean).join(" — ");
</script>
