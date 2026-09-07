<template>
  <q-list class="app-menu q-py-xs">
    <template v-for="section in visibleSections" :key="section.key">
      <!-- Ungrouped items (no label, e.g. Dashboard) render flat at the top. -->
      <template v-if="!section.label">
        <q-item
          v-for="item in section.items"
          :key="item.label"
          v-ripple
          dense
          clickable
          :to="item.to"
          :exact="item.exact"
          active-class="text-primary bg-teal-1"
          @click="onItem(item)"
        >
          <q-item-section avatar><q-icon :name="item.icon" size="20px" /></q-item-section>
          <q-item-section>{{ item.label }}</q-item-section>
          <q-tooltip v-if="mini" anchor="center right" self="center left">{{ item.label }}</q-tooltip>
        </q-item>
      </template>

      <!-- Collapsed, a group is one icon whose children hang off it as a flyout. -->
      <q-item
        v-else-if="mini"
        :key="`${section.key}-mini`"
        v-ripple
        dense
        clickable
        @mouseenter="openFlyout(section.key)"
        @mouseleave="scheduleFlyoutClose"
        @click="openFlyout(section.key)"
      >
        <q-item-section avatar><q-icon :name="section.icon" size="20px" /></q-item-section>
        <q-item-section>{{ section.label }}</q-item-section>

        <q-menu
          :model-value="flyoutKey === section.key"
          anchor="top right"
          self="top left"
          :offset="[0, 0]"
          no-parent-event
          no-focus
          no-refocus
          transition-show="jump-right"
          transition-hide="jump-left"
          @update:model-value="(open) => (flyoutKey = open ? section.key : null)"
        >
          <!-- Listeners on the list, not q-menu (which renders into a portal). -->
          <q-list
            dense
            class="app-menu__flyout"
            @mouseenter="openFlyout(section.key)"
            @mouseleave="scheduleFlyoutClose"
          >
            <q-item-label header class="app-menu__flyout-head">{{ section.label }}</q-item-label>
            <q-item
              v-for="item in section.items"
              :key="item.label"
              v-ripple
              v-close-popup
              dense
              clickable
              :to="item.to"
              :exact="item.exact"
              active-class="text-primary bg-teal-1"
              @click="onItem(item)"
            >
              <q-item-section avatar><q-icon :name="item.icon" size="20px" /></q-item-section>
              <q-item-section>{{ item.label }}</q-item-section>
            </q-item>
          </q-list>
        </q-menu>
      </q-item>

      <!-- Labelled sections are collapsible groups. -->
      <q-expansion-item
        v-else
        dense
        :icon="section.icon"
        :label="section.label"
        :model-value="isOpen(section)"
        header-class="app-menu__group text-primary text-weight-bold"
        @update:model-value="(v) => setOpen(section.key, v)"
      >
        <q-item
          v-for="item in section.items"
          :key="item.label"
          v-ripple
          dense
          clickable
          :to="item.to"
          :exact="item.exact"
          active-class="text-primary bg-teal-1"
          class="app-menu__nested"
          @click="onItem(item)"
        >
          <q-item-section avatar><q-icon :name="item.icon" size="20px" /></q-item-section>
          <q-item-section>{{ item.label }}</q-item-section>
        </q-item>
      </q-expansion-item>
    </template>
  </q-list>
</template>

<script setup>
import { computed, reactive, ref, onBeforeUnmount } from "vue";
import { LocalStorage } from "quasar";
import { useRouter } from "vue-router";
import { useAuthStore } from "stores/auth";
import { Permissions } from "composables/usePermissions";

defineProps({
  // Collapsed to the icon rail: each group offers its children as a flyout beside its icon.
  mini: { type: Boolean, default: false }
});

const authStore = useAuthStore();
const router = useRouter();

// Which group is showing its children beside the rail. One at a time — they would overlap otherwise.
const flyoutKey = ref(null);
let flyoutTimer = null;

const openFlyout = (key) => {
  clearTimeout(flyoutTimer);
  flyoutKey.value = key;
};

// Delayed, so the pointer can cross from the icon into the flyout without it closing underneath.
const scheduleFlyoutClose = () => {
  clearTimeout(flyoutTimer);
  flyoutTimer = setTimeout(() => { flyoutKey.value = null; }, 150);
};

onBeforeUnmount(() => clearTimeout(flyoutTimer));

// Menu items with an `action` (e.g. Logout) run a handler instead of navigating.
const onItem = async (item) => {
  if (item.action === "logout") {
    await authStore.logout();
    router.replace({ name: "login" });
  } else if (item.action === "logoutAll") {
    await authStore.logoutAll();
    router.replace({ name: "login" });
  }
};

