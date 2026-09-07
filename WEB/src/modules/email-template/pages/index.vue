<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[
        { label: 'Home', icon: 'o_home', to: '/' },
        { label: 'Configuration' },
        { label: 'Email Templates' }
      ]"
      show-filters
      :filter-count="filterChips.length"
      show-back
      @filters="filterOpen = true"
      @back="$router.back()"
    >
      <template #actions>
        <app-select
          v-if="canChooseTenant" v-model="selectedScope" :options="scopeOptions" label="Scope"
          :loading="loadingTenants" :clearable="false" style="min-width: 260px;"
        />
      </template>
    </app-list-header>

    <app-filter-drawer v-model="filterOpen" :chips="filterChips" @remove="removeFilter" @clear="clearFilters">
      <app-column-filters v-model="filters" :columns="filterableColumns" />
      <q-toggle
        v-if="canManageDeleted" v-model="showDeleted" label="Show deleted?" dense class="q-mt-md"
      />
    </app-filter-drawer>

    <q-banner dense rounded class="bg-teal-1 text-primary q-mb-md">
      <template #avatar><q-icon name="o_info" color="primary" /></template>
      <span v-if="isGlobalScope">Editing the <strong>platform default</strong> templates — used by every tenant that hasn't customised its own.</span>
      <span v-else>Editing this tenant's templates. Unchanged templates fall back to the platform default.</span>
    </q-banner>

    <app-data-table
      page-key="email-templates"
      row-key="key"
      title="Email templates"
      :rows="filteredRows"
      :columns="columns"
      :loading="loading"
      :total-records="filteredRows.length"
      :pagination="pagination"
      @request="onRequest"
      @refresh="load"
    >
      <template #body-cell-status="cell">
        <q-td :props="cell">
          <q-badge v-if="cell.row.isOverridden" color="positive">Custom</q-badge>
          <q-badge v-else color="grey">Default</q-badge>
        </q-td>
      </template>

      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <q-btn type="a" flat round dense color="primary" icon="o_edit" @click="openEdit(cell.row)">
            <q-tooltip>Edit</q-tooltip>
          </q-btn>
          <!-- One button per action, all of them on the row. -->
          <q-btn type="a" flat round dense color="primary" icon="o_visibility" @click="previewRow(cell.row)">
            <q-tooltip>Preview</q-tooltip>
          </q-btn>
          <q-btn
            v-if="canReset(cell.row)" type="a"
            flat round dense color="negative" icon="o_restart_alt" @click="resetRow(cell.row)"
          >
            <q-tooltip>Reset to default</q-tooltip>
          </q-btn>
        </q-td>
      </template>
    </app-data-table>

    <deleted-records-panel
      v-if="canManageDeleted" :entity-type="EntityType.EmailTemplate" :show="showDeleted" @restored="load"
    />

    <email-template-form-drawer
      v-model="formOpen" :template="editing" :scope-params="scopeParams" @saved="onSaved"
    />

    <email-template-preview-dialog v-model="previewOpen" :subject="preview.subject" :body="preview.body" />
  </q-page>
</template>

<script setup>
import { ref, reactive, computed, watch } from "vue";
import { emailTemplateApi, getApiErrorMessage, EntityType } from "services/api";
import { useTenantOptions } from "composables/useTenantOptions";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useListTable } from "composables/useListTable";
import { useColumnFilters } from "composables/useColumnFilters";
import { useDeletedRecords } from "composables/useDeletedRecords";
import { useAuditColumns } from "composables/useAuditColumns";

import AppDataTable from "components/common/AppDataTable.vue";
import DeletedRecordsPanel from "components/universal/DeletedRecordsPanel.vue";
import AppListHeader from "components/common/AppListHeader.vue";
import AppFilterDrawer from "components/common/AppFilterDrawer.vue";
import AppColumnFilters from "components/common/AppColumnFilters.vue";
import AppSelect from "components/common/AppSelect.vue";
import EmailTemplateFormDrawer from "modules/email-template/components/EmailTemplateFormDrawer.vue";
import EmailTemplatePreviewDialog from "modules/email-template/components/EmailTemplatePreviewDialog.vue";

const GLOBAL = "__global__";

const auditColumns = useAuditColumns();
const { showDeleted, canManageDeleted } = useDeletedRecords();
const notify = useNotify();
const { confirm } = useConfirm();
const { canChooseTenant, tenantOptions, loadingTenants, loadTenants } = useTenantOptions();

// Super Admins choose the platform default or a specific tenant; Tenant Admins are auto-scoped.
const selectedScope = ref(GLOBAL);
const scopeOptions = computed(() => [
  { label: "Platform default (all tenants)", value: GLOBAL },
  ...tenantOptions.value
]);
const isGlobalScope = computed(() => !canChooseTenant.value ? false : selectedScope.value === GLOBAL);
const scopeParams = computed(() => {
  if (!canChooseTenant.value) return {};
  return selectedScope.value === GLOBAL ? { global: true } : { tenantId: selectedScope.value };
});

const columns = [
  { name: "displayName", label: "Template", field: "displayName", align: "left", sortable: true, default: true },
  { name: "subject", label: "Subject", field: "subject", align: "left", default: true },
  {
    name: "status",
    label: "Status",
    field: "isOverridden",
    align: "left",
    default: true,
    filterOptions: [{ label: "Overridden", value: true }, { label: "Default", value: false }]
  },
  // Blank on a template nobody has overridden — its content is still the built-in definition in code,
  // so there is no edit and nobody to attribute it to.
  ...auditColumns(),
  { name: "actions", label: "Actions", field: "actions", align: "left" }
];

const { rows, loading, pagination, load, onRequest } = useListTable({
  pageKey: "email-templates",
  fetcher: ({ sortBy, descending }) =>
    emailTemplateApi.list({ ...scopeParams.value, sortBy, descending })
      .then((r) => ({ data: r?.data, total: r?.data?.length })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

// Client-side column filters (the list loads all templates); badge/count standard via AppListHeader.
const filterOpen = ref(false);
const { filters, filterableColumns, filteredRows, filterChips, removeFilter, clearFilters } = useColumnFilters(columns, rows, { server: false });

watch(selectedScope, () => load());

if (canChooseTenant.value) {
  loadTenants();
}

const canReset = (row) => isGlobalScope.value || row.isOverridden;

// ---- Edit ----
const formOpen = ref(false);
const editing = ref(null);
const openEdit = (row) => { editing.value = row; formOpen.value = true; };
const onSaved = () => { formOpen.value = false; load(); };

// ---- Reset ----
const resetRow = async (row) => {
  const ok = await confirm({
    title: "Reset template",
    message: isGlobalScope.value
      ? `Restore "${row.displayName}" to its built-in default content?`
      : `Reset "${row.displayName}" to the platform default? This tenant's customisation will be removed.`,
    confirmLabel: "Reset",
    type: "danger"
  });
  if (!ok) return;
  try {
    await emailTemplateApi.reset(row.key, scopeParams.value);
    notify.success("Template reset to default.");
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// ---- Preview (effective) ----
const previewOpen = ref(false);
const preview = reactive({ subject: "", body: "" });
const previewRow = async (row) => {
  try {
    const rendered = await emailTemplateApi.preview(row.key, {}, scopeParams.value);
    preview.subject = rendered?.subject || "";
    preview.body = rendered?.body || "";
    previewOpen.value = true;
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// Exposed for unit tests.
defineExpose({ openEdit, resetRow, previewRow, canReset, isGlobalScope, scopeParams, previewOpen });
</script>
