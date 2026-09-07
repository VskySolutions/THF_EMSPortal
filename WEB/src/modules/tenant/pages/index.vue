<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'Tenants' }]"
      :search="search"
      show-search
      search-placeholder="Search name or identifier"
      show-filters
      :filter-count="filterChips.length"
      show-add
      add-label="Create Tenant"
      show-back
      @update:search="search = $event"
      @filters="filterOpen = true"
      @add="openCreate"
      @back="$router.back()"
    />

    <app-filter-drawer v-model="filterOpen" :chips="filterChips" @remove="removeFilter" @clear="clearFilters">
      <app-select
        v-model="filters.status"
        :options="statusFilterOptions"
        label="Status"
      />
      <q-toggle v-model="filters.includeArchived" label="Show archived" />
      <q-toggle
        v-if="canManageDeleted" v-model="showDeleted" label="Show deleted?" dense class="q-mt-md"
      />
    </app-filter-drawer>

    <app-data-table
      page-key="tenants"
      row-key="tenantId"
      title="All tenants"
      :rows="rows"
      :columns="columns"
      :loading="loading"
      :total-records="totalRecords"
      :pagination="pagination"
      selectable
      @request="onRequest"
      @refresh="load"
      @update:selected="selected = $event"
    >
      <template #bulk-actions="{ selected: sel }">
        <q-btn flat dense no-caps color="positive" label="Activate" @click="bulkSetStatus(sel, true)" />
        <q-btn flat dense no-caps color="negative" label="Deactivate" @click="bulkSetStatus(sel, false)" />
      </template>

      <template #body-cell-status="cell">
        <q-td :props="cell">
          <q-badge :color="statusColor(cell.value)">{{ cell.value }}</q-badge>
        </q-td>
      </template>

      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <q-btn flat round dense color="primary" icon="o_visibility" :to="{ name: 'tenant_detail', params: { id: cell.row.tenantId } }">
            <q-tooltip>View / Manage</q-tooltip>
          </q-btn>
          <!-- One button per action, all of them on the row. -->
          <q-btn type="a" flat round dense color="primary" icon="o_edit" @click="openEdit(cell.row)">
            <q-tooltip>Edit</q-tooltip>
          </q-btn>
          <q-btn
            type="a" flat round dense
            :color="cell.row.status === 'Active' ? 'grey-8' : 'positive'"
            :icon="cell.row.status === 'Active' ? 'o_block' : 'o_check_circle'"
            @click="setStatus(cell.row, cell.row.status !== 'Active')"
          >
            <q-tooltip>{{ cell.row.status === "Active" ? "Deactivate" : "Activate" }}</q-tooltip>
          </q-btn>
          <q-btn type="a" flat round dense color="negative" icon="o_archive" @click="archive(cell.row)">
            <q-tooltip>Archive</q-tooltip>
          </q-btn>
        </q-td>
      </template>
    </app-data-table>

    <deleted-records-panel
      v-if="canManageDeleted" :entity-type="EntityType.Tenant" :show="showDeleted" @restored="load"
    />

    <!-- Create / Edit drawer -->
    <app-form-drawer
      v-model="formOpen"
      :title="editing ? 'Edit Tenant' : 'Create Tenant'"
      :saving="saving"
      @submit="submitForm"
      @cancel="resetForm"
    >
      <q-form ref="formRef" greedy>
        <app-text-field
          v-model="form.name"
          label="Name"
          required
          class="q-mb-md"
          :rules="[(v) => !!v || 'Name is required']"
        />
        <app-text-field
          v-model="form.identifier"
          label="Identifier"
          required
          :disable="editing"
          hint="Lowercase letters, numbers and hyphens"
          :error="!!identifierError"
          :error-message="identifierError"
          :rules="editing ? [] : [
            (v) => !!v || 'Identifier is required',
            (v) => /^[a-z0-9-]+$/.test(v) || 'Use lowercase letters, numbers and hyphens only'
          ]"
        />
        <app-select
          v-model="form.timeZoneId"
          label="Time Zone"
          required
          class="q-mt-md"
          use-input
          :clearable="false"
          :options="allZones"
        />
      </q-form>
    </app-form-drawer>
  </q-page>
</template>

<script setup>
import { ref, reactive, computed, watch } from "vue";
import { debounce } from "quasar";
import { tenantApi, getApiErrorMessage, getApiErrorCode, ApiErrorCodes, EntityType } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useListTable } from "composables/useListTable";
import { useDeletedRecords } from "composables/useDeletedRecords";
import { useAuditColumns } from "composables/useAuditColumns";
import { useTenantScope } from "composables/useTenantScope";

import AppDataTable from "components/common/AppDataTable.vue";
import DeletedRecordsPanel from "components/universal/DeletedRecordsPanel.vue";
import AppFormDrawer from "components/common/AppFormDrawer.vue";
import AppFilterDrawer from "components/common/AppFilterDrawer.vue";
import AppListHeader from "components/common/AppListHeader.vue";
import AppSelect from "components/common/AppSelect.vue";
import AppTextField from "components/common/AppTextField.vue";

const { showDeleted, canManageDeleted } = useDeletedRecords();
const notify = useNotify();
const { confirm } = useConfirm();
const auditColumns = useAuditColumns();
// The toolbar's "View as" menu is rendered from a list this composable caches for the whole session, so
// anything on this page that adds a tenant, renames one or retires one has to tell it.
const { refreshTenants } = useTenantScope();

