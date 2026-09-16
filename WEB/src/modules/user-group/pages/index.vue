<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'User Groups' }]"
      :search="search"
      show-search
      search-placeholder="Search groups"
      show-filters
      :filter-count="filterChips.length"
      show-add
      add-label="Add Group"
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

    <div class="row q-col-gutter-md">
      <!-- Group list -->
      <div class="col-12 col-md-5">
        <app-data-table
          page-key="user-groups"
          row-key="id"
          title="All groups"
          :rows="filteredRows"
          :columns="columns"
          :loading="loading"
          :total-records="filteredRows.length"
          :pagination="pagination"
          default-sort-by="updatedOnUtc"
          @request="onRequest"
          @refresh="load"
        >
          <template #body-cell-memberCount="cell">
            <q-td :props="cell"><q-badge color="teal-1" text-color="primary">{{ cell.value }}</q-badge></q-td>
          </template>

          <template #body-cell-createdOnUtc="cell">
            <q-td :props="cell">
              {{ cell.value }}
              <q-icon name="o_info" size="14px" color="grey-6" class="q-ml-xs cursor-pointer">
                <q-tooltip>Created by {{ cell.row.createdBy || "Unknown" }}</q-tooltip>
              </q-icon>
            </q-td>
          </template>

          <template #body-cell-actions="cell">
            <q-td :props="cell">
              <q-btn type="a" flat round dense color="primary" icon="o_groups" @click="selectGroup(cell.row)">
                <q-tooltip>View members</q-tooltip>
              </q-btn>
              <q-btn type="a" flat round dense color="negative" icon="o_delete" @click="removeGroup(cell.row)">
                <q-tooltip>Delete</q-tooltip>
              </q-btn>
            </q-td>
          </template>

          <template #no-data>
            <div class="full-width column flex-center q-pa-xl text-grey-6">
              <q-icon name="o_groups" size="40px" class="q-mb-sm" />
              <div class="text-subtitle1 q-mb-xs">No groups yet</div>
              <q-btn unelevated no-caps color="primary" icon="o_add" label="Add Group" @click="openCreate" />
            </div>
          </template>
        </app-data-table>
      </div>

      <!-- Selected group's members -->
      <div class="col-12 col-md-7">
        <q-card v-if="!selectedGroup" flat bordered class="ug-card">
          <q-card-section class="text-subtitle1 text-weight-medium">Members</q-card-section>
          <q-separator />
          <q-card-section class="column flex-center q-pa-xl text-grey-6">
            <q-icon name="o_groups" size="36px" class="q-mb-sm" />
            Select a group to see its members.
          </q-card-section>
        </q-card>

        <app-data-table
          v-else
          row-key="userId"
          :title="`Members — ${selectedGroup.name}`"
          :rows="members"
          :columns="memberColumns"
          :loading="loadingMembers"
          :pagination="{ rowsPerPage: 10 }"
          default-sort-by="fullName"
          :default-descending="false"
          client-sort
          @refresh="loadMembers"
        >
          <!-- Add members sits beside the columns + refresh buttons in the table's top bar. -->
          <template #actions>
            <q-btn outline no-caps color="primary" icon="o_person_add" label="Add members" @click="openAddMembers" />
          </template>
          <template #body-cell-isActive="cell">
            <q-td :props="cell">
              <q-badge :color="cell.value ? 'positive' : 'grey'">{{ cell.value ? "Active" : "Inactive" }}</q-badge>
            </q-td>
          </template>
          <template #body-cell-addedOnUtc="cell">
            <q-td :props="cell">
              {{ cell.value }}
              <q-icon v-if="cell.row.addedBy" name="o_info" size="14px" color="grey-6" class="q-ml-xs cursor-pointer">
                <q-tooltip>Added by {{ cell.row.addedBy }}</q-tooltip>
              </q-icon>
            </q-td>
          </template>
          <template #body-cell-actions="cell">
            <q-td :props="cell">
              <q-btn type="a" flat round dense color="negative" icon="o_person_remove" @click="removeMember(cell.row)">
                <q-tooltip>Remove from group</q-tooltip>
              </q-btn>
            </q-td>
          </template>
          <template #no-data>
            <div class="full-width column flex-center q-pa-lg text-grey-6">
              <q-icon name="o_person_off" size="32px" class="q-mb-sm" />
              No members yet.
            </div>
          </template>
        </app-data-table>
      </div>
    </div>

    <!-- Below both panes: this restores GROUPS, not the memberships shown on the right. -->
    <deleted-records-panel
      v-if="canManageDeleted" :entity-type="EntityType.UserGroup" :show="showDeleted" @restored="load"
    />

    <!-- Create group dialog -->
    <q-dialog v-model="createOpen" persistent>
      <q-card style="min-width: 380px; max-width: 90vw;">
        <q-card-section class="text-h6">New group</q-card-section>
        <q-separator />
        <q-card-section>
          <q-form ref="createForm" greedy>
            <app-text-field v-model="newGroup.name" label="Name *" :rules="[(v) => !!v || 'Name is required']" class="q-mb-md" />
            <app-rich-text-field v-model="newGroup.description" label="Description" />
          </q-form>
        </q-card-section>
        <q-separator />
        <q-card-actions align="right">
          <q-btn flat no-caps color="grey-8" label="Cancel" @click="createOpen = false" />
          <q-btn unelevated no-caps color="primary" label="Create" :loading="creating" @click="submitCreate" />
        </q-card-actions>
      </q-card>
    </q-dialog>

    <!-- Add members dialog -->
    <q-dialog v-model="addOpen" persistent>
      <q-card style="min-width: 420px; max-width: 92vw;">
        <q-card-section class="text-h6">Add members</q-card-section>
        <q-separator />
        <q-card-section>
          <app-select
            v-model="addUserIds" :options="userOptions" :loading="loadingUsers"
            label="Users" multiple use-chips
            hint="Users already in the group are not listed."
          />
        </q-card-section>
        <q-separator />
        <q-card-actions align="right">
          <q-btn flat no-caps color="grey-8" label="Cancel" @click="addOpen = false" />
          <q-btn unelevated no-caps color="primary" label="Add" :loading="addingMembers" :disable="!addUserIds.length" @click="submitAddMembers" />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup>
