<template>
  <q-btn dense flat color="grey-8">
    <div class="flex items-center nav-user-info">
      <q-icon name="o_account_circle" color="orange" class="material-icons-outlined q-mr-sm" size="38px" />
      <div class="line-height-normal q-mr-sm text-left">
        <div class="fs-16 text-capitalize flex justify-between items-center">
          <span>{{ displayName }}</span>
          <q-icon side name="o_keyboard_arrow_down" size="sm" class="q-ml-sm" style="color:#697A8D;" />
        </div>
      </div>
    </div>
    <q-menu>
      <q-list style="min-width: 250px" class="user-card">
        <q-item class="q-py-sm">
          <q-item-section avatar>
            <q-icon name="o_account_circle" color="orange" class="material-icons-outlined q-mr-sm" size="38px" />
          </q-item-section>
          <q-item-section>
            <q-item-label class="text-subtitle1 text-weight-medium text-capitalize">{{ displayName }}</q-item-label>
            <q-item-label caption>{{ email }}</q-item-label>
            <q-item-label v-if="roles.length" caption class="text-capitalize user-card__role">
              {{ roles.join(" · ") }}
              <q-icon v-if="permissions.length" name="o_info" size="14px" class="q-ml-xs cursor-pointer" />
              <q-tooltip v-if="permissions.length" anchor="bottom left" self="top left" max-width="340px" class="bg-grey-9 text-white">
                <div class="text-weight-medium q-mb-xs">Permissions ({{ permissions.length }})</div>
                <div class="user-card__perms">
                  <div v-for="p in permissions" :key="p">{{ prettyPermission(p) }}</div>
                </div>
              </q-tooltip>
            </q-item-label>
            <q-item-label v-if="tenantName" caption class="row items-center no-wrap">
              <q-icon name="o_apartment" size="14px" class="q-mr-xs" />{{ tenantName }}
            </q-item-label>
          </q-item-section>
        </q-item>
        <q-separator class="q-mb-sm" />
        <!-- The full list, above the bell's popover — which only ever shows the unread few. -->
        <q-item v-ripple :to="{ name: 'uf_notifications' }" clickable>
          <q-item-section avatar>
            <q-icon name="o_notifications" color="orange" class="material-icons-outlined" />
          </q-item-section>
          <q-item-section>
            <q-item-label>My Notifications</q-item-label>
          </q-item-section>
        </q-item>
        <q-item v-ripple :to="{ name: 'uf_mentions' }" clickable>
          <q-item-section avatar>
            <q-icon name="o_alternate_email" color="orange" class="material-icons-outlined" />
          </q-item-section>
          <q-item-section>
            <q-item-label>My Mentions</q-item-label>
          </q-item-section>
        </q-item>
        <q-item v-ripple :to="{ name: 'uf_pinned' }" clickable>
          <q-item-section avatar>
            <q-icon name="o_push_pin" color="orange" class="material-icons-outlined" />
          </q-item-section>
          <q-item-section>
            <q-item-label>My Pinned</q-item-label>
          </q-item-section>
        </q-item>
        <q-item v-ripple :to="{ name: 'uf_notification_preferences' }" clickable>
          <q-item-section avatar>
            <q-icon name="o_tune" color="orange" class="material-icons-outlined" />
          </q-item-section>
          <q-item-section>
            <q-item-label>Notification Preferences</q-item-label>
          </q-item-section>
        </q-item>
        <q-separator class="q-my-sm" />
        <q-item v-ripple :to="{ name: 'profile' }" clickable>
          <q-item-section avatar>
            <q-icon name="person" class="material-icons-outlined" color="orange" />
          </q-item-section>
          <q-item-section>
            <q-item-label>Profile</q-item-label>
          </q-item-section>
        </q-item>
        <q-item v-ripple :to="{ name: 'change_password' }" clickable>
          <q-item-section avatar>
            <q-icon name="lock" color="orange" class="material-icons-outlined" />
          </q-item-section>
          <q-item-section>
            <q-item-label>Change Password</q-item-label>
          </q-item-section>
        </q-item>
        <q-separator class="q-my-sm" />
        <q-item v-ripple clickable @click="onLogout">
          <q-item-section avatar>
            <q-icon name="logout" color="orange" class="material-icons-outlined" />
          </q-item-section>
          <q-item-section>
            <q-item-label>Logout</q-item-label>
          </q-item-section>
        </q-item>
        <q-item v-ripple clickable @click="onLogoutAll">
          <q-item-section avatar>
            <q-icon name="o_devices" color="orange" class="material-icons-outlined" />
          </q-item-section>
          <q-item-section>
            <q-item-label>Logout all devices</q-item-label>
          </q-item-section>
        </q-item>
      </q-list>
    </q-menu>
  </q-btn>
</template>

<script setup>
import { computed } from "vue";
import { useRouter } from "vue-router";
import { useAuthStore } from "stores/auth";
import { useTenantStore } from "stores/tenant";

const router = useRouter();
const authStore = useAuthStore();
const tenantStore = useTenantStore();

const displayName = computed(() => authStore.user?.displayName || authStore.user?.email || "Account");
const email = computed(() => authStore.user?.email || "");
// All RBAC role names the user holds in the active tenant (multi-role, WO-123).
const roles = computed(() => tenantStore.activeRoles);

// Active tenant name (falls back to its identifier) shown under the role.
const tenantName = computed(() => tenantStore.activeTenant?.name || tenantStore.activeTenant?.identifier || "");

// All effective permissions for the active tenant (decoded from the JWT), sorted for the tooltip.
const permissions = computed(() => [...(authStore.permissions || [])].sort());

// "users.reset_password" → "Users · Reset password" for a readable tooltip.
const prettyPermission = (p) => p
  .split(".")
  .map((seg) => seg.replace(/_/g, " "))
  .map((seg) => seg.charAt(0).toUpperCase() + seg.slice(1))
  .join(" · ");

const onLogout = async () => {
  await authStore.logout();
  router.replace({ name: "login" });
};

const onLogoutAll = async () => {
  await authStore.logoutAll();
  router.replace({ name: "login" });
};
</script>

<style scoped>
.user-card__role {
  display: flex;
  align-items: center;
}
/* Compact two-column list so even a Super Admin's full permission set stays readable in the tooltip. */
.user-card__perms {
  columns: 2;
  column-gap: 16px;
  font-size: 12px;
  line-height: 1.5;
  text-transform: none;
}
.user-card__perms > div {
  break-inside: avoid;
  white-space: nowrap;
}
</style>
