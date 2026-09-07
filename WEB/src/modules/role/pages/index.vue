<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'Roles' }]"
      :search="search"
      show-search
      search-placeholder="Search roles"
      show-filters
      :filter-count="filterChips.length"
      show-add
      add-label="Create Role"
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
      page-key="roles"
      row-key="id"
      title="Roles"
      :rows="filteredRows"
      :columns="columns"
      :loading="loading"
      :total-records="filteredRows.length"
      :pagination="pagination"
      @request="onRequest"
      @refresh="load"
    >
      <template #body-cell-isSystem="cell">
        <q-td :props="cell">
          <q-badge :color="cell.value ? 'blue-grey' : 'primary'">{{ cell.value ? "System" : "Custom" }}</q-badge>
        </q-td>
      </template>

      <!-- The row opens the role's own page. -->
      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <!-- A LINK, not a click handler: the role detail is a place, so this renders as a real <a href>
               and middle-click / "open in new tab" work on it. -->
          <q-btn
            flat round dense color="primary" :icon="cell.row.canManage ? 'o_edit' : 'o_visibility'"
            :to="roleRoute(cell.row)"
          >
            <q-tooltip>{{ actionTooltip(cell.row) }}</q-tooltip>
          </q-btn>
          <q-btn
            v-if="cell.row.canManage && !cell.row.isSystem" type="a" flat round dense color="negative"
            icon="o_delete" @click="removeRole(cell.row)"
          >
            <q-tooltip>Delete</q-tooltip>
          </q-btn>
        </q-td>
      </template>
    </app-data-table>

    <deleted-records-panel
      v-if="canManageDeleted" :entity-type="EntityType.Role" :show="showDeleted" @restored="load"
    />

    <!-- Create. -->
    <app-form-drawer
      v-model="formOpen" title="Create Role" :saving="saving" save-label="Create"
      @submit="submitForm" @cancel="resetForm"
    >
      <q-form ref="formRef" greedy>
        <app-text-field
          v-model="form.name" label="Name" required class="q-mb-md"
          :rules="[(v) => !!v || 'Name is required']"
        />
        <app-rich-text-field v-model="form.description" label="Description" class="q-mb-md" />
        <app-select
          v-model="form.permissions" :options="permissionOptions" label="Permissions" multiple
          :loading="loadingPermissions"
          :info="isSuperAdmin ? '' : 'The list stops at what your own tenant can hand out.'"
        />
      </q-form>
    </app-form-drawer>
  </q-page>
</template>

<script setup>
import { ref, reactive, computed, watch } from "vue";
import { useRouter } from "vue-router";
import { debounce } from "quasar";
import { roleApi, getApiErrorMessage, getApiErrorCode, ApiErrorCodes, EntityType } from "services/api";
import { useAuthStore } from "stores/auth";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useListTable } from "composables/useListTable";
import { useColumnFilters } from "composables/useColumnFilters";
import { useDeletedRecords } from "composables/useDeletedRecords";
import { useAuditColumns } from "composables/useAuditColumns";

import AppDataTable from "components/common/AppDataTable.vue";
import DeletedRecordsPanel from "components/universal/DeletedRecordsPanel.vue";
import { stripHtml } from "utils/richText";
import AppFormDrawer from "components/common/AppFormDrawer.vue";
import AppListHeader from "components/common/AppListHeader.vue";
import AppFilterDrawer from "components/common/AppFilterDrawer.vue";
import AppColumnFilters from "components/common/AppColumnFilters.vue";
import AppSelect from "components/common/AppSelect.vue";
import AppTextField from "components/common/AppTextField.vue";
import AppRichTextField from "components/common/AppRichTextField.vue";

const auditColumns = useAuditColumns();
const { showDeleted, canManageDeleted } = useDeletedRecords();
const notify = useNotify();
const { confirm } = useConfirm();
const authStore = useAuthStore();
const router = useRouter();

// The list shows every role this caller may see: the platform ones (theirs to read, a Super Admin's to
// change) and the ones their own tenant created.
const isSuperAdmin = computed(() => authStore.roles.includes("SuperAdmin"));

