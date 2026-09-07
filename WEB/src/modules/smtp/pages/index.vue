<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[
        { label: 'Home', icon: 'o_home', to: '/' },
        { label: 'Configuration' },
        { label: 'Email Accounts' }
      ]"
      :search="search"
      show-search
      search-placeholder="Search name, host or from address"
      show-filters
      :filter-count="filterChips.length"
      show-add
      add-label="Add Account"
      show-back
      @update:search="search = $event"
      @filters="filterOpen = true"
      @add="openCreate"
      @back="$router.back()"
    />

    <!-- Warning when the tenant has accounts but none is active (AC-SMTP-009.3). -->
    <q-banner v-if="showNoActiveWarning" dense rounded class="bg-red-1 text-negative q-mb-md">
      <template #avatar><q-icon name="o_warning" color="negative" /></template>
      No active email account is configured for this tenant. Set one active so notifications can be sent.
    </q-banner>

    <app-filter-drawer v-model="filterOpen" :chips="filterChips" @remove="removeFilter" @clear="clearFilters">
      <app-column-filters v-model="filters" :columns="filterableColumns" />
      <q-toggle
        v-if="canManageDeleted" v-model="showDeleted" label="Show deleted?" dense class="q-mt-md"
      />
    </app-filter-drawer>

    <app-data-table
      page-key="smtp-accounts"
      row-key="id"
      title="All email accounts"
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
        <q-btn flat dense no-caps color="negative" icon="o_delete" label="Delete" @click="bulkDelete(sel)" />
      </template>

      <template #body-cell-encryptionType="cell">
        <q-td :props="cell">{{ encryptionLabel(cell.row.encryptionType) }}</q-td>
      </template>

      <template #body-cell-status="cell">
        <q-td :props="cell">
          <q-badge :color="cell.value ? 'positive' : 'grey'">{{ cell.value ? "Active" : "Inactive" }}</q-badge>
        </q-td>
      </template>

      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <q-btn type="a" flat round dense color="primary" icon="o_edit" @click="openEdit(cell.row)">
            <q-tooltip>Edit</q-tooltip>
          </q-btn>
          <!-- One button per action, all of them on the row. -->
          <q-btn
            v-if="!cell.row.isActive" type="a"
            flat round dense color="positive" icon="o_check_circle" @click="setActive(cell.row)"
          >
            <q-tooltip>Set as Active</q-tooltip>
          </q-btn>
          <q-btn type="a" flat round dense color="primary" icon="o_send" @click="openTest(cell.row)">
            <q-tooltip>Send Test Email</q-tooltip>
          </q-btn>
          <q-btn
            type="a" flat round dense color="negative" icon="o_delete"
            :disable="cell.row.isActive" @click="deleteAccount(cell.row)"
          >
            <q-tooltip>{{ cell.row.isActive ? "The active account cannot be deleted." : "Delete" }}</q-tooltip>
          </q-btn>
        </q-td>
      </template>

      <template #no-data>
        <div class="full-width column flex-center q-pa-xl text-grey-6">
          <q-icon name="o_mail" size="40px" class="q-mb-sm" />
          <div class="text-subtitle1 q-mb-xs">No email accounts configured</div>
          <div class="q-mb-md">Add your first SMTP account to send notifications for this tenant.</div>
          <q-btn unelevated no-caps color="primary" icon="o_add" label="Add Account" @click="openCreate" />
        </div>
      </template>
    </app-data-table>

    <deleted-records-panel
      v-if="canManageDeleted" :entity-type="EntityType.SmtpAccount" :show="showDeleted" @restored="load"
    />

    <smtp-account-form-drawer
      v-model="formOpen" :tenant-id="selectedTenantId" :account-id="editingId" @saved="onSaved"
    />

    <test-email-dialog v-model="testOpen" :account="testAccount" :tenant-id="selectedTenantId" />
  </q-page>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { debounce } from "quasar";
import { smtpAccountApi, getApiErrorMessage, EntityType } from "services/api";
import { useTenantOptions } from "composables/useTenantOptions";
import { useTenantScope } from "composables/useTenantScope";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useListTable } from "composables/useListTable";
import { useColumnFilters } from "composables/useColumnFilters";
import { useDeletedRecords } from "composables/useDeletedRecords";
import { useAuditColumns } from "composables/useAuditColumns";
import { useSmtpOptions } from "composables/useSmtpOptions";

import AppDataTable from "components/common/AppDataTable.vue";
import DeletedRecordsPanel from "components/universal/DeletedRecordsPanel.vue";
import AppFilterDrawer from "components/common/AppFilterDrawer.vue";
import AppColumnFilters from "components/common/AppColumnFilters.vue";
import AppListHeader from "components/common/AppListHeader.vue";
import SmtpAccountFormDrawer from "modules/smtp/components/SmtpAccountFormDrawer.vue";
import TestEmailDialog from "modules/smtp/components/TestEmailDialog.vue";

const auditColumns = useAuditColumns();
const { showDeleted, canManageDeleted } = useDeletedRecords();
const notify = useNotify();
const { confirm } = useConfirm();
const { encryptionLabel } = useSmtpOptions();
const { canChooseTenant } = useTenantOptions();

