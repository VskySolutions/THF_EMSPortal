<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'Permission Groups' }]"
      :search="search"
      show-search
      search-placeholder="Search group name or description"
      show-filters
      :filter-count="filterChips.length"
      show-add
      add-label="Create Group"
      show-back
      @update:search="search = $event"
      @filters="filterOpen = true"
      @add="openCreate"
      @back="$router.back()"
    >
      <template #actions>
        <q-btn outline no-caps color="primary" icon="o_dashboard_customize" label="Use Template" @click="openTemplatePicker" />
      </template>
    </app-list-header>

    <app-filter-drawer v-model="filterOpen" :chips="filterChips" @remove="removeFilter" @clear="onClearFilters">
      <app-column-filters v-model="filters" :columns="filterableColumns" />
      <q-toggle v-model="filters.usedByRoles" label="Used by roles only" left-label color="primary" />
      <q-toggle
        v-if="canManageDeleted" v-model="showDeleted" label="Show deleted?" dense class="q-mt-md"
      />
    </app-filter-drawer>

    <app-data-table
      page-key="permission-groups"
      row-key="id"
      title="All permission groups"
      :rows="rows"
      :columns="columns"
      :loading="loading"
      :total-records="totalRecords"
      :pagination="pagination"
      default-sort-by="updatedOnUtc"
      selectable
      @request="onRequest"
      @refresh="load"
      @update:selected="selected = $event"
    >
      <template #body-cell-members="cell">
        <q-td :props="cell">
          <span class="text-weight-medium">{{ cell.row.currentUsage }}</span>
          <template v-if="cell.row.capacityLimit != null"> / {{ cell.row.capacityLimit }}</template>
          <span v-else class="text-grey-6"> · unlimited</span>
          <q-badge v-if="cell.row.isFull" color="negative" class="q-ml-sm" data-test="full-badge">
            <q-icon name="o_block" size="12px" class="q-mr-xs" />Full
          </q-badge>
        </q-td>
      </template>

      <template #body-cell-status="cell">
        <q-td :props="cell">
          <q-badge :color="cell.value ? 'positive' : 'grey'">{{ cell.value ? "Active" : "Inactive" }}</q-badge>
        </q-td>
      </template>

      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <q-btn flat round dense color="primary" icon="o_visibility" :to="{ name: 'permission_group_detail', params: { id: cell.row.id } }">
            <q-tooltip>View</q-tooltip>
          </q-btn>
          <!-- One button per action, all of them on the row. -->
          <q-btn type="a" flat round dense color="primary" icon="o_edit" @click="openEdit(cell.row)">
            <q-tooltip>Edit</q-tooltip>
          </q-btn>
          <q-btn
            type="a" flat round dense
            :color="cell.row.isActive ? 'grey-8' : 'positive'"
            :icon="cell.row.isActive ? 'o_toggle_off' : 'o_toggle_on'"
            @click="toggleStatus(cell.row)"
          >
            <q-tooltip>{{ cell.row.isActive ? "Deactivate" : "Activate" }}</q-tooltip>
          </q-btn>
          <q-btn type="a" flat round dense color="negative" icon="o_delete" @click="removeGroup(cell.row)">
            <q-tooltip>Delete</q-tooltip>
          </q-btn>
        </q-td>
      </template>

      <template #no-data>
        <div class="full-width column flex-center q-pa-xl text-grey-6">
          <q-icon name="o_workspaces" size="40px" class="q-mb-sm" />
          <div class="text-subtitle1 q-mb-xs">No permission groups yet</div>
          <div class="q-mb-md">Create your first group to start composing roles from reusable permission sets.</div>
          <q-btn unelevated no-caps color="primary" icon="o_add" label="Create Group" @click="openCreate" />
        </div>
      </template>
    </app-data-table>

    <deleted-records-panel
      v-if="canManageDeleted" :entity-type="EntityType.PermissionGroup" :show="showDeleted" @restored="load"
    />

    <!-- Template picker: choose a template (or start blank) before opening the form drawer. -->
    <q-dialog v-model="templateOpen">
      <q-card style="min-width: 420px; max-width: 92vw;">
        <q-card-section class="text-h6">Start from a template</q-card-section>
        <q-separator />
        <q-card-section>
          <div class="text-body2 text-grey-7 q-mb-sm">Pick a template to pre-fill the group, or start from scratch.</div>
          <q-list v-if="templates.length" bordered separator>
            <q-item v-for="t in templates" :key="t.id" clickable @click="chooseTemplate(t)">
              <q-item-section avatar><q-icon name="o_dashboard_customize" color="primary" /></q-item-section>
              <q-item-section>
                <q-item-label>{{ t.name }}</q-item-label>
                <q-item-label caption>{{ stripHtml(t.description) || "—" }} · {{ (t.permissionKeys || []).length }} keys</q-item-label>
              </q-item-section>
            </q-item>
          </q-list>
          <div v-else class="text-grey-6">No templates available.</div>
        </q-card-section>
        <q-separator />
        <q-card-actions align="right">
          <q-btn flat no-caps color="grey-8" label="Cancel" @click="templateOpen = false" />
          <q-btn unelevated no-caps color="primary" label="Start Blank" @click="chooseTemplate(null)" />
        </q-card-actions>
      </q-card>
    </q-dialog>

    <permission-group-form-drawer
      ref="formDrawer" v-model="formOpen" :tenant-id="selectedTenantId" :group-id="editingId" :template="pendingTemplate"
      @saved="onSaved"
    />
  </q-page>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { debounce } from "quasar";