const columns = [
  { name: "name", label: "Name", field: "name", align: "left", sortable: true, default: true },
  // Descriptions are rich text; the cell shows the text without its markup (see utils/richText).
  { name: "description", label: "Description", field: (r) => stripHtml(r.description), align: "left", default: true },
  {
    name: "isSystem",
    label: "Type",
    field: "isSystem",
    align: "left",
    sortable: true,
    default: true,
    filterOptions: [{ label: "System", value: true }, { label: "Custom", value: false }]
  },
  // Who the role belongs to. A platform role is offered in every tenant and only a Super Admin may
  // change it; the rest were created by a tenant for itself and are its own to maintain.
  {
    name: "scope",
    label: "Scope",
    field: (r) => r.tenantName || "Platform",
    align: "left",
    sortable: true,
    default: true
  },
  { name: "permissionCount", label: "Permissions", field: "permissionCount", align: "left", sortable: true, default: true, filterable: false },
  ...auditColumns(),
  { name: "actions", label: "Actions", field: "actions", align: "left" }
];

const { rows, loading, search, pagination, load, onRequest } = useListTable({
  pageKey: "roles",
  fetcher: ({ sortBy, descending }) =>
    roleApi.list({ search: search.value || undefined, sortBy, descending })
      .then((r) => ({ data: r || [], total: (r || []).length })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

// Client-side column filters (the list loads all roles); badge/count standard via AppListHeader.
const filterOpen = ref(false);
const { filters, filterableColumns, filteredRows, filterChips, removeFilter, clearFilters } = useColumnFilters(columns, rows, { server: false });

// Server-side search: reload (debounced, first page) when it changes.
const reload = debounce(() => { pagination.value.page = 1; load(); }, 300);
watch(search, reload);

// ---- Permission catalogue ----
const permissionOptions = ref([]);
const loadingPermissions = ref(false);
const prettyPermission = (key) => key.replace(/_/g, " ").replace(/\./g, " · ");

const loadPermissions = async () => {
  if (permissionOptions.value.length) return;
  loadingPermissions.value = true;
  try {
    const perms = await roleApi.permissions();
    permissionOptions.value = (perms || []).map((p) => ({ label: prettyPermission(p), value: p }));
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loadingPermissions.value = false;
  }
};

// ---- Open ----
// Editing a role happens on the role's own page.
const roleRoute = (row) => ({ name: "role_detail", params: { id: row.id } });

const actionTooltip = (row) => (row.canManage ? "Open" : "View");

// ---- Create ----
const formOpen = ref(false);
const saving = ref(false);
const formRef = ref(null);
const form = reactive({ name: "", description: "", permissions: [] });

const resetForm = () => {
  form.name = "";
  form.description = "";
  form.permissions = [];
};

const openCreate = async () => {
  resetForm();
  await loadPermissions();
  formOpen.value = true;
};

// Straight onto the new role's page: creating one is the start of setting it up, not the end.
const submitForm = async ({ clearDraft } = {}) => {
  if (!(await formRef.value?.validate())) return;
  saving.value = true;
  try {
    const created = await roleApi.create({
      name: form.name,
      description: form.description,
      permissions: form.permissions
    });
    clearDraft?.();
    formOpen.value = false;
    notify.success("Role created.");
    if (created?.id) {
      router.push({ name: "role_detail", params: { id: created.id } });
    } else {
      load();
    }
  } catch (err) {
    if (getApiErrorCode(err) === ApiErrorCodes.DuplicateIdentifier) {
      notify.error("A role with that name already exists.");
    } else {
      notify.error(getApiErrorMessage(err));
    }
  } finally {
    saving.value = false;
  }
};

const removeRole = async (row) => {
  const ok = await confirm({
    title: "Delete role",
    message: `Delete the "${row.name}" role? Users keeping this role will lose its permissions.`,
    confirmLabel: "Delete",
    type: "danger"
  });
  if (!ok) return;
  try {
    await roleApi.remove(row.id);
    notify.success("Role deleted.");
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};
</script>