import { ref, reactive, onMounted, watch } from "vue";
import { debounce } from "quasar";
import { userGroupApi, userApi, getApiErrorMessage, EntityType } from "services/api";
import { useNotify } from "composables/useNotify";
import { useDeletedRecords } from "composables/useDeletedRecords";
import { useConfirm } from "composables/useConfirm";
import { useDateFormat } from "composables/useDateFormat";
import { useListTable } from "composables/useListTable";
import { useColumnFilters } from "composables/useColumnFilters";
import { useAuditColumns } from "composables/useAuditColumns";
import AppListHeader from "components/common/AppListHeader.vue";
import DeletedRecordsPanel from "components/universal/DeletedRecordsPanel.vue";
import AppFilterDrawer from "components/common/AppFilterDrawer.vue";
import AppColumnFilters from "components/common/AppColumnFilters.vue";
import AppDataTable from "components/common/AppDataTable.vue";
import { stripHtml } from "utils/richText";
import AppTextField from "components/common/AppTextField.vue";
import AppRichTextField from "components/common/AppRichTextField.vue";
import AppSelect from "components/common/AppSelect.vue";

const auditColumns = useAuditColumns();
const { showDeleted, canManageDeleted } = useDeletedRecords();
const notify = useNotify();
const { confirm } = useConfirm();
const fmt = useDateFormat();

const columns = [
  { name: "name", label: "Name", field: "name", align: "left", sortable: true, default: true },
  // Descriptions are rich text; the cell shows the text without its markup (see utils/richText).
  { name: "description", label: "Description", field: (r) => stripHtml(r.description), align: "left", sortable: true },
  { name: "memberCount", label: "Members", field: "memberCount", align: "left", sortable: true, default: true, filterable: false },
  ...auditColumns(),
  { name: "actions", label: "Actions", field: "actions", align: "left" }
];

