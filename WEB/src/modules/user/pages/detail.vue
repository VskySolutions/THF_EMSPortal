<template>
  <q-page padding>
    <app-detail-header
      :items="[
        { label: 'Home', icon: 'o_home', to: '/' },
        { label: 'Users', to: { name: 'users' } },
        { label: user?.displayName || 'User' }
      ]"
      :back-to="{ name: 'users' }"
    />

    <div v-if="loading" class="row flex-center q-pa-xl"><q-spinner color="primary" size="40px" /></div>

    <div v-else-if="user">
      <!-- Who this is, at a glance: the identity, the standing, and the two actions that change it. -->
      <q-card flat bordered class="user-card q-mb-md">
        <!-- Avatar and identity share a line at every width; the actions drop beneath them on a phone
             rather than squeezing the name into a column two words wide. -->
        <q-card-section class="row items-center q-col-gutter-md">
          <!-- The avatar gets a column of its own. -->
          <div class="col-auto">
            <q-avatar size="72px" :color="user.isActive ? 'primary' : 'grey-5'" text-color="white">
              <img v-if="avatarUrl" :src="avatarUrl" alt="">
              <span v-else class="text-h5 text-weight-medium text-white">{{ initials }}</span>
            </q-avatar>
          </div>

          <div class="col" style="min-width: 0;">
            <div class="row items-center q-gutter-sm">
              <div class="text-h6 text-weight-bold ellipsis">{{ user.fullName || user.displayName }}</div>
              <q-badge :color="user.isActive ? 'positive' : 'grey-6'">{{ user.isActive ? "Active" : "Inactive" }}</q-badge>
              <!-- Surfaced because it explains a sign-in the admin may be about to be asked about. -->
              <q-badge v-if="user.mustChangePassword" color="orange-8">
                <q-icon name="o_key" size="13px" class="q-mr-xs" />Must change password
              </q-badge>
            </div>
            <div class="text-body2 text-grey-7 ellipsis">{{ user.email }}</div>
            <div v-if="user.phoneNumber" class="text-body2 text-grey-7">{{ user.phoneNumber }}</div>

            <div class="row items-center q-gutter-xs q-mt-sm">
              <q-chip v-if="user.department" dense color="blue-grey-1" text-color="blue-grey-8" icon="o_apartment">
                {{ departmentLabel(user.department) }}<span v-if="user.isDepartmentHead"> · Head</span>
              </q-chip>
              <!-- Every role held where the caller can see it, so "what can this person do" is answered
                   here rather than only in the assignments card. -->
              <q-chip
                v-for="r in summaryRoles" :key="r.key" dense size="sm"
                :color="roleCategoryChip(r.name).color" text-color="white" class="text-capitalize"
              >
                {{ r.name }}
                <q-tooltip>{{ r.tenantName }} · {{ roleCategoryChip(r.name).category }}</q-tooltip>
              </q-chip>
              <span v-if="!summaryRoles.length" class="text-caption text-grey-6">No roles assigned</span>
            </div>
          </div>

          <div class="col-12 col-sm-auto column q-gutter-sm">
            <q-btn
              v-if="canEdit" unelevated no-caps :color="user.isActive ? 'negative' : 'positive'"
              :icon="user.isActive ? 'o_block' : 'o_check_circle'"
              :label="user.isActive ? 'Deactivate' : 'Activate'"
              :disable="!canManageTarget" @click="toggleStatus"
            >
              <q-tooltip v-if="!canManageTarget">Only a Super Admin can manage this user.</q-tooltip>
            </q-btn>
            <q-btn
              v-if="canResetPassword" outline no-caps color="primary" icon="o_lock_reset" label="Reset password"
              :disable="!canManageTarget" @click="resetPassword"
            >
              <q-tooltip v-if="!canManageTarget">Only a Super Admin can reset this user.</q-tooltip>
            </q-btn>
            <!-- The profile behind the account: names, contact details and the rest live on the Person
                 record, and this is the only route to it from here. -->
            <q-btn
              v-if="user.personId && canReadPersons" flat no-caps color="primary" icon="o_badge"
              label="Person record" :to="{ name: 'person_detail', params: { id: user.personId } }"
            />
          </div>
        </q-card-section>
      </q-card>

      <div class="row q-col-gutter-md">
        <!-- Left: who they are. -->
        <div class="col-12 col-md-7">
          <!-- Basic info -->
          <q-card flat bordered class="user-card q-mb-md">
            <q-card-section class="row items-center q-gutter-sm">
              <q-icon name="o_badge" color="primary" size="sm" />
              <div class="text-subtitle1 text-weight-medium">Basic information</div>
              <app-info-tip
                v-if="canEdit"
                text="Saves as you leave each field. The username is the exception: it is the sign-in credential, so changing it asks first and signs the user out of their sessions."
              />
              <q-space />
              <app-auto-save-state :state="basicSave.state" :message="basicSave.message" />
            </q-card-section>
            <q-separator />
            <q-card-section class="row q-col-gutter-md">
              <app-text-field
                v-model="firstName" label="First Name" class="col-12 col-sm-6"
                :readonly="!canEdit" :rules="nameRules('First name')" @blur="autoSaveBasics"
              />
              <app-text-field
                v-model="lastName" label="Last Name" class="col-8 col-sm-4"
                :readonly="!canEdit" :rules="nameRules('Last name')" @blur="autoSaveBasics"
              />
              <!-- The generational particle on the name, after the surname where it is read. -->
              <app-name-suffix-field
                v-model="suffix" class="col-4 col-sm-2" :readonly="!canEdit" @blur="autoSaveBasics"
              />
              <!-- This IS the sign-in credential, not a contact address — labelled so whoever edits it
                   knows, and committed through its own path rather than saved on the way past. -->
              <app-text-field
                v-model="email" label="Username (Email)" type="email" class="col-12 col-sm-6"
                :readonly="!canEdit" hint="Used to sign in. Changing it signs the user out of their sessions."
                @blur="commitEmail"
              />
              <app-text-field
                v-model="phoneNumber" label="Phone Number" class="col-12 col-sm-6"
                :readonly="!canEdit" @blur="autoSaveBasics"
              />
            </q-card-section>
          </q-card>

          <!-- Department. Per-tenant, and the head of a department is that department's REMS Director. -->
          <q-card flat bordered class="user-card q-mb-md">
            <q-card-section class="row items-center q-gutter-sm">
              <q-icon name="o_apartment" color="primary" size="sm" />
              <div class="text-subtitle1 text-weight-medium">Department</div>
              <app-info-tip
                v-if="canEdit && inActiveTenant"
                text="Saves as soon as you change it. Taking a headship from somebody else asks first — the head of a department is its REMS Department Director, and that is who new engagements are routed to."
              />
              <q-space />
              <app-auto-save-state :state="departmentSave.state" :message="departmentSave.message" />
            </q-card-section>
            <q-separator />
            <q-card-section v-if="!inActiveTenant" class="text-grey-6">
              Assign this user to the active tenant before setting a department.
            </q-card-section>
            <q-card-section v-else class="row q-col-gutter-md items-start">
              <app-select
                v-model="department" :options="departmentOptions" :loading="loadingDepartments" label="Department"
                class="col-12 col-sm-6" :readonly="!canEdit"
                info="From the REMS Department option list (Administration → Option Sets). A department has one head, and that head is its REMS Department Director."
                @update:model-value="onDepartmentChange"
              />
              <div class="col-12 col-sm-6">
                <q-toggle
                  v-model="isDepartmentHead" :disable="!canEdit || !department" label="Department head"
                  @update:model-value="autoSaveDepartment"
                />
                <div class="text-caption text-grey-7 q-mt-xs">{{ headHint }}</div>
              </div>
            </q-card-section>
          </q-card>
        </div>

        <div class="col-12 col-md-5">
          <!-- Tenant assignments (requires roles.assign — Super Admins tenant-wide, Tenant Admins within
               their own tenant only, and never on a Super Admin target; the API enforces the same). -->
          <q-card v-if="canManageAssignments" flat bordered class="user-card q-mb-md">
            <q-card-section class="row items-center q-gutter-sm">
              <q-icon name="o_key" color="primary" size="sm" />
              <div class="text-subtitle1 text-weight-medium">Tenants &amp; roles</div>
              <q-space />
              <q-btn
                unelevated no-caps dense color="primary" icon="o_add" label="Add"
                :disable="!canManageTarget" @click="openAssign"
              >
                <q-tooltip v-if="!canManageTarget">Only a Super Admin can manage this user.</q-tooltip>
              </q-btn>
            </q-card-section>
            <q-separator />
            <q-banner v-if="visibleAssignments.length === 1" dense class="bg-orange-1 text-orange-9">
              <template #avatar><q-icon name="o_warning" color="orange" /></template>
              This is the user's only tenant assignment.
            </q-banner>
            <q-list separator>
              <q-item v-for="a in visibleAssignments" :key="a.tenantId">
                <q-item-section>
                  <q-item-label class="text-weight-medium">{{ tenantName(a.tenantId) }}</q-item-label>
                  <!-- All roles held in this tenant, each chip coloured by its category (AC-ADM-006.6). -->
                  <q-item-label caption>
                    <div class="row items-center q-gutter-xs q-mt-xs">
                      <q-chip
                        v-for="r in (a.roles || [])" :key="r.roleId" dense size="sm"
                        :color="roleCategoryChip(r.roleName || r.role).color" text-color="white" class="text-capitalize"
                      >
                        {{ r.roleName || r.role }}
                        <q-tooltip>{{ roleCategoryChip(r.roleName || r.role).category }}</q-tooltip>
                      </q-chip>
                      <span v-if="!(a.roles || []).length" class="text-grey-6">No roles</span>
                    </div>
                  </q-item-label>
                </q-item-section>
                <!-- Icons rather than a labelled button: this column is half the width the card had. -->
                <q-item-section side>
                  <div class="row items-center no-wrap">
                    <q-btn
                      flat round dense color="primary" icon="o_manage_accounts"
                      :disable="!canManageTarget" @click="openChangeRole(a)"
                    >
                      <q-tooltip>{{ canManageTarget ? "Manage roles" : "Only a Super Admin can manage this user." }}</q-tooltip>
                    </q-btn>
                    <q-btn
                      flat round dense color="negative" icon="o_delete"
                      :disable="!canManageTarget" @click="removeAssignment(a)"
                    >
                      <q-tooltip>{{ canManageTarget ? "Remove" : "Only a Super Admin can manage this user." }}</q-tooltip>
                    </q-btn>
                  </div>
                </q-item-section>
              </q-item>
              <q-item v-if="!visibleAssignments.length">
                <q-item-section class="text-grey-6">No assignments.</q-item-section>
              </q-item>
            </q-list>
          </q-card>

          <!-- Groups -->
          <q-card flat bordered class="user-card q-mb-md">
            <q-card-section class="row items-center q-gutter-sm">
              <q-icon name="o_groups" color="primary" size="sm" />
              <div class="text-subtitle1 text-weight-medium">Groups</div>
              <q-space />
              <q-btn
                v-if="canManageGroups" outline no-caps dense color="primary" icon="o_group_add"
                label="Manage" @click="openGroups"
              />
            </q-card-section>
            <q-separator />
            <q-card-section>
              <div v-if="(user.groups || []).length" class="row q-gutter-sm">
                <q-chip v-for="g in user.groups" :key="g.id" dense color="teal-1" text-color="primary" icon="o_groups">
                  {{ g.name }}
                </q-chip>
              </div>
              <div v-else class="text-grey-6">Not a member of any group.</div>
            </q-card-section>
          </q-card>

          <!-- REMS delegation, arranged on the user's behalf. -->
          <rems-delegates-panel
            v-if="canManageDelegates && inActiveTenant" :principal-user-id="userId"
            :principal-name="user?.displayName || 'this user'" class="q-mb-md"
          />
        </div>
      </div>

      <app-record-audit :audit="user.audit" />
    </div>

    <!-- Manage groups dialog -->
    <q-dialog v-model="groupsOpen" persistent>
      <q-card style="min-width: 420px; max-width: 92vw;">
        <q-card-section class="text-h6">Manage groups</q-card-section>
        <q-separator />
        <q-card-section>
          <!-- Create a new group inline. -->
          <div class="row items-center q-col-gutter-sm q-mb-md">
            <app-text-field v-model="newGroupName" label="Create a new group" placeholder="e.g. Finance Team" class="col" @keyup.enter="createGroup" />
            <q-btn outline no-caps color="primary" icon="o_add" label="Create" :loading="creatingGroup" :disable="!newGroupName.trim()" @click="createGroup" />
          </div>
          <!-- Tick the groups this user belongs to. The info icon shows who created each group and when. -->
          <q-list bordered separator class="rounded-borders" style="max-height: 320px; overflow: auto;">
            <q-item v-for="g in groupList" :key="g.id" v-ripple tag="label">
              <q-item-section avatar>
                <q-checkbox v-model="selectedGroupIds" :val="g.id" />
              </q-item-section>
              <q-item-section>
                <q-item-label class="row items-center">
                  {{ g.name }}
                  <q-icon name="o_info" size="16px" color="grey-6" class="q-ml-xs cursor-pointer">
                    <q-tooltip>
                      Created by {{ g.createdBy || "Unknown" }}<template v-if="g.createdOnUtc"> · {{ fmt.formatDateTime(g.createdOnUtc) }}</template>
                    </q-tooltip>
                  </q-icon>
                </q-item-label>
                <q-item-label v-if="g.memberCount != null" caption>{{ g.memberCount }} member(s)</q-item-label>
              </q-item-section>
              <q-item-section side>
                <q-btn flat round dense color="negative" icon="o_delete" @click.prevent.stop="deleteGroup(g)">
                  <q-tooltip>Delete group</q-tooltip>
                </q-btn>
              </q-item-section>
            </q-item>
            <q-item v-if="!loadingGroups && !groupList.length">
              <q-item-section class="text-grey-6">No groups yet. Create one above.</q-item-section>
            </q-item>
          </q-list>
        </q-card-section>
        <q-separator />
        <q-card-actions align="right">
          <q-btn flat no-caps color="grey-8" label="Cancel" @click="groupsOpen = false" />
          <q-btn unelevated no-caps color="primary" label="Save" :loading="savingGroups" @click="saveGroups" />
        </q-card-actions>
      </q-card>
    </q-dialog>

    <!-- Add assignment -->
    <app-form-drawer v-model="assignOpen" :title="assignTitle" :saving="assignSaving" @submit="submitAssign" @cancel="assignOpen = false">
      <q-form ref="assignForm" greedy>
        <app-select
          v-if="isPlatformAdmin" v-model="assign.tenantId" :options="tenantOptions" :loading="loadingTenants"
          label="Tenant *" class="q-mb-md" :clearable="false" :disable="assignMode === 'edit'" @update:model-value="onAssignTenantChange"
        />
        <app-select
          v-model="assign.roleIds" :options="roleOptions" :loading="loadingRoles" label="Roles *" multiple
          hint="Grouped by category. The assignment is reconciled to exactly these roles."
          info="The roles assignable in the selected tenant, grouped System / Operational / Custom. Super Admin is only listed for a Super Admin."
        />
      </q-form>
    </app-form-drawer>

    <temp-password-dialog v-model="tempPwOpen" :password="tempPassword" />
  </q-page>
