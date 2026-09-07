<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'Person' }]"
      :search="search"
      show-search
      search-placeholder="Search name, email or code"
      show-filters
      :filter-count="filterChips.length"
      :show-add="canWrite"
      add-label="Create Person"
      show-back
      @update:search="search = $event"
      @filters="filterOpen = true"
      @add="openCreate"
      @back="$router.back()"
    />

    <app-filter-drawer v-model="filterOpen" :chips="filterChips" @remove="removeFilter" @clear="clearFilters">
      <app-column-filters v-model="filters" :columns="filterableColumns" />
      <q-toggle
        v-if="canManageDeleted" v-model="showDeleted" label="Show deleted?" dense class="q-mt-md"
      />
    </app-filter-drawer>

    <app-data-table
      page-key="persons"
      row-key="id"
      title="All persons"
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
      <template v-if="canDelete" #bulk-actions="{ selected: sel }">
        <q-btn flat dense no-caps color="negative" label="Delete" @click="bulkDelete(sel)" />
      </template>

      <template #body-cell-isUser="cell">
        <q-td :props="cell">
          <q-badge :color="cell.value ? 'primary' : 'grey-4'" :text-color="cell.value ? 'white' : 'grey-8'">
            {{ cell.value ? "User" : "Not a user" }}
          </q-badge>
        </q-td>
      </template>

      <template #body-cell-isActive="cell">
        <q-td :props="cell">
          <q-badge :color="cell.value ? 'positive' : 'grey'">{{ cell.value ? "Active" : "Inactive" }}</q-badge>
        </q-td>
      </template>

      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <q-btn flat round dense color="primary" icon="o_visibility" :to="{ name: 'person_detail', params: { id: cell.row.id } }">
            <q-tooltip>View / Edit</q-tooltip>
          </q-btn>
          <!-- One button per action, all of them on the row. -->
          <q-btn
            v-if="canCreateUser" type="a"
            flat round dense
            :color="cell.row.isUser ? 'grey-6' : 'primary'"
            :icon="cell.row.isUser ? 'o_how_to_reg' : 'o_person_add'"
            :disable="cell.row.isUser"
            @click="convertToUser(cell.row)"
          >
            <q-tooltip>{{ cell.row.isUser ? "Already a user" : "Convert to User" }}</q-tooltip>
          </q-btn>
          <q-btn
            v-if="canDelete" type="a"
            flat round dense color="negative" icon="o_delete"
            :disable="cell.row.isUser" @click="removePerson(cell.row)"
          >
            <q-tooltip>{{ cell.row.isUser ? "A person who is a user cannot be deleted" : "Delete" }}</q-tooltip>
          </q-btn>
        </q-td>
      </template>
    </app-data-table>

    <deleted-records-panel
      v-if="canManageDeleted" :entity-type="EntityType.Person" :show="showDeleted" @restored="load"
    />

    <!-- Create person -->
    <app-form-drawer v-model="formOpen" title="Create Person" :saving="saving" @submit="submitForm" @cancel="resetForm">
      <q-form ref="formRef" greedy>
        <person-form-fields v-model="form" :tenant-options="tenantOptions" :loading-tenants="loadingTenants" />
      </q-form>
    </app-form-drawer>
  </q-page>
</template>

<script setup>
import { ref, reactive, computed, watch, onMounted } from "vue";
import { useRouter } from "vue-router";
import { personApi, getApiErrorMessage, EntityType } from "services/api";
import { usePermissions, Permissions } from "composables/usePermissions";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useListTable } from "composables/useListTable";
import { useColumnFilters } from "composables/useColumnFilters";
import { useDeletedRecords } from "composables/useDeletedRecords";
import { useAuditColumns } from "composables/useAuditColumns";
import { debounce } from "quasar";

import AppDataTable from "components/common/AppDataTable.vue";
import DeletedRecordsPanel from "components/universal/DeletedRecordsPanel.vue";
import AppFormDrawer from "components/common/AppFormDrawer.vue";
import AppFilterDrawer from "components/common/AppFilterDrawer.vue";
import AppColumnFilters from "components/common/AppColumnFilters.vue";
import AppListHeader from "components/common/AppListHeader.vue";
import PersonFormFields from "components/person/PersonFormFields.vue";
import { blankPersonForm } from "composables/personForm";
import { useTenantOptions } from "composables/useTenantOptions";

const router = useRouter();
const { showDeleted, canManageDeleted } = useDeletedRecords();
const notify = useNotify();
const { confirm } = useConfirm();
const { has } = usePermissions();
const auditColumns = useAuditColumns();
const { canChooseTenant, activeTenantId, tenantOptions, loadingTenants, loadTenants } = useTenantOptions();

const canWrite = computed(() => has(Permissions.PersonsWrite));
const canDelete = computed(() => has(Permissions.PersonsDelete));
const canCreateUser = computed(() => has(Permissions.UsersWrite));

// Tenant dropdown filter for platform/super admins (option value is the tenant id, sent to the API).
const tenantFilterOptions = computed(() =>
  (canChooseTenant.value && tenantOptions.value.length ? tenantOptions.value : null));

