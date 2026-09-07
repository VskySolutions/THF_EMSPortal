<template>
  <q-page padding>
    <app-detail-header
      :items="[
        { label: 'Home', icon: 'o_home', to: '/' },
        { label: 'Roles', to: { name: 'roles' } },
        { label: role?.name || 'Role' }
      ]"
      :back-to="{ name: 'roles' }"
    />

    <div v-if="loading" class="row flex-center q-pa-xl"><q-spinner color="primary" size="40px" /></div>

    <div v-else-if="role">
      <!-- What this role is, before what it grants: the kind, whose it is, how much it carries and how many
           people hold it. -->
      <q-card flat bordered class="role-card q-mb-md">
        <q-card-section class="row items-center q-col-gutter-md">
          <!-- In its own column: a q-col-gutter row pads its direct children, and on an avatar that padding
               lands inside the circle. -->
          <div class="col-auto">
            <q-avatar
              size="72px" :color="role.isSystem ? 'blue-grey-6' : 'primary'" text-color="white"
              icon="o_admin_panel_settings"
            />
          </div>

          <div class="col" style="min-width: 0;">
            <div class="row items-center q-gutter-sm">
              <div class="text-h6 text-weight-bold ellipsis">{{ role.name }}</div>
              <q-badge :color="role.isSystem ? 'blue-grey' : 'primary'">
                {{ role.isSystem ? "System" : "Custom" }}
              </q-badge>
              <q-badge :color="role.tenantId ? 'teal' : 'indigo-5'">
                {{ scopeLabel }}
                <q-tooltip>{{ scopeExplainer }}</q-tooltip>
              </q-badge>
            </div>
            <div class="text-body2 text-grey-7 q-mt-xs">{{ descriptionText || "No description." }}</div>

            <div class="row items-center q-gutter-xs q-mt-sm">
              <q-chip dense icon="o_vpn_key" color="blue-grey-1" text-color="blue-grey-8">
                {{ permissionSummary }}
              </q-chip>
              <q-chip v-if="canManageMembers" dense icon="o_group" color="blue-grey-1" text-color="blue-grey-8">
                {{ memberSummary }}
              </q-chip>
              <span class="text-caption text-grey-6 q-ml-sm">Updated {{ fmt.formatDateTime(role.audit?.updatedOnUtc) }}</span>
            </div>
          </div>

          <div class="col-12 col-sm-auto column q-gutter-sm">
            <q-btn
              v-if="isSuperAdmin && !role.tenantId && !role.isSystem" outline no-caps color="primary"
              icon="o_apartment" label="Manage tenants" @click="openTenants"
            />
            <q-btn
              v-if="role.canManage && !role.isSystem" flat no-caps color="negative"
              icon="o_delete" label="Delete role" @click="removeRole"
            />
          </div>
        </q-card-section>
      </q-card>

      <!-- Read-only for this caller: said once, at the top, rather than left to be inferred from fields
           that will not take a keystroke. -->
      <q-banner v-if="!role.canManage" dense rounded class="bg-blue-grey-1 text-blue-grey-9 q-mb-md">
        <template #avatar><q-icon name="o_lock" color="blue-grey-7" /></template>
        This role belongs to the platform, so what it grants is a Super Admin's to change — create a role
        of your own for a different set of permissions. Who holds it in your tenant is still yours to
        manage, on the right.
      </q-banner>

      <div class="row q-col-gutter-md">
        <div class="col-12 col-md-7">
          <!-- Definition -->
          <q-card flat bordered class="role-card q-mb-md">
            <q-card-section class="row items-center q-gutter-sm">
              <q-icon name="o_edit_note" color="primary" size="sm" />
              <div class="text-subtitle1 text-weight-medium">Definition</div>
              <app-info-tip v-if="role.canManage" :text="definitionHint" />
              <q-space />
              <app-auto-save-state :state="definitionSave.state" :message="definitionSave.message" />
            </q-card-section>
            <q-separator />
            <q-card-section>
              <app-text-field
                v-model="form.name" label="Name" required class="q-mb-md"
                :readonly="!canEditName"
                :hint="role.isSystem ? 'System role names are fixed; permissions can still be tuned.' : undefined"
                @blur="autoSaveName"
              />
              <app-rich-text-field
                v-model="form.description" label="Description" class="q-mb-md" :readonly="!role.canManage"
              />

              <!-- Read-only, the keys are listed rather than put in a picker: the catalogue a tenant admin
                   is offered stops at their ceiling, so a platform role's wider set has nothing to map to. -->
              <div v-if="!role.canManage">
                <div class="text-caption text-grey-7 q-mb-xs">Permissions</div>
                <div v-if="form.permissions.length" class="row q-gutter-xs">
                  <q-chip
                    v-for="permission in form.permissions" :key="permission"
                    dense square color="grey-3" text-color="grey-9"
                  >
                    {{ prettyPermission(permission) }}
                  </q-chip>
                </div>
                <div v-else class="text-body2 text-grey-6">This role grants no permissions of its own.</div>
              </div>
              <app-select
                v-else v-model="form.permissions" :options="permissionOptions" label="Permissions" multiple
                :loading="loadingPermissions"
                :info="isSuperAdmin ? '' : 'The list stops at what your own tenant can hand out.'"
                @update:model-value="autoSavePermissions"
              />
            </q-card-section>
          </q-card>

          <!-- Role ↔ Permission Group composition (WO-70). -->
          <role-permission-groups-panel v-if="role.canManage" :role-id="role.id" class="q-mb-md" />
        </div>

        <div class="col-12 col-md-5">
          <!-- Who holds it, in this tenant. -->
          <role-users-panel
            v-if="canManageMembers" :role-id="role.id" :role-name="role.name" class="q-mb-md"
            @loaded="memberCount = $event"
          />
          <q-card v-else flat bordered class="role-card q-mb-md">
            <q-card-section class="text-grey-6">
              You do not have permission to manage who holds this role.
            </q-card-section>
          </q-card>
        </div>
      </div>

      <app-record-audit :audit="role.audit" />
    </div>

    <!-- Tenant availability (platform roles, Super Admin only) -->
    <app-form-drawer
      v-model="tenantsOpen" title="Available to tenants" :saving="tenantsSaving"
      @submit="submitTenants" @cancel="resetTenants"
    >
      <div class="text-body2 text-grey-7 q-mb-md">Select the tenants this role can be assigned within.</div>
      <app-select
        v-model="selectedTenantIds" :options="tenantOptions" label="Tenants" multiple
        :loading="loadingTenants"
      />
    </app-form-drawer>
  </q-page>