</template>

<script setup>
import { ref, reactive, computed, onMounted } from "vue";
import { useRoute } from "vue-router";
import { userApi, tenantApi, userGroupApi, mediaApi, getApiErrorMessage } from "services/api";
import { useTenantStore } from "stores/tenant";
import { usePermissions, Permissions } from "composables/usePermissions";
import { useRoleOptions, roleCategoryChip } from "composables/useRoleOptions";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useDateFormat } from "composables/useDateFormat";
import { nameRules } from "utils/personName";
import AppDetailHeader from "components/common/AppDetailHeader.vue";
import AppRecordAudit from "components/common/AppRecordAudit.vue";
import AppFormDrawer from "components/common/AppFormDrawer.vue";
import AppSelect from "components/common/AppSelect.vue";
import AppTextField from "components/common/AppTextField.vue";
import AppNameSuffixField from "components/common/AppNameSuffixField.vue";
import AppInfoTip from "components/common/AppInfoTip.vue";
import AppAutoSaveState from "components/common/AppAutoSaveState.vue";
import TempPasswordDialog from "components/temp_password_dialog.vue";
import RemsDelegatesPanel from "modules/rems/components/RemsDelegatesPanel.vue";

const route = useRoute();
const notify = useNotify();
const { confirm } = useConfirm();
const fmt = useDateFormat();
const tenantStore = useTenantStore();
const { has } = usePermissions();
// Platform admins (tenants.write) manage any user/tenant; tenant admins (roles.assign) are scoped.
const isPlatformAdmin = computed(() => has(Permissions.TenantsWrite));
const canEdit = computed(() => has(Permissions.UsersWrite));
const canManageAssignments = computed(() => has(Permissions.RolesAssign));
const canResetPassword = computed(() => has(Permissions.UsersResetPassword));
const canManageGroups = computed(() => has(Permissions.UsersGroupManagement));
// Arranging somebody else's REMS cover is its own right, not a side effect of being able to edit their
// account — the panel and the endpoints behind it agree on that.
const canManageDelegates = computed(() => has(Permissions.RemsDelegationsManage));
// The Person record behind the account is a separate page with its own permission; the link to it is
// only offered to somebody who could actually open it.
const canReadPersons = computed(() => has(Permissions.PersonsRead));