const columns = [
  { name: "name", label: "Name", field: "name", align: "left", sortable: true, default: true },
  { name: "identifier", label: "Identifier", field: "identifier", align: "left", sortable: true, default: true },
  { name: "status", label: "Status", field: "status", align: "left", sortable: true, default: true },
  { name: "timeZoneId", label: "Time Zone", field: "timeZoneId", align: "left", sortable: true },
  ...auditColumns(),
  { name: "actions", label: "Actions", field: "actions", align: "left" }
];

const filters = reactive({ status: null, includeArchived: false });
const { rows, loading, totalRecords, selected, search, filterOpen, pagination, load, onRequest } = useListTable({
  pageKey: "tenants",
  fetcher: ({ page, limit, sortBy, descending }) =>
    tenantApi.list({
      page,
      limit,
      sortBy,
      descending,
      includeArchived: filters.includeArchived,
      status: filters.status || undefined,
      search: search.value || undefined
    })
      .then((r) => ({ data: r?.data, total: r?.meta?.totalRecords })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

// Server-side filtering: reload (debounced, first page) whenever the search box or a filter changes.
const reload = debounce(() => { pagination.value.page = 1; load(); }, 300);
watch([search, filters], reload, { deep: true });

const statusColor = (status) => ({ Active: "positive", Inactive: "grey", Archived: "blue-grey" }[status] || "grey");
const statusFilterOptions = ["Active", "Inactive", "Archived"].map((s) => ({ label: s, value: s }));

const filterChips = computed(() => {
  const chips = [];
  if (filters.status) chips.push({ key: "status", label: `Status: ${filters.status}` });
  if (filters.includeArchived) chips.push({ key: "includeArchived", label: "Including archived" });
  return chips;
});

const removeFilter = (key) => {
  if (key === "status") filters.status = null;
  if (key === "includeArchived") filters.includeArchived = false;
};
const clearFilters = () => {
  filters.status = null;
  filters.includeArchived = false;
};

// ---- Create / Edit ----
const formOpen = ref(false);
const editing = ref(false);
const saving = ref(false);
const identifierError = ref("");
const formRef = ref(null);
const form = reactive({ tenantId: null, name: "", identifier: "", timeZoneId: "UTC" });

// The whole list goes to AppSelect, which narrows it as you type: it filters in a computed rather than
// through QSelect's filter/done-callback round trip, so there is nothing left here to filter.
const allZones = typeof Intl.supportedValuesOf === "function" ? Intl.supportedValuesOf("timeZone") : ["UTC"];

const resetForm = () => {
  form.tenantId = null;
  form.name = "";
  form.identifier = "";
  form.timeZoneId = "UTC";
  identifierError.value = "";
  editing.value = false;
};

const openCreate = () => {
  resetForm();
  formOpen.value = true;
};

const openEdit = (row) => {
  resetForm();
  editing.value = true;
  form.tenantId = row.tenantId;
  form.name = row.name;
  form.identifier = row.identifier;
  form.timeZoneId = row.timeZoneId || "UTC";
  formOpen.value = true;
};

const submitForm = async ({ clearDraft } = {}) => {
  identifierError.value = "";
  const valid = await formRef.value?.validate();
  if (!valid) return;

  saving.value = true;
  try {
    if (editing.value) {
      await tenantApi.update(form.tenantId, { name: form.name, timeZoneId: form.timeZoneId });
      notify.success("Tenant updated.");
    } else {
      await tenantApi.create({ name: form.name, identifier: form.identifier, timeZoneId: form.timeZoneId });
      notify.success("Tenant created.");
    }
    clearDraft?.();
    formOpen.value = false;
    resetForm();
    load();
    refreshTenants();
  } catch (err) {
    if (getApiErrorCode(err) === ApiErrorCodes.DuplicateIdentifier) {
      identifierError.value = "This identifier is already in use.";
    } else {
      notify.error(getApiErrorMessage(err));
    }
  } finally {
    saving.value = false;
  }
};

// ---- Status / Archive ----
const setStatus = async (row, isActive) => {
  const ok = await confirm({
    title: isActive ? "Activate tenant" : "Deactivate tenant",
    message: `${isActive ? "Activate" : "Deactivate"} "${row.name}"?`,
    type: isActive ? "primary" : "danger"
  });
  if (!ok) return;
  try {
    await tenantApi.setStatus(row.tenantId, isActive);
    notify.success("Status updated.");
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

const bulkSetStatus = async (sel, isActive) => {
  if (!sel.length) return;
  const ok = await confirm({
    title: isActive ? "Activate tenants" : "Deactivate tenants",
    message: `${isActive ? "Activate" : "Deactivate"} ${sel.length} tenant(s)?`,
    type: isActive ? "primary" : "danger"
  });
  if (!ok) return;
  try {
    await Promise.all(sel.map((r) => tenantApi.setStatus(r.tenantId, isActive)));
    notify.success("Tenants updated.");
    selected.value = [];
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

const archive = async (row) => {
  const ok = await confirm({
    title: "Archive tenant",
    message: `Archive "${row.name}"? This retires the tenant.`,
    confirmLabel: "Archive",
    type: "danger"
  });
  if (!ok) return;
  try {
    await tenantApi.archive(row.tenantId);
    notify.success("Tenant archived.");
    load();
    // An archived tenant drops out of the list the scope picker offers, so it must not stay on the menu.
    refreshTenants();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

</script>