import { permissionGroupApi, getApiErrorMessage, EntityType } from "services/api";
import { useTenantOptions } from "composables/useTenantOptions";
import { useTenantScope } from "composables/useTenantScope";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useListTable } from "composables/useListTable";
import { useColumnFilters } from "composables/useColumnFilters";
import { useDeletedRecords } from "composables/useDeletedRecords";
import { useAuditColumns } from "composables/useAuditColumns";

import AppDataTable from "components/common/AppDataTable.vue";
import DeletedRecordsPanel from "components/universal/DeletedRecordsPanel.vue";
import { stripHtml } from "utils/richText";
import AppFilterDrawer from "components/common/AppFilterDrawer.vue";
import AppColumnFilters from "components/common/AppColumnFilters.vue";
import AppListHeader from "components/common/AppListHeader.vue";
import PermissionGroupFormDrawer from "modules/permission-group/components/PermissionGroupFormDrawer.vue";

const auditColumns = useAuditColumns();
const { showDeleted, canManageDeleted } = useDeletedRecords();
const notify = useNotify();
const { confirm } = useConfirm();
const { canChooseTenant } = useTenantOptions();

// The tenant in view comes from the toolbar's global scope control rather than a dropdown of its own —
// one selection drives every tenant-scoped screen.
const { selectedTenantId } = useTenantScope();

const STATUS_OPTIONS = [
  { label: "Active", value: "true" },
  { label: "Inactive", value: "false" }
];
const CATEGORY_OPTIONS = [
  "Tenants", "Users", "Access", "Mappings", "Jobs", "Schedules", "System"
].map((c) => ({ label: c, value: c }));

const columns = computed(() => [
  { name: "name", label: "Group Name", field: "name", align: "left", sortable: true, default: true, filterable: false },
  // Descriptions are rich text; the cell shows the text without its markup (see utils/richText).
  { name: "description", label: "Description", field: (r) => stripHtml(r.description), align: "left", default: true, filterable: false },
  { name: "permissionCount", label: "Permission Count", field: "permissionCount", align: "left", sortable: true, default: true, filterable: false },
  // Neither is sortable: both are counted in their own batched query after the page of groups is read, so
  // there is no column for the database to order the whole set by.
  { name: "rolesUsingCount", label: "Roles Using", field: "rolesUsingCount", align: "left", default: true, filterable: false },
  { name: "members", label: "Members", field: "currentUsage", align: "left", default: true, filterable: false },
  { name: "status", label: "Status", field: "isActive", align: "left", sortable: true, default: true, filterOptions: STATUS_OPTIONS },
  { name: "category", label: "Category", field: "category", align: "left", default: false, filterOptions: CATEGORY_OPTIONS },
  ...(canChooseTenant.value ? [{ name: "tenantName", label: "Tenant", field: "tenantName", align: "left", sortable: true, default: true, filterable: false }] : []),
  ...auditColumns(),
  { name: "actions", label: "Actions", field: "actions", align: "left" }
]);

const { rows, loading, totalRecords, selected, search, filterOpen, pagination, load, onRequest } = useListTable({
  pageKey: "permission-groups",
  fetcher: ({ page, limit, sortBy, descending }) =>
    permissionGroupApi.list({
      page,
      limit,
      sortBy,
      descending,
      search: search.value || undefined,
      isActive: filters.status != null ? filters.status === "true" : undefined,
      usedByRoles: filters.usedByRoles || undefined,
      category: filters.category || undefined,
      tenantId: (canChooseTenant.value && selectedTenantId.value) ? selectedTenantId.value : undefined
    }).then((r) => ({ data: r?.data, total: r?.meta?.totalRecords })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

const { filters, filterableColumns, filterChips, removeFilter, clearFilters } = useColumnFilters(columns, rows, { server: true });
// "Used by roles only" is a toggle outside the column-filter set; reset it alongside the rest.
const onClearFilters = () => { clearFilters(); filters.usedByRoles = false; };
const reload = debounce(() => { pagination.value.page = 1; load(); }, 300);
watch([search, filters], reload, { deep: true });
// No watcher on the tenant: changing the global scope fires `tenant-switched`, which useListTable already
// reloads on.

// ---- Create / Edit ----
const formOpen = ref(false);
const editingId = ref(null);
const pendingTemplate = ref(null);
const formDrawer = ref(null);

const openCreate = () => {
  editingId.value = null;
  pendingTemplate.value = null;
  formOpen.value = true;
};

const openEdit = (row) => {
  editingId.value = row.id;
  pendingTemplate.value = null;
  formOpen.value = true;
};

const onSaved = () => { formOpen.value = false; load(); };

// ---- Template picker ----
const templateOpen = ref(false);
const templates = ref([]);

const openTemplatePicker = async () => {
  templateOpen.value = true;
  try {
    templates.value = await permissionGroupApi.templates() || [];
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

const chooseTemplate = (template) => {
  templateOpen.value = false;
  editingId.value = null;
  pendingTemplate.value = template;
  formOpen.value = true;
};

// ---- Activate / Deactivate ----
const toggleStatus = async (row) => {
  try {
    await permissionGroupApi.setStatus(row.id, !row.isActive);
    notify.success(row.isActive ? "Group deactivated." : "Group activated.");
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// ---- Delete ----
const removeGroup = async (row) => {
  const ok = await confirm({
    title: "Delete permission group",
    message: `Delete "${row.name}"? Roles using it will lose its permissions. This cannot be undone.`,
    confirmLabel: "Delete",
    type: "danger"
  });
  if (!ok) return;
  try {
    await permissionGroupApi.remove(row.id);
    notify.success("Permission group deleted.");
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};
</script>