// Ordered by application flow: overview → set up → configure → operate → personal. `permissions:
// null` → visible to every authenticated user.
const sections = [
  {
    key: "overview",
    label: null,
    items: [
      { label: "Dashboard", icon: "o_dashboard", to: "/dashboard", permissions: null }
    ]
  },
  {
    // REMS (Phase 15).
    key: "rems",
    label: "REMS",
    icon: "o_business_center",
    // Three lists, one per role, plus the shared Related Entities board.
    items: [
      // Open to everyone, like Approvals: the list shows the requests you raised or are named on, so a
      // user with no REMS work sees an empty list rather than a menu that hides the page from them.
      { label: "My Requests", icon: "o_space_dashboard", to: "/rems/partner", permissions: null },
      { label: "EMS Review", icon: "o_fact_check", to: "/rems/ems-review", permissions: [Permissions.RemsEngagementsManage] },
      // Open to everyone too, and for a different reason from the two above: this list is not scoped to the
      // caller's own work at all.
      { label: "Related Entities", icon: "o_account_tree", to: "/rems/related-entities", permissions: null },
      { label: "Approvals", icon: "o_approval", to: "/rems/approvals", permissions: null }
    ]
  },
  {
    key: "administration",
    label: "Administration",
    icon: "o_corporate_fare",
    items: [
      { label: "Tenants", icon: "o_apartment", to: "/tenants", permissions: [Permissions.TenantsWrite] },
      { label: "Person", icon: "o_badge", to: "/persons", permissions: [Permissions.PersonsRead] },
    ]
  },
  {
    key: "access-management",
    label: "Access Management",
    icon: "o_lock",
    items: [
      { label: "Permission Groups", icon: "o_workspaces", to: "/permission-groups", permissions: [Permissions.GroupsManage] },
      { label: "Roles", icon: "o_admin_panel_settings", to: "/roles", permissions: [Permissions.RolesWrite] },
      { label: "Users", icon: "o_group", to: "/users", permissions: [Permissions.UsersRead] },
      { label: "User Groups", icon: "o_groups", to: "/user-groups", permissions: [Permissions.UsersGroupManagement] }
    ]
  },
  {
    // Tenant-wide settings and universal features (email, and future cross-cutting settings).
    key: "settings",
    label: "Tenant Settings",
    icon: "o_settings",
    items: [
      { label: "Email Accounts", icon: "o_mail", to: "/smtp-accounts", permissions: [Permissions.EmailManage] },
      { label: "Email Templates", icon: "o_drafts", to: "/email-templates", permissions: [Permissions.EmailManage] },
      // Gated on MANAGE, not read: Partner and REMS Admin hold optionSets.read so their dropdowns resolve,
      // but the lists are configuration and only Super Admin / Tenant Admin maintain them.
      { label: "Option Sets", icon: "o_list_alt", to: "/option-sets", permissions: [Permissions.OptionSetsManage] },
      { label: "Tag Management", icon: "o_label", to: "/settings/tags", permissions: [Permissions.SettingsManage] },
      { label: "Sticky Notes", icon: "o_sticky_note_2", to: "/settings/sticky-notes", permissions: [Permissions.SettingsManage] },
      { label: "Modified Log", icon: "o_manage_history", to: "/settings/modified-log-config", permissions: [Permissions.SettingsManage] },
      { label: "Deleted Records", icon: "o_restore_from_trash", to: "/settings/retention", permissions: [Permissions.RecordsAdminDelete] }
    ]
  },
  {
    key: "account",
    label: "Account",
    icon: "o_account_circle",
    items: [
      { label: "My Account", icon: "o_manage_accounts", to: "/account", permissions: null },
      { label: "Profile", icon: "o_person", to: { name: "profile" }, permissions: null },
      { label: "Change Password", icon: "o_lock", to: { name: "change_password" }, permissions: null },
      // The full list behind the bell's "View all". Named and ordered as the avatar menu has it, since
      // both lead to the same four pages and reading differently in each is what makes one look missing.
      { label: "My Notifications", icon: "o_notifications", to: { name: "uf_notifications" }, permissions: null },
      { label: "My Mentions", icon: "o_alternate_email", to: { name: "uf_mentions" }, permissions: null },
      { label: "My Pinned", icon: "o_push_pin", to: { name: "uf_pinned" }, permissions: null },
      { label: "Notification Preferences", icon: "o_tune", to: { name: "uf_notification_preferences" }, permissions: null },
      { label: "Logout", icon: "o_logout", action: "logout", permissions: null },
      { label: "Logout all devices", icon: "o_devices", action: "logoutAll", permissions: null }
    ]
  }
];

const canSee = (permissions) => !permissions || authStore.hasAnyPermission(permissions);

const visibleSections = computed(() =>
  sections
    .map((section) => ({ ...section, items: section.items.filter((item) => canSee(item.permissions)) }))
    .filter((section) => section.items.length));

// Per-group collapse state, persisted to LocalStorage so the user's expand/collapse choices survive a
// page refresh. The stored object holds only the collapsed groups ({ [sectionKey]: true }).
const STORAGE_KEY = "appMenuCollapsed";
const collapsed = reactive(LocalStorage.getItem(STORAGE_KEY) || {});
const isOpen = (section) => collapsed[section.key] !== true;
const setOpen = (key, open) => {
  if (open) {
    delete collapsed[key];
  } else {
    collapsed[key] = true;
  }
  LocalStorage.set(STORAGE_KEY, { ...collapsed });
};
</script>

<style scoped>
/* Compact spacing — the drawer holds many items. */
.app-menu :deep(.q-item) {
  min-height: 34px;
}
/* Tighten the icon gutter so icon + label sit close together. */
.app-menu :deep(.q-item__section--avatar) {
  min-width: 32px;
  padding-right: 8px;
}
/* Slim, uppercase collapsible group headers in the theme colour. */
.app-menu :deep(.app-menu__group) {
  min-height: 38px;
  padding: 4px 12px;
  font-size: 11px;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}
.app-menu :deep(.app-menu__group .q-item__section--avatar) {
  min-width: 30px;
  padding-right: 6px;
}
/* Indent the items within a group so the hierarchy reads clearly. */
.app-menu__nested {
  padding-left: 20px;
}

/* The rail's flyout. Portaled to the body, so .app-menu selectors cannot reach it — spacing restated. */
.app-menu__flyout {
  min-width: 208px;
  padding: 2px 0 4px;
}
.app-menu__flyout :deep(.q-item) {
  min-height: 34px;
}
.app-menu__flyout :deep(.q-item__section--avatar) {
  min-width: 32px;
  padding-right: 8px;
}
/* Names the group, styled as the expanded menu's group headers. */
.app-menu__flyout-head {
  min-height: auto;
  padding: 8px 16px 4px;
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--q-primary);
}
</style>