const userId = route.params.id;
const user = ref(null);
const loading = ref(false);
// The generational particle on the person's name.
const suffix = ref("");
const firstName = ref("");
const lastName = ref("");
const email = ref("");
const phoneNumber = ref("");
const tenantOptions = ref([]);
const loadingTenants = ref(false);

// Grouped, category-labelled multi-role options (SuperAdmin excluded for non-Super-Admin callers).
const { roleOptions, loading: loadingRoles, loadForTenant } = useRoleOptions();

// Initials for the summary avatar, from the display name (two words at most, like the account page).
// The picture the person set on their own profile, when there is one.
const avatarUrl = computed(() => mediaApi.absoluteUrl(user.value?.profileMediaUrl));

// The stand-in when there is not: first and last initial.
const initials = computed(() => {
  const u = user.value;
  if (!u) return "?";
  const fromFields = [u.firstName, u.lastName]
    .map((n) => (n || "").trim().charAt(0))
    .join("");
  if (fromFields) return fromFields.toUpperCase();

  // No names on the person record — fall back to the first and last word of whatever we display.
  const words = (u.fullName || u.displayName || "").trim().split(/\s+/).filter(Boolean);
  if (!words.length) return "?";
  const last = words.length > 1 ? words[words.length - 1].charAt(0) : "";
  return (words[0].charAt(0) + last).toUpperCase();
});