// The tenant in view comes from the toolbar's global scope control rather than a dropdown of its own —
// one selection drives every tenant-scoped screen.
const { selectedTenantId } = useTenantScope();
const scopeTenantId = () => (canChooseTenant.value && selectedTenantId.value ? selectedTenantId.value : undefined);

const STATUS_OPTIONS = [
  { label: "Active", value: "active" },
  { label: "Inactive", value: "inactive" }
];

const columns = [
  { name: "accountName", label: "Account Name", field: "accountName", align: "left", sortable: true, default: true, filterable: false },
  { name: "host", label: "Host", field: "host", align: "left", sortable: true, default: true, filterable: false },
  { name: "port", label: "Port", field: "port", align: "left", sortable: true, default: true, filterable: false },
  { name: "fromEmail", label: "From Email", field: "fromEmail", align: "left", sortable: true, default: true, filterable: false },
  { name: "encryptionType", label: "Encryption", field: "encryptionType", align: "left", default: true, filterable: false },
  { name: "status", label: "Status", field: "isActive", align: "left", sortable: true, default: true, filterOptions: STATUS_OPTIONS },
  // All four from the shared set, so this list keeps the platform convention: the updated pair last and
  // visible, the created pair a click away in the Columns menu.
  ...auditColumns({ overrides: { createdBy: "createdByName", updatedBy: "updatedByName" } }),
  { name: "actions", label: "Actions", field: "actions", align: "left" }
];

const { rows, loading, totalRecords, selected, search, filterOpen, pagination, load, onRequest } = useListTable({
  pageKey: "smtp-accounts",
  // No default column.
  defaultSortBy: null,
  fetcher: ({ page, limit, sortBy, descending }) =>
    smtpAccountApi.list({
      tenantId: scopeTenantId(),
      status: filters.status || undefined,
      page,
      limit,
      sortBy,
      descending
    }).then((r) => ({ data: r?.data, total: r?.meta?.totalRecords ?? r?.data?.length })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

const { filters, filterableColumns, filterChips, removeFilter, clearFilters } = useColumnFilters(columns, rows, { server: true });
const reload = debounce(() => { pagination.value.page = 1; load(); }, 300);
watch([search, filters], reload, { deep: true });
// No watcher on the tenant: changing the global scope fires `tenant-switched`, which useListTable already
// reloads on.

// Warn only when not filtering by status, the tenant has accounts, and none is active.
const showNoActiveWarning = computed(() =>
  !filters.status && !loading.value && rows.value.length > 0 && !rows.value.some((r) => r.isActive));

// ---- Create / Edit ----
const formOpen = ref(false);
const editingId = ref(null);

const openCreate = () => { editingId.value = null; formOpen.value = true; };
const openEdit = (row) => { editingId.value = row.id; formOpen.value = true; };
const onSaved = () => { formOpen.value = false; load(); };

// ---- Set active ----
const setActive = async (row) => {
  if (row.isActive) return; // already active → no-op (AC-SMTP-005.3)
  const ok = await confirm({
    title: "Set active account",
    message: `Make "${row.accountName}" the active sending account for this tenant?`,
    type: "primary"
  });
  if (!ok) return;
  try {
    await smtpAccountApi.activate(row.id, scopeTenantId());
    notify.success("Active account updated.");
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// ---- Test ----
const testOpen = ref(false);
const testAccount = ref(null);
const openTest = (row) => { testAccount.value = row; testOpen.value = true; };

// ---- Delete ----
const deleteAccount = async (row) => {
  if (row.isActive) {
    notify.error("The active account cannot be deleted. Activate another account first.");
    return;
  }
  const ok = await confirm({
    title: "Delete email account",
    message: `Delete "${row.accountName}"? This cannot be undone.`,
    confirmLabel: "Delete",
    type: "danger"
  });
  if (!ok) return;
  try {
    await smtpAccountApi.remove(row.id, scopeTenantId());
    notify.success("Email account deleted.");
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// ---- Bulk delete (active accounts are skipped, as they cannot be deleted) ----
const bulkDelete = async (sel) => {
  if (!sel.length) return;
  const deletable = sel.filter((r) => !r.isActive);
  const skipped = sel.length - deletable.length;
  if (!deletable.length) {
    notify.error("The active account cannot be deleted. Activate another account first.");
    return;
  }
  const ok = await confirm({
    title: "Delete email accounts",
    message: `Delete ${deletable.length} account(s)?${skipped ? ` ${skipped} active account(s) will be skipped.` : ""}`,
    confirmLabel: "Delete",
    type: "danger"
  });
  if (!ok) return;
  try {
    await Promise.all(deletable.map((r) => smtpAccountApi.remove(r.id, scopeTenantId())));
    notify.success(`Deleted ${deletable.length} account(s).${skipped ? ` Skipped ${skipped} active.` : ""}`);
    selected.value = [];
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// Exposed for unit tests (and parent interactions).
defineExpose({ openCreate, openEdit, setActive, deleteAccount, bulkDelete, openTest, showNoActiveWarning });
</script>
