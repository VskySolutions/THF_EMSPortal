<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'Users' }]"
      :search="search"
      show-search
      search-placeholder="Search name or email"
      show-filters
      :filter-count="filterChips.length"
      :show-add="canCreate"
      add-label="Create User"
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
      page-key="users"
      row-key="userId"
      title="All users"
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
        <q-btn v-if="has(Permissions.UsersWrite)" flat dense no-caps color="positive" label="Activate" @click="bulkSetStatus(sel, true)" />
        <q-btn v-if="has(Permissions.UsersWrite)" flat dense no-caps color="negative" label="Deactivate" @click="bulkSetStatus(sel, false)" />
      </template>

      <template #body-cell-isActive="cell">
        <q-td :props="cell">
          <q-badge :color="cell.value ? 'positive' : 'grey'">{{ cell.value ? "Active" : "Inactive" }}</q-badge>
        </q-td>
      </template>

      <!-- Department, badged when this user heads it — a head is that department's REMS Director. -->
      <template #body-cell-department="cell">
        <q-td :props="cell">
          <template v-if="cell.value">
            <span>{{ cell.value }}</span>
            <q-icon v-if="cell.row.isDepartmentHead" name="o_workspace_premium" color="primary" size="18px" class="q-ml-xs">
              <q-tooltip>Heads {{ cell.value }} — its REMS Department Director</q-tooltip>
            </q-icon>
          </template>
          <span v-else class="text-grey-6">—</span>
        </q-td>
      </template>

      <template #body-cell-actions="cell">
        <q-td :props="cell">
          <q-btn flat round dense color="primary" icon="o_visibility" :to="{ name: 'user_detail', params: { id: cell.row.userId } }">
            <q-tooltip>View / Manage</q-tooltip>
          </q-btn>
          <!-- One button per action, all of them on the row. -->
          <q-btn
            v-if="has(Permissions.UsersWrite)" type="a"
            flat round dense
            :color="cell.row.isActive ? 'grey-8' : 'positive'"
            :icon="cell.row.isActive ? 'o_block' : 'o_check_circle'"
            @click="setStatus(cell.row, !cell.row.isActive)"
          >
            <q-tooltip>{{ cell.row.isActive ? "Deactivate" : "Activate" }}</q-tooltip>
          </q-btn>
          <q-btn
            v-if="has(Permissions.UsersResetPassword)" type="a"
            flat round dense color="primary" icon="o_lock_reset" @click="resetPassword(cell.row)"
          >
            <q-tooltip>Reset Password</q-tooltip>
          </q-btn>
        </q-td>
      </template>
    </app-data-table>

    <deleted-records-panel
      v-if="canManageDeleted" :entity-type="EntityType.User" :show="showDeleted" @restored="load"
    />

    <!-- Create user (promote an existing Person to a login account). -->
    <user-create-drawer v-model="formOpen" :person-id="presetPersonId" @created="load" />

    <temp-password-dialog v-model="tempPwOpen" :password="tempPassword" />
  </q-page>
</template>

<script setup>
import { ref, computed, watch, onMounted } from "vue";
import { useRoute, useRouter } from "vue-router";
import { debounce } from "quasar";
import { userApi, getApiErrorMessage, EntityType } from "services/api";
import { usePermissions, Permissions } from "composables/usePermissions";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useListTable } from "composables/useListTable";
import { useColumnFilters } from "composables/useColumnFilters";
import { useDeletedRecords } from "composables/useDeletedRecords";
import { useAuditColumns } from "composables/useAuditColumns";

import AppDataTable from "components/common/AppDataTable.vue";
import DeletedRecordsPanel from "components/universal/DeletedRecordsPanel.vue";
import AppFilterDrawer from "components/common/AppFilterDrawer.vue";
import AppColumnFilters from "components/common/AppColumnFilters.vue";
import AppListHeader from "components/common/AppListHeader.vue";
import UserCreateDrawer from "components/user/UserCreateDrawer.vue";
import TempPasswordDialog from "components/temp_password_dialog.vue";

const route = useRoute();
const router = useRouter();

const { showDeleted, canManageDeleted } = useDeletedRecords();
const notify = useNotify();
const { confirm } = useConfirm();
const { has } = usePermissions();
const canCreate = computed(() => has(Permissions.UsersWrite));
const auditColumns = useAuditColumns();