const memberColumns = [
  { name: "fullName", label: "Name", field: "fullName", align: "left", sortable: true, default: true },
  { name: "email", label: "Email", field: "email", align: "left", sortable: true, default: true },
  { name: "isActive", label: "Status", field: "isActive", align: "left", sortable: true },
  { name: "addedBy", label: "Added By", field: "addedBy", align: "left", sortable: true, default: true },
  { name: "addedOnUtc", label: "Added On", field: (r) => fmt.formatDateTime(r.addedOnUtc), sort: (r) => r.addedOnUtc || "", align: "left", sortable: true, default: true },
  { name: "actions", label: "Actions", field: "actions", align: "left" }
];

const { rows, loading, search, pagination, load, onRequest } = useListTable({
  pageKey: "user-groups",
  fetcher: ({ sortBy, descending }) =>
    userGroupApi.list({ search: search.value || undefined, sortBy, descending })
      .then((r) => ({ data: r || [], total: (r || []).length })),
  onError: (err) => notify.error(getApiErrorMessage(err))
});
const reload = debounce(() => { pagination.value.page = 1; load(); }, 300);
watch(search, reload);

// Client-side column filters (the list loads all groups); badge/count standard via AppListHeader.
const filterOpen = ref(false);
const { filters, filterableColumns, filteredRows, filterChips, removeFilter, clearFilters } = useColumnFilters(columns, rows, { server: false });

// ---- Members of the selected group ----
const selectedGroup = ref(null);
const members = ref([]);
const loadingMembers = ref(false);

const loadMembers = async () => {
  if (!selectedGroup.value) return;
  loadingMembers.value = true;
  try {
    members.value = (await userGroupApi.members(selectedGroup.value.id)) || [];
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loadingMembers.value = false;
  }
};

const selectGroup = async (g) => {
  selectedGroup.value = g;
  members.value = [];
  await loadMembers();
};

// ---- Create group ----
const createOpen = ref(false);
const creating = ref(false);
const createForm = ref(null);
const newGroup = reactive({ name: "", description: "" });

const openCreate = () => { newGroup.name = ""; newGroup.description = ""; createOpen.value = true; };

const submitCreate = async () => {
  if (!(await createForm.value?.validate())) return;
  creating.value = true;
  try {
    await userGroupApi.create({ name: newGroup.name, description: newGroup.description || null });
    notify.success("Group created.");
    createOpen.value = false;
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    creating.value = false;
  }
};

const removeGroup = async (g) => {
  const ok = await confirm({
    title: "Delete group",
    message: `Delete the group "${g.name}"? It will be removed from all users.`,
    confirmLabel: "Delete",
    type: "danger"
  });
  if (!ok) return;
  try {
    await userGroupApi.remove(g.id);
    notify.success("Group deleted.");
    if (selectedGroup.value?.id === g.id) { selectedGroup.value = null; members.value = []; }
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// ---- Add / remove members ----
const addOpen = ref(false);
const addingMembers = ref(false);
const addUserIds = ref([]);
const userOptions = ref([]);
const loadingUsers = ref(false);

const openAddMembers = async () => {
  addUserIds.value = [];
  addOpen.value = true;
  loadingUsers.value = true;
  try {
    const resp = await userApi.list({ page: 1, limit: 100 });
    const memberIds = new Set(members.value.map((m) => m.userId));
    userOptions.value = (resp?.data || [])
      .filter((u) => !memberIds.has(u.userId))
      .map((u) => ({ label: u.email ? `${u.fullName || u.email} (${u.email})` : (u.fullName || "User"), value: u.userId }));
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loadingUsers.value = false;
  }
};

const submitAddMembers = async () => {
  if (!addUserIds.value.length) return;
  addingMembers.value = true;
  try {
    await userGroupApi.addMembers(selectedGroup.value.id, addUserIds.value);
    notify.success("Members added.");
    addOpen.value = false;
    await loadMembers();
    load(); // refresh group member counts
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    addingMembers.value = false;
  }
};

const removeMember = async (m) => {
  const ok = await confirm({
    title: "Remove member",
    message: `Remove ${m.fullName} from "${selectedGroup.value.name}"?`,
    confirmLabel: "Remove",
    type: "danger"
  });
  if (!ok) return;
  try {
    await userGroupApi.removeMember(selectedGroup.value.id, m.userId);
    notify.success("Member removed.");
    await loadMembers();
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

onMounted(load);
</script>

<style scoped>
.ug-card {
  border-radius: 12px;
}
</style>
