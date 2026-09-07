<template>
  <q-page padding>
    <app-detail-header
      :items="[
        { label: 'Home', icon: 'o_home', to: '/' },
        { label: 'Tenants', to: { name: 'tenants' } },
        { label: tenant?.name || 'Tenant' }
      ]"
      :back-to="{ name: 'tenants' }"
    />

    <div v-if="loading" class="row flex-center q-pa-xl">
      <q-spinner color="primary" size="40px" />
    </div>

    <div v-else-if="tenant">
      <!-- Basic info -->
      <q-card flat bordered class="tenant-card q-mb-md">
        <q-card-section class="text-subtitle1 text-weight-medium">Basic information</q-card-section>
        <q-separator />
        <q-card-section class="row q-col-gutter-md">
          <app-text-field v-model="name" label="Name" class="col-12 col-sm-6" />
          <app-text-field :model-value="tenant.identifier" readonly label="Identifier" class="col-12 col-sm-6" />
        </q-card-section>
        <q-card-actions align="right">
          <q-btn unelevated no-caps color="primary" label="Save" :loading="savingName" :disable="name === tenant.name" @click="saveName" />
        </q-card-actions>
      </q-card>

      <!-- Status -->
      <q-card flat bordered class="tenant-card q-mb-md">
        <q-card-section class="row items-center">
          <div class="text-subtitle1 text-weight-medium">Status</div>
          <q-badge :color="statusColor" class="q-ml-md">{{ tenant.status }}</q-badge>
          <q-space />
          <q-btn
            v-if="tenant.status !== 'Archived'"
            outline
            no-caps
            :color="tenant.status === 'Active' ? 'negative' : 'positive'"
            :label="tenant.status === 'Active' ? 'Deactivate' : 'Activate'"
            @click="toggleStatus"
          />
        </q-card-section>
      </q-card>

      <!-- The tenant's own user accounts. -->
      <app-data-table
        v-if="canReadUsers"
        page-key="tenant-users"
        row-key="userId"
        :title="`Users in ${tenant.name}`"
        class="q-mb-md"
        :rows="userRows"
        :columns="userColumns"
        :loading="loadingUsers"
        :total-records="totalUsers"
        :pagination="userPagination"
        @request="onUsersRequest"
        @refresh="loadUsers"
      >
        <template #actions>
          <q-input
            v-model="userSearch" dense outlined debounce="300" placeholder="Search name or email"
            style="min-width: 220px;"
          >
            <template #prepend><q-icon name="o_search" /></template>
          </q-input>
          <q-btn
            v-if="canWriteUsers" unelevated no-caps color="primary" icon="o_person_add" label="Add User"
            @click="createUserOpen = true"
          />
        </template>

        <template #body-cell-isActive="cell">
          <q-td :props="cell">
            <q-badge :color="cell.value ? 'positive' : 'grey'">{{ cell.value ? "Active" : "Inactive" }}</q-badge>
          </q-td>
        </template>

        <template #body-cell-actions="cell">
          <q-td :props="cell">
            <q-btn
              flat round dense color="primary" icon="o_visibility"
              :to="{ name: 'user_detail', params: { id: cell.row.userId } }"
            >
              <q-tooltip>View / Manage</q-tooltip>
            </q-btn>
            <!-- One button per action, all of them on the row. -->
            <q-btn
              v-if="canWriteUsers" type="a"
              flat round dense
              :color="cell.row.isActive ? 'grey-8' : 'positive'"
              :icon="cell.row.isActive ? 'o_block' : 'o_check_circle'"
              @click="setUserStatus(cell.row, !cell.row.isActive)"
            >
              <q-tooltip>{{ cell.row.isActive ? "Deactivate" : "Activate" }}</q-tooltip>
            </q-btn>
            <q-btn
              v-if="canResetPassword" type="a"
              flat round dense color="primary" icon="o_lock_reset" @click="resetUserPassword(cell.row)"
            >
              <q-tooltip>Reset Password</q-tooltip>
            </q-btn>
          </q-td>
        </template>
      </app-data-table>

      <!-- The account is created IN this tenant, so the drawer is told which one and never asks. -->
      <user-create-drawer v-model="createUserOpen" :tenant-id="tenantId" @created="loadUsers" />

      <temp-password-dialog v-model="tempPwOpen" :password="tempPassword" />

      <!-- Danger zone -->
      <q-card v-if="tenant.status !== 'Archived'" flat bordered class="tenant-card danger-zone q-mb-md">
        <q-card-section class="text-subtitle1 text-weight-medium text-negative">Danger zone</q-card-section>
        <q-separator />
        <q-banner v-if="archiveError" dense class="bg-red-1 text-negative q-ma-md">
          <template #avatar><q-icon name="o_error" color="negative" /></template>
          {{ archiveError }}
        </q-banner>
        <q-card-actions>
          <div class="text-body2 text-grey-7 q-pa-sm">Archiving retires this tenant.</div>
          <q-space />
          <q-btn outline no-caps color="negative" icon="o_archive" label="Archive tenant" @click="archive" />
        </q-card-actions>
      </q-card>

      <app-record-audit :audit="tenant.audit" />
    </div>
  </q-page>
</template>