</template>

<script setup>
// The role, in full: what it is, what it grants, which groups compose it, and who holds it here.
import { ref, reactive, computed, watch, onMounted } from "vue";
import { useRoute, useRouter } from "vue-router";
import { roleApi, tenantApi, getApiErrorMessage } from "services/api";
import { useAuthStore } from "stores/auth";
import { useTenantStore } from "stores/tenant";
import { usePermissions, Permissions } from "composables/usePermissions";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useDateFormat } from "composables/useDateFormat";
import { stripHtml } from "utils/richText";

import AppDetailHeader from "components/common/AppDetailHeader.vue";
import AppRecordAudit from "components/common/AppRecordAudit.vue";
import AppFormDrawer from "components/common/AppFormDrawer.vue";
import AppSelect from "components/common/AppSelect.vue";
import AppTextField from "components/common/AppTextField.vue";
import AppRichTextField from "components/common/AppRichTextField.vue";
import AppInfoTip from "components/common/AppInfoTip.vue";
import AppAutoSaveState from "components/common/AppAutoSaveState.vue";
import RolePermissionGroupsPanel from "modules/permission-group/components/RolePermissionGroupsPanel.vue";
import RoleUsersPanel from "modules/role/components/RoleUsersPanel.vue";

const route = useRoute();
const router = useRouter();
const notify = useNotify();
const { confirm } = useConfirm();
const fmt = useDateFormat();
const authStore = useAuthStore();
const tenantStore = useTenantStore();
const { has } = usePermissions();

const roleId = route.params.id;
const role = ref(null);
const loading = ref(false);
const memberCount = ref(null);

const isSuperAdmin = computed(() => authStore.roles.includes("SuperAdmin"));
// Managing who holds a role is a narrower right than changing what it grants, and it only means anything
// in the tenant the caller is working in — another tenant's role has no membership to show here.
const canAssign = computed(() => has(Permissions.RolesAssign));
const canManageMembers = computed(() =>
  canAssign.value && (!role.value?.tenantId || role.value.tenantId === tenantStore.activeTenantId));

// A system role's name is fixed: the platform seeds it and looks it up by that name.
const canEditName = computed(() => !!role.value?.canManage && !role.value?.isSystem);

const scopeLabel = computed(() => (role.value?.tenantId ? role.value.tenantName || "This tenant" : "Platform"));
const scopeExplainer = computed(() => (role.value?.tenantId
  ? "Created inside this tenant. It exists nowhere else, and only its own tenant's admins can change it."
  : "Offered in every tenant. Only a Super Admin can change what it grants."));

const descriptionText = computed(() => stripHtml(role.value?.description || ""));
const permissionSummary = computed(() => {
  const n = form.permissions.length;
  return n === 1 ? "1 permission" : `${n} permissions`;
});
const memberSummary = computed(() => {
  if (memberCount.value == null) return "Users…";
  return memberCount.value === 1 ? "1 user" : `${memberCount.value} users`;
});
const definitionHint = computed(() => (canEditName.value
  ? "Saves as you go: the name and description when you leave them, the permissions as soon as you change them."
  : "A system role keeps its name — the platform looks it up by that name — but its permissions can still be tuned, and they save as soon as you change them."));

const form = reactive({ name: "", description: "", permissions: [] });

const prettyPermission = (key) => key.replace(/_/g, " ").replace(/\./g, " · ");