// Every role the caller can see this person holding, flattened for the summary chips.
const summaryRoles = computed(() =>
  visibleAssignments.value.flatMap((a) =>
    (a.roles || []).map((r) => ({
      key: `${a.tenantId}:${r.roleId}`,
      name: r.roleName || r.role,
      tenantName: tenantName(a.tenantId)
    }))));

const targetIsSuperAdmin = computed(() =>
  !!user.value?.assignments?.some((a) => (a.roles || []).some((r) => r.role === "SuperAdmin")));
const canManageTarget = computed(() => isPlatformAdmin.value || !targetIsSuperAdmin.value);

// Platform admins see every assignment; tenant admins see only their active tenant's.
const visibleAssignments = computed(() => {
  const all = user.value?.assignments || [];
  return isPlatformAdmin.value ? all : all.filter((a) => a.tenantId === tenantStore.activeTenantId);
});

const tenantName = (id) => {
  const opt = tenantOptions.value.find((t) => t.value === id);
  if (opt) return opt.label;
  // Fall back to the signed-in user's own tenant list (it carries names) — covers Tenant Admins, who
  // cannot list all tenants, and any case where the tenant options failed to load.
  const mine = (tenantStore.assignments || []).find((t) => t.tenantId === id);
  return mine?.name || mine?.identifier || id;
};