<script setup>
import { ref, computed, watch, onMounted } from "vue";
import { useRoute, useRouter } from "vue-router";
import { debounce } from "quasar";
import { tenantApi, userApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { usePermissions, Permissions } from "composables/usePermissions";
import { useListTable } from "composables/useListTable";
import { useTenantScope } from "composables/useTenantScope";
import { useAuditColumns } from "composables/useAuditColumns";
import { useAuthStore } from "stores/auth";
import { useTenantStore } from "stores/tenant";
import AppDetailHeader from "components/common/AppDetailHeader.vue";
import AppTextField from "components/common/AppTextField.vue";
import AppDataTable from "components/common/AppDataTable.vue";
import AppRecordAudit from "components/common/AppRecordAudit.vue";
import UserCreateDrawer from "components/user/UserCreateDrawer.vue";
import TempPasswordDialog from "components/temp_password_dialog.vue";

const route = useRoute();
const router = useRouter();
const notify = useNotify();
const { confirm } = useConfirm();
const { has } = usePermissions();
const auditColumns = useAuditColumns();
// Renaming or retiring the tenant changes what the toolbar's "View as" menu should say about it, and
// that menu is drawn from a list cached for the whole session.
const { refreshTenants } = useTenantScope();

const tenantId = route.params.id;
const tenant = ref(null);
const loading = ref(false);
const name = ref("");
const savingName = ref(false);
const archiveError = ref("");

// Whether the tenant being edited is one the signed-in user actually belongs to.
const authStore = useAuthStore();
const tenantStore = useTenantStore();
const isOwnTenant = computed(() => tenantStore.assignments.some((t) => t.tenantId === tenantId));
const reloadAssignments = () => authStore.loadProfile().catch(() => { /* non-fatal: the name catches up on the next sign-in */ });

const statusColor = computed(() =>
  ({ Active: "positive", Inactive: "grey", Archived: "blue-grey" }[tenant.value?.status] || "grey"));

const load = async () => {
  loading.value = !tenant.value;
  try {
    tenant.value = await tenantApi.get(tenantId);
    name.value = tenant.value.name;
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loading.value = false;
  }
};

const saveName = async () => {
  savingName.value = true;
  try {
    await tenantApi.update(tenantId, { name: name.value });
    notify.success("Tenant updated.");
    load();
    // The new name has to reach the toolbar, and — when this is a tenant the user is actually assigned
    // to — the tenant switcher beside it, which reads the assignments cached at sign-in.
    refreshTenants();
    if (isOwnTenant.value) reloadAssignments();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    savingName.value = false;
  }
};

const toggleStatus = async () => {
  const activate = tenant.value.status !== "Active";
  const ok = await confirm({
    title: activate ? "Activate tenant" : "Deactivate tenant",
    message: `${activate ? "Activate" : "Deactivate"} "${tenant.value.name}"?`,
    type: activate ? "primary" : "danger"
  });
  if (!ok) return;
  try {
    await tenantApi.setStatus(tenantId, activate);
    notify.success("Status updated.");
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

const archive = async () => {
  archiveError.value = "";
  const ok = await confirm({
    title: "Archive tenant",
    message: `Archive "${tenant.value.name}"?`,
    confirmLabel: "Archive",
    type: "danger"
  });
  if (!ok) return;
  try {
    await tenantApi.archive(tenantId);
    notify.success("Tenant archived.");
    // An archived tenant is no longer somewhere to look, so it must leave the "View as" menu with it.
    refreshTenants();
    router.push({ name: "tenants" });
  } catch (err) {
    archiveError.value = getApiErrorMessage(err);
  }
};

// ---------------------------------------------------------------------------------------------------
// The tenant's user accounts
// ---------------------------------------------------------------------------------------------------
// Asked.
const canReadUsers = computed(() => has(Permissions.UsersRead));
const canWriteUsers = computed(() => has(Permissions.UsersWrite));
const canResetPassword = computed(() => has(Permissions.UsersResetPassword));

const createUserOpen = ref(false);
const tempPwOpen = ref(false);
const tempPassword = ref("");

// No Tenant column and no Department column: every row here is this tenant's, and a department is read
// through the caller's ACTIVE tenant, which this one need not be — the server sends none.
const userColumns = [
  { name: "fullName", label: "Name", field: "fullName", align: "left", sortable: true, default: true },
  { name: "email", label: "Email", field: "email", align: "left", sortable: true, default: true },
  { name: "phoneNumber", label: "Phone", field: "phoneNumber", align: "left", sortable: true },
  { name: "roles", label: "Role", field: (r) => (r.roles || []).join(", "), align: "left", sortable: false, default: true },
  { name: "isActive", label: "Status", field: "isActive", align: "left", sortable: true, default: true },
  ...auditColumns(),
  { name: "actions", label: "Actions", field: "actions", align: "left" }
];

const {
  rows: userRows, loading: loadingUsers, totalRecords: totalUsers, search: userSearch,
  pagination: userPagination, load: loadUsers, onRequest: onUsersRequest
} = useListTable({
  pageKey: "tenant-users",
  fetcher: ({ page, limit, sortBy, descending }) =>
    userApi.list({ page, limit, sortBy, descending, tenantId, search: userSearch.value || undefined })
      .then((r) => ({ data: r?.data, total: r?.meta?.totalRecords })),
  onError: (err) => notify.error(getApiErrorMessage(err)),
  reloadOnTenantSwitch: false
});

const reloadUsers = debounce(() => { userPagination.value.page = 1; loadUsers(); }, 300);
watch(userSearch, reloadUsers);

const setUserStatus = async (row, isActive) => {
  const ok = await confirm({
    title: isActive ? "Activate user" : "Deactivate user",
    message: `${isActive ? "Activate" : "Deactivate"} ${row.fullName}?`,
    type: isActive ? "primary" : "danger"
  });
  if (!ok) return;
  try {
    await userApi.setStatus(row.userId, isActive);
    notify.success("Status updated.");
    loadUsers();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

const resetUserPassword = async (row) => {
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

onMounted(load);
</script>

<style scoped>
.tenant-card {
  border-radius: 12px;
}
.danger-zone {
  border-color: var(--q-negative);
}
</style>