// ---- Load ----
// `syncFields` seeds the inputs.
const load = async ({ syncFields = true } = {}) => {
  loading.value = !role.value;
  try {
    role.value = await roleApi.get(roleId);
    if (!syncFields) return;
    form.name = role.value.name;
    form.description = role.value.description || "";
    form.permissions = role.value.permissions || [];
  } catch (err) {
    notify.error(getApiErrorMessage(err));
    router.replace({ name: "roles" });
  } finally {
    loading.value = false;
  }
};

// ---- Permission catalogue (what this caller may put in a role) ----
const permissionOptions = ref([]);
const loadingPermissions = ref(false);

const loadPermissions = async () => {
  if (permissionOptions.value.length || !role.value?.canManage) return;
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

// ---- Auto-save ----
// The definition saves itself: the name when you leave it, the permissions the moment you change them, the
// description a beat after you stop typing (a rich-text editor has no blur to hang it on).
const definitionSave = reactive({ state: "idle", message: "" });

const runAutoSave = async (target, fn) => {
  target.state = "saving";
  target.message = "";
  try {
    await fn();
    target.state = "saved";
    setTimeout(() => { if (target.state === "saved") target.state = "idle"; }, 2500);
    return true;
  } catch (err) {
    target.state = "error";
    target.message = getApiErrorMessage(err);
    return false;
  }
};

// Only ever sends what changed. The endpoint treats each field as optional, so a description save cannot
// carry a half-typed name along with it.
const saveDefinition = (patch) => runAutoSave(definitionSave, async () => {
  await roleApi.update(roleId, patch);
  await load({ syncFields: false });
});

const autoSaveName = async () => {
  if (!canEditName.value || !role.value) return;
  const next = (form.name || "").trim();
  if (next === role.value.name) {
    form.name = role.value.name; // whitespace-only edits tidied away
    return;
  }
  if (!next) {
    notify.error("A role needs a name.");
    form.name = role.value.name;
    return;
  }

  const saved = await saveDefinition({ name: next });
  if (!saved) {
    // Refused — a duplicate name, most likely, and the indicator carries the reason. The field goes back
    // to the name that is actually in force rather than sitting there looking saved.
    form.name = role.value.name;
  }
};

const autoSavePermissions = async () => {
  if (!role.value?.canManage) return;
  const saved = await saveDefinition({ permissions: [...form.permissions] });
  if (!saved) {
    form.permissions = role.value.permissions || [];
  }
};

// The description is a CKEditor field with no blur to hang a save on, so it commits a beat after the typing
// stops.
let descriptionTimer = null;
watch(() => form.description, (next) => {
  if (!role.value?.canManage) return;
  if ((next || "") === (role.value.description || "")) return;
  clearTimeout(descriptionTimer);
  descriptionTimer = setTimeout(() => saveDefinition({ description: next || "" }), 1200);
});

// ---- Delete ----
const removeRole = async () => {
  const ok = await confirm({
    title: "Delete role",
    message: `Delete the "${role.value.name}" role? Users keeping this role will lose its permissions.`,
    confirmLabel: "Delete",
    type: "danger"
  });
  if (!ok) return;
  try {
    await roleApi.remove(roleId);
    notify.success("Role deleted.");
    router.replace({ name: "roles" });
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// ---- Tenant availability (platform roles, Super Admin only) ----
const tenantsOpen = ref(false);
const tenantsSaving = ref(false);
const tenantOptions = ref([]);
const loadingTenants = ref(false);
const selectedTenantIds = ref([]);
const originalTenantIds = ref([]);

const resetTenants = () => {
  selectedTenantIds.value = [];
  originalTenantIds.value = [];
};

const openTenants = async () => {
  resetTenants();
  loadingTenants.value = true;
  tenantsOpen.value = true;
  try {
    const [tenantsResp, current] = await Promise.all([
      tenantApi.list({ page: 1, limit: 100 }),
      roleApi.roleTenants(roleId)
    ]);
    tenantOptions.value = (tenantsResp?.data || []).map((t) => ({ label: t.name, value: t.tenantId }));
    originalTenantIds.value = current || [];
    selectedTenantIds.value = [...originalTenantIds.value];
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loadingTenants.value = false;
  }
};

// Kept behind an explicit Save: this one writes across tenants, and a stray click in a multi-select is
// not the moment to take a role away from one.
const submitTenants = async () => {
  const selected = selectedTenantIds.value;
  const original = originalTenantIds.value;
  const toAdd = selected.filter((id) => !original.includes(id));
  const toRemove = original.filter((id) => !selected.includes(id));
  tenantsSaving.value = true;
  try {
    await Promise.all([
      ...toAdd.map((tenantId) => roleApi.assignToTenant(tenantId, roleId)),
      ...toRemove.map((tenantId) => roleApi.unassignFromTenant(tenantId, roleId))
    ]);
    tenantsOpen.value = false;
    notify.success("Tenant availability updated.");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    tenantsSaving.value = false;
  }
};

onMounted(async () => {
  await load();
  await loadPermissions();
});
</script>

<style scoped>
.role-card {
  border-radius: 12px;
}
</style>