const loadTenants = async () => {
  if (!isPlatformAdmin.value) return;
  loadingTenants.value = true;
  try {
    const resp = await tenantApi.list({ page: 1, limit: 100 });
    tenantOptions.value = (resp?.data || []).map((t) => ({ label: t.name, value: t.tenantId }));
  } catch {
    // non-fatal
  } finally {
    loadingTenants.value = false;
  }
};

// Roles assignable within a tenant (system + the tenant's custom roles), grouped by category.
const loadRoles = async (tenantId) => {
  try {
    await loadForTenant(tenantId);
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// `syncFields` seeds the inputs from the record.
const load = async ({ syncFields = true } = {}) => {
  loading.value = !user.value;
  try {
    user.value = await userApi.get(userId);
    if (!syncFields) return;
    suffix.value = user.value.suffix || "";
    firstName.value = user.value.firstName || "";
    lastName.value = user.value.lastName || "";
    phoneNumber.value = user.value.phoneNumber || "";
    email.value = user.value.email;
    department.value = user.value.department || null;
    isDepartmentHead.value = !!user.value.isDepartmentHead;
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loading.value = false;
  }
};

// ---- Department ----
// Scoped to the active tenant.
const department = ref(null);
const isDepartmentHead = ref(false);
const departmentOptions = ref([]);
const departmentHeads = ref([]);
const loadingDepartments = ref(false);

const sameId = (a, b) => String(a || "").toLowerCase() === String(b || "").toLowerCase();

// These roles only mean something inside a tenant, so the section needs an assignment in the active one.
const inActiveTenant = computed(() =>
  (user.value?.assignments || []).some((a) => a.tenantId === tenantStore.activeTenantId));

const departmentLabel = (code) => departmentOptions.value.find((o) => o.value === code)?.label || code;

// Who holds the selected department today (possibly this same user).
const currentHead = computed(() => departmentHeads.value.find((h) => h.department === department.value) || null);

const headHint = computed(() => {
  if (!department.value) return "Pick a department to set a head.";
  if (!currentHead.value) return `${departmentLabel(department.value)} has no head yet.`;
  if (sameId(currentHead.value.userId, userId)) return "Heads this department, and is its REMS Department Director.";
  return `${currentHead.value.fullName} currently heads ${departmentLabel(department.value)}.`;
});

const departmentDirty = computed(() =>
  (department.value || null) !== (user.value?.department || null) ||
  isDepartmentHead.value !== !!user.value?.isDepartmentHead);

// Moving to a different department never carries headship across — it has to be granted deliberately.
const onDepartmentChange = (value) => {
  department.value = value;
  isDepartmentHead.value = value && value === user.value?.department ? !!user.value.isDepartmentHead : false;
  // Picking a department is the whole edit — there is no second field to fill in before it means
  // something, so it commits here rather than waiting for a button that no longer exists.
  autoSaveDepartment();
};

const loadDepartments = async () => {
  loadingDepartments.value = true;
  try {
    const result = await userApi.departments();
    departmentOptions.value = result?.departments || [];
    departmentHeads.value = result?.heads || [];
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loadingDepartments.value = false;
  }
};

// ---- Auto-save ----
// The cards on this page save themselves: a field commits when you leave it, a picker when you change it.
const basicSave = reactive({ state: "idle", message: "" });
const departmentSave = reactive({ state: "idle", message: "" });

const runAutoSave = async (target, fn) => {
  target.state = "saving";
  target.message = "";
  try {
    await fn();
    target.state = "saved";
    // The tick fades; an error does not, because an error is still true until something changes it.
    setTimeout(() => { if (target.state === "saved") target.state = "idle"; }, 2500);
    return true;
  } catch (err) {
    target.state = "error";
    target.message = getApiErrorMessage(err);
    return false;
  }
};

// The name and phone fields.
const autoSaveBasics = async () => {
  const u = user.value;
  if (!canEdit.value || !u) return;
  if (suffix.value === (u.suffix || "") &&
    firstName.value === (u.firstName || "") &&
    lastName.value === (u.lastName || "") &&
    phoneNumber.value === (u.phoneNumber || "")) {
    return;
  }

  await runAutoSave(basicSave, async () => {
    await userApi.update(userId, {
      // "" rather than null: the endpoint reads an omitted field as "leave it alone", so a suffix taken
      // back off would otherwise stay on the record.
      suffix: suffix.value || "",
      firstName: firstName.value,
      lastName: lastName.value,
      phoneNumber: phoneNumber.value,
      email: u.email
    });
    await load({ syncFields: false });
  });
};

// The username is the one field that cannot simply be saved on the way past: the API only rejects
// duplicates, so the format is checked here.
const commitEmail = async () => {
  const u = user.value;
  if (!canEdit.value || !u) return;

  const next = (email.value || "").trim();
  const current = u.email || "";
  if (next.toLowerCase() === current.toLowerCase()) {
    email.value = current; // tidies whitespace and case-only edits away
    return;
  }

  if (!/^\S+@\S+\.\S+$/.test(next)) {
    notify.error("Enter a valid username (email address).");
    email.value = current;
    return;
  }

  const ok = await confirm({
    title: "Change username",
    message: `Change the sign-in username for ${u.displayName} from "${current}" to "${next}"? ` +
      "They will be signed out and must sign in with the new username.",
    confirmLabel: "Change username"
  });
  if (!ok) {
    email.value = current;
    return;
  }

  const saved = await runAutoSave(basicSave, async () => {
    await userApi.update(userId, {
      suffix: suffix.value || "",
      firstName: firstName.value,
      lastName: lastName.value,
      phoneNumber: phoneNumber.value,
      email: next
    });
    await load({ syncFields: false });
  });
  if (!saved) {
    // Rejected (a duplicate, most likely). The indicator carries the reason; the field goes back to the
    // username that is actually in force, so nobody reads the page as if the change had taken.
    notify.error(basicSave.message);
    email.value = current;
  }
};

// The department picker and the head toggle. Taking a headship off somebody still asks first — that is a
// decision about another person, not a preference — and a refusal puts the control back where it was.
const revertDepartment = () => {
  department.value = user.value?.department || null;
  isDepartmentHead.value = !!user.value?.isDepartmentHead;
};

const autoSaveDepartment = async () => {
  if (!canEdit.value || !inActiveTenant.value || !departmentDirty.value) return;

  if (isDepartmentHead.value && currentHead.value && !sameId(currentHead.value.userId, userId)) {
    const ok = await confirm({
      title: "Change department head",
      message: `${currentHead.value.fullName} currently heads ${departmentLabel(department.value)}. Make ` +
        `${user.value.displayName} the head instead? ${currentHead.value.fullName} will no longer be the ` +
        "Department Director on new engagements.",
      confirmLabel: "Make head"
    });
    if (!ok) {
      revertDepartment();
      return;
    }
  }

  const saved = await runAutoSave(departmentSave, async () => {
    const result = await userApi.setDepartment(userId, {
      department: department.value,
      isHead: isDepartmentHead.value
    });
    // Somebody else lost their headship: too consequential to leave to a tick in the corner.
    if (result?.demotedHeadName) {
      notify.info(`${result.demotedHeadName} is no longer the department head.`);
    }
    await Promise.all([loadDepartments(), load({ syncFields: false })]);
  });
  if (!saved) {
    revertDepartment();
  }
};
const toggleStatus = async () => {
  const activate = !user.value.isActive;
  const ok = await confirm({
    title: activate ? "Activate user" : "Deactivate user",
    message: `${activate ? "Activate" : "Deactivate"} ${user.value.displayName}?`,
    type: activate ? "primary" : "danger"
  });
  if (!ok) return;
  try {
    await userApi.setStatus(userId, activate);
    notify.success("Status updated.");
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

const tempPwOpen = ref(false);
const tempPassword = ref("");
const resetPassword = async () => {
  const ok = await confirm({
    title: "Reset password",
    message: `Generate a new temporary password for ${user.value.displayName}? Their current sessions will end.`,
    confirmLabel: "Reset",
    type: "danger"
  });
  if (!ok) return;
  try {
    const result = await userApi.resetPassword(userId);
    tempPassword.value = result?.temporaryPassword || "";
    tempPwOpen.value = true;
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// Assignments
const assignOpen = ref(false);
const assignSaving = ref(false);
const assignForm = ref(null);
const assign = reactive({ tenantId: null, roleIds: [] });
// "add" = new tenant assignment; "edit" = manage the roles on an existing assignment (tenant locked).
const assignMode = ref("add");
const assignTitle = computed(() => (assignMode.value === "edit" ? "Manage Roles" : "Add assignment"));

const onAssignTenantChange = (tenantId) => {
  assign.tenantId = tenantId;
  assign.roleIds = [];
  loadRoles(assign.tenantId);
};

// Manage the roles on an existing assignment: pre-fill the tenant (locked) + its current role set, then
// reuse the assign endpoint which reconciles the assignment to exactly the submitted roles.
const openChangeRole = async (a) => {
  assignMode.value = "edit";
  assign.tenantId = a.tenantId;
  await loadRoles(a.tenantId);
  assign.roleIds = (a.roles || []).map((r) => r.roleId);
  assignOpen.value = true;
};

const openAssign = async () => {
  assignMode.value = "add";
  assign.roleIds = [];
  if (isPlatformAdmin.value) {
    assign.tenantId = null;
    await loadRoles(null); // clear any roles carried over from a previous open
    await loadTenants();
  } else {
    // Tenant Admin: assignment is scoped to their active tenant.
    assign.tenantId = tenantStore.activeTenantId;
    await loadRoles(assign.tenantId);
  }
  assignOpen.value = true;
};

const submitAssign = async ({ clearDraft } = {}) => {
  if (!(await assignForm.value?.validate())) return;
  if (!assign.tenantId) {
    notify.error("Select a tenant.");
    return;
  }
  if (!assign.roleIds.length) {
    notify.error("Select at least one role.");
    return;
  }
  assignSaving.value = true;
  try {
    // Reconcile: the endpoint adds/removes so the tenant's role set matches exactly (no remove/re-add).
    await userApi.assignTenantRole(userId, { tenantId: assign.tenantId, roleIds: assign.roleIds });
    notify.success("Assignment saved.");
    clearDraft?.();
    assignOpen.value = false;
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    assignSaving.value = false;
  }
};

const removeAssignment = async (a) => {
  const roleLabel = (a.roles || []).map((r) => r.roleName || r.role).join(", ") || "all roles";
  const ok = await confirm({
    title: "Remove assignment",
    message: `Remove ${roleLabel} on ${tenantName(a.tenantId)}?`,
    confirmLabel: "Remove",
    type: "danger"
  });
  if (!ok) return;
  try {
    await userApi.removeTenantRole(userId, a.tenantId);
    notify.success("Assignment removed.");
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// ---- Groups ----
const groupsOpen = ref(false);
const groupList = ref([]);
const loadingGroups = ref(false);
const selectedGroupIds = ref([]);
const newGroupName = ref("");
const creatingGroup = ref(false);
const savingGroups = ref(false);

const loadGroups = async () => {
  loadingGroups.value = true;
  try {
    groupList.value = (await userGroupApi.list()) || [];
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loadingGroups.value = false;
  }
};

const openGroups = async () => {
  selectedGroupIds.value = (user.value?.groups || []).map((g) => g.id);
  newGroupName.value = "";
  groupsOpen.value = true;
  await loadGroups();
};

// Create a group inline and add it to the current selection.
const createGroup = async () => {
  const name = newGroupName.value.trim();
  if (!name) return;
  creatingGroup.value = true;
  try {
    const created = await userGroupApi.create({ name });
    if (!groupList.value.some((g) => g.id === created.id)) {
      groupList.value = [...groupList.value, created];
    }
    if (!selectedGroupIds.value.includes(created.id)) {
      selectedGroupIds.value = [...selectedGroupIds.value, created.id];
    }
    newGroupName.value = "";
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    creatingGroup.value = false;
  }
};

// Delete a group entirely (removes it for every user in the tenant).
const deleteGroup = async (g) => {
  const ok = await confirm({
    title: "Delete group",
    message: `Delete the group "${g.name}"? It will be removed from all users.`,
    confirmLabel: "Delete",
    type: "danger"
  });
  if (!ok) return;
  try {
    await userGroupApi.remove(g.id);
    groupList.value = groupList.value.filter((x) => x.id !== g.id);
    selectedGroupIds.value = selectedGroupIds.value.filter((id) => id !== g.id);
    notify.success("Group deleted.");
    load(); // refresh the user's chips in case they were a member
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

const saveGroups = async () => {
  savingGroups.value = true;
  try {
    await userApi.setGroups(userId, selectedGroupIds.value);
    notify.success("Groups updated.");
    groupsOpen.value = false;
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    savingGroups.value = false;
  }
};

onMounted(async () => {
  await Promise.all([loadTenants(), loadDepartments()]);
  await load();
});
</script>

<style scoped>
.user-card {
  border-radius: 12px;
}
</style>