// Filterable columns are server-side; text/date columns are covered by the search box.
const SOURCE_LABELS = {
  Person: "Added manually",
  Rems: "REMS request",
  User: "User account"
};
const sourceLabel = (value) => (value ? (SOURCE_LABELS[value] || value) : "—");

const columns = computed(() => [
  {
    name: "tenantName",
    label: "Tenant",
    field: "tenantName",
    align: "left",
    sortable: true,
    default: true,
    ...(tenantFilterOptions.value ? { filterOptions: tenantFilterOptions.value } : { filterable: false })
  },
  { name: "personCode", label: "Code", field: "personCode", align: "left", sortable: true, filterable: false },
  { name: "fullName", label: "Name", field: "fullName", align: "left", sortable: true, default: true, filterable: false },
  { name: "primaryEmail", label: "Email", field: "primaryEmail", align: "left", sortable: true, default: true, filterable: false },
  { name: "mobileNumber", label: "Phone", field: "mobileNumber", align: "left", filterable: false },
  { name: "isUser", label: "Account", field: "isUser", align: "left", sortable: true, default: true, filterOptions: [{ label: "User", value: true }, { label: "Not a user", value: false }] },
  { name: "isActive", label: "Status", field: "isActive", align: "left", sortable: true, filterOptions: [{ label: "Active", value: true }, { label: "Inactive", value: false }] },
  // Where the record came from. Filtering is client-side over the loaded page (this list is not
  // server-filtered on it), so it stays a plain column rather than claiming a server filter it lacks.
  { name: "sourceEntityType", label: "Source", field: (r) => sourceLabel(r.sourceEntityType), align: "left", default: true, filterable: false },
  ...auditColumns(),
  { name: "actions", label: "Actions", field: "actions", align: "left" }
]);

const { rows, loading, totalRecords, selected, search, filterOpen, pagination, load, onRequest } = useListTable({
  pageKey: "persons",
  fetcher: ({ page, limit, sortBy, descending }) =>
    personApi.list({
      page,
      limit,
      sortBy,
      descending,
      search: search.value || undefined,
      tenantId: filters.tenantName || undefined,
      isUser: typeof filters.isUser === "boolean" ? filters.isUser : undefined,
      isActive: typeof filters.isActive === "boolean" ? filters.isActive : undefined
    }).then((r) => ({ data: r?.data, total: r?.meta?.totalRecords })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

// Server-side per-column filters + search box: reload (debounced, first page) on any change.
const { filters, filterableColumns, filterChips, removeFilter, clearFilters } = useColumnFilters(columns, rows, { server: true });
const reload = debounce(() => { pagination.value.page = 1; load(); }, 300);
watch([search, filters], reload, { deep: true });

// Load tenant options so the Tenant dropdown filter is available to platform/super admins.
onMounted(() => { if (canChooseTenant.value) loadTenants(); });

// ---- Create ----
const formOpen = ref(false);
const saving = ref(false);
const formRef = ref(null);
const form = reactive(blankPersonForm());

const resetForm = () => Object.assign(form, blankPersonForm());

const openCreate = async () => {
  resetForm();
  // Super/platform admins pick a tenant; everyone else is auto-scoped to their active tenant.
  if (canChooseTenant.value) {
    await loadTenants();
  } else {
    form.tenantId = activeTenantId.value;
  }
  formOpen.value = true;
};

const submitForm = async ({ clearDraft } = {}) => {
  if (!(await formRef.value?.validate())) return;
  saving.value = true;
  try {
    const payload = { ...form, dateOfBirth: form.dateOfBirth || null };
    await personApi.create(payload);
    clearDraft?.();
    formOpen.value = false;
    resetForm();
    notify.success("Person created.");
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    saving.value = false;
  }
};

const convertToUser = (row) => {
  router.push({ name: "users", query: { personId: row.id } });
};

const removePerson = async (row) => {
  const ok = await confirm({
    title: "Delete person",
    message: `Delete ${row.fullName}? This cannot be undone.`,
    confirmLabel: "Delete",
    type: "danger"
  });
  if (!ok) return;
  try {
    await personApi.remove(row.id);
    notify.success("Person deleted.");
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

const bulkDelete = async (sel) => {
  // Persons linked to a user can't be deleted (the API rejects them) — skip and warn.
  const deletable = sel.filter((r) => !r.isUser);
  const skipped = sel.length - deletable.length;
  if (!deletable.length) {
    notify.error("Selected persons are linked to users and can't be deleted.");
    return;
  }
  const ok = await confirm({
    title: "Delete persons",
    message: `Delete ${deletable.length} person(s)?${skipped ? ` (${skipped} linked to a user will be skipped.)` : ""} This cannot be undone.`,
    confirmLabel: "Delete",
    type: "danger"
  });
  if (!ok) return;
  try {
    await Promise.all(deletable.map((r) => personApi.remove(r.id)));
    notify.success("Persons deleted.");
    selected.value = [];
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};
</script>

<style scoped>
.section-subhead {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--q-primary);
  margin: 4px 0 8px;
}
</style>