// Filterable columns are server-side; text/computed/audit/date columns are covered by the search box.
const columns = computed(() => [
  // No Tenant column: the list is scoped to the active tenant, so every row would repeat the same name.
  // A Super Admin changes which tenant they are looking at with the toolbar's tenant scope.
  { name: "fullName", label: "Name", field: "fullName", align: "left", sortable: true, default: true },
  { name: "email", label: "Email", field: "email", align: "left", sortable: true, default: true },
  { name: "phoneNumber", label: "Phone", field: "phoneNumber", align: "left", sortable: true },
  { name: "roles", label: "Role", field: (r) => (r.roles || []).join(", "), align: "left", sortable: false, default: true },
  { name: "groups", label: "Groups", field: (r) => (r.groups || []).map((g) => g.name).join(", "), align: "left", sortable: false, default: true },
  // Department placement in the active tenant.
  { name: "department", label: "Department", field: "department", align: "left", default: true, filterable: false },
  { name: "isActive", label: "Status", field: "isActive", align: "left", sortable: true, default: true, filterOptions: [{ label: "Active", value: true }, { label: "Inactive", value: false }] },
  ...auditColumns(),
  { name: "actions", label: "Actions", field: "actions", align: "left" }
]);

const { rows, loading, totalRecords, selected, search, filterOpen, pagination, load, onRequest } = useListTable({
  pageKey: "users",
  fetcher: ({ page, limit, sortBy, descending }) =>
    userApi.list({
      page,
      limit,
      sortBy,
      descending,
      search: search.value || undefined,
      isActive: typeof filters.isActive === "boolean" ? filters.isActive : undefined,
      name: filters.fullName || undefined,
      email: filters.email || undefined,
      phone: filters.phoneNumber || undefined,
      role: filters.roles || undefined,
      group: filters.groups || undefined
    }).then((r) => ({ data: r?.data, total: r?.meta?.totalRecords })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});

// Server-side per-column filters + search box: reload (debounced, first page) whenever they change.
const { filters, filterableColumns, filterChips, removeFilter, clearFilters } = useColumnFilters(columns, rows, { server: true });
const reload = debounce(() => { pagination.value.page = 1; load(); }, 300);
watch([search, filters], reload, { deep: true });

// ---- Create ----
// The drawer owns everything the form needs (people, roles, placements) and loads it when it opens; this
// page only says WHEN to open it and, for the deep-link below, about whom.
const formOpen = ref(false);
const presetPersonId = ref(null);

const openCreate = (personId = null) => {
  presetPersonId.value = personId;
  formOpen.value = true;
};

// "Convert to User" from the People list deep-links here with ?personId=...
onMounted(() => {
  const personId = route.query.personId;
  if (personId && canCreate.value) {
    openCreate(personId);
    // Drop the query param so a refresh doesn't re-open the drawer.
    router.replace({ query: {} });
  }
});

// Shown after an admin password reset. The create drawer has its own for the new account's password.
const tempPwOpen = ref(false);
const tempPassword = ref("");

// ---- Status / reset ----
const setStatus = async (row, isActive) => {
  const ok = await confirm({
    title: isActive ? "Activate user" : "Deactivate user",
    message: `${isActive ? "Activate" : "Deactivate"} ${row.fullName}?`,
    type: isActive ? "primary" : "danger"
  });
  if (!ok) return;
  try {
    await userApi.setStatus(row.userId, isActive);
    notify.success("Status updated.");
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

const bulkSetStatus = async (sel, isActive) => {
  if (!sel.length) return;
  const ok = await confirm({
    title: isActive ? "Activate users" : "Deactivate users",
    message: `${isActive ? "Activate" : "Deactivate"} ${sel.length} user(s)?`,
    type: isActive ? "primary" : "danger"
  });
  if (!ok) return;
  try {
    await Promise.all(sel.map((r) => userApi.setStatus(r.userId, isActive)));
    notify.success("Users updated.");
    selected.value = [];
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

const resetPassword = async (row) => {
  const ok = await confirm({
    title: "Reset password",
    message: `Generate a new temporary password for ${row.fullName}? Their current sessions will end.`,
    confirmLabel: "Reset",
    type: "danger"
  });
  if (!ok) return;
  try {
    const result = await userApi.resetPassword(row.userId);
    tempPassword.value = result?.temporaryPassword || "";
    tempPwOpen.value = true;
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

</script>
