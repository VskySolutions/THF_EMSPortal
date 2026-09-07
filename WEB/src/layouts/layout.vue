<template>
  <q-layout view="lHh Lpr lFf">
    <q-header bordered class="header">
      <q-toolbar class="header-top flex items-center justify-between">
        <div class="flex items-center">
          <q-btn v-if="isLoggedIn" flat dense round icon="o_menu" class="text-black" aria-label="Menu" @click="toggleLeftDrawer" />
          <q-btn flat no-caps class="no-padding q-ml-md" @click="$router.push('/')">
            <span class="text-weight-bold fs-18 text-primary">EMS Portal</span>
          </q-btn>
        </div>
        <!-- User menu when signed in, otherwise a login action -->
        <div class="row q-gutter-sm items-center no-wrap">
          <!-- Active Tenant switcher -->
          <q-btn-dropdown
            v-if="isLoggedIn && hasMultipleTenants"
            flat
            no-caps
            icon="o_apartment"
            :label="activeTenantLabel"
            class="text-grey-9"
          >
            <q-list>
              <q-item-label header class="text-grey-7">Switch tenant</q-item-label>
              <q-item
                v-for="t in assignments"
                :key="t.tenantId"
                v-close-popup
                clickable
                :active="t.tenantId === activeTenantId"
                @click="onSwitchTenant(t.tenantId)"
              >
                <q-item-section>
                  <q-item-label>{{ t.name || t.identifier }}</q-item-label>
                  <q-item-label caption class="text-capitalize">{{ (t.roleNames || []).join(", ") || "No roles" }}</q-item-label>
                </q-item-section>
                <q-item-section v-if="t.tenantId === activeTenantId" side>
                  <q-icon name="o_check" color="primary" />
                </q-item-section>
              </q-item>
            </q-list>
          </q-btn-dropdown>

          <!-- Active-tenant roles (AC-REMS-001.8): the user's roles for the active tenant, shown on every
               authenticated screen alongside their name (in user-info). -->
          <div v-if="isLoggedIn && activeRoles.length" class="gt-xs row items-center q-gutter-xs">
            <q-chip
              v-for="r in activeRoles" :key="r" dense square color="teal-1" text-color="primary"
              class="text-capitalize q-my-none"
            >
              {{ r }}
            </q-chip>
          </div>

          <!-- Super-Admin tenant scope. -->
          <app-tenant-scope-select v-if="isLoggedIn" class="gt-xs" />

          <notification-centre v-if="isLoggedIn" />
          <user-info v-if="isLoggedIn" />
          <q-btn v-else unelevated color="primary" no-caps icon="o_login" label="Login" :to="{ name: 'login' }" />
        </div>
      </q-toolbar>
    </q-header>

    <!-- Collapsed, the drawer stays as a 60px icon rail. -->
    <q-drawer
      v-if="isLoggedIn"
      v-model="leftDrawerOpen"
      show-if-above
      :mini="menuCollapsed"
      :width="292"
      :mini-width="60"
      :breakpoint="1024"
      bordered
      class="bg-white"
    >
      <aside-header />
      <q-scroll-area class="fit">
        <AppMenu :mini="menuCollapsed" />
      </q-scroll-area>
    </q-drawer>

    <q-page-container>
      <!-- The tenant scope is global and sticky (it survives reloads), so it is stated on every screen. -->
      <!-- inline-actions keeps the message and the button on ONE row; without it q-banner drops actions
           onto a second line and the banner takes twice the height on every page. -->
      <q-banner
        v-if="isLoggedIn && tenantScopeActive" dense inline-actions
        class="bg-orange-2 text-orange-10 q-px-md"
      >
        <template #avatar><q-icon name="o_visibility" color="orange-10" /></template>
        Viewing <span class="text-weight-bold">{{ scopedTenantName || "another tenant" }}</span> — changes apply to that tenant.
        <template #action>
          <q-btn flat dense no-caps color="orange-10" label="Back to my tenant" @click="clearScope" />
        </template>
      </q-banner>

      <router-view />
    </q-page-container>

    <!-- Universal Features floating sticky notes overlay (authenticated users only). -->
    <sticky-note-layer v-if="isLoggedIn" />

    <q-footer bordered class="bg-white">
      <div class="text-center q-py-sm">
        <h6 class="q-my-none text-black" style="font-size: 13px; font-weight: 400;">
          Copyright &copy; 2025 Vsky. Website Designed and Developed by
          <a href="https://www.vskysolutions.com/" target="_blank" style="text-decoration: none; color: #007bff;">
            VSky Solutions.
          </a>
        </h6>
      </div>
    </q-footer>
  </q-layout>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { LocalStorage, Dialog, useQuasar } from "quasar";
import { storeToRefs } from "pinia";
import { useTenantStore } from "stores/tenant";

import UserInfo from "shared/user_info.vue";
import AsideHeader from "shared/aside_header.vue";
import AppMenu from "src/components/app_menu.vue";
import NotificationCentre from "components/universal/NotificationCentre.vue";
import StickyNoteLayer from "components/universal/StickyNoteLayer.vue";
import AppTenantScopeSelect from "components/common/AppTenantScopeSelect.vue";
import { useTenantScope } from "composables/useTenantScope";

const $q = useQuasar();
const isLoggedIn = !!LocalStorage.getItem("token");

// Persist the drawer open/closed state across reloads (defaults to open). Above the breakpoint the
// drawer is always mounted (show-if-above) and this only governs the mobile overlay.
const DRAWER_KEY = "leftDrawerOpen";
const storedDrawer = LocalStorage.getItem(DRAWER_KEY);
const leftDrawerOpen = ref(storedDrawer === null ? true : storedDrawer);
watch(leftDrawerOpen, (value) => LocalStorage.set(DRAWER_KEY, value));

// …and remember whether the user collapsed it to the icon rail.
const MINI_KEY = "leftDrawerMini";
const menuCollapsed = ref(LocalStorage.getItem(MINI_KEY) === true);
watch(menuCollapsed, (value) => LocalStorage.set(MINI_KEY, value));

const toggleLeftDrawer = () => {
  // Below the breakpoint the drawer is an overlay and Quasar ignores mini entirely, so there the button
  // has to keep opening and closing it outright.
  if ($q.screen.lt.md) {
    leftDrawerOpen.value = !leftDrawerOpen.value;
    return;
  }
  menuCollapsed.value = !menuCollapsed.value;
};

// Super-Admin tenant scope: shown as a banner because the selection is global and survives a reload.
const {
  isScoped: tenantScopeActive, scopedTenantName, loadTenants: loadScopeTenants, clearScope
} = useTenantScope();
if (isLoggedIn) loadScopeTenants();

const tenantStore = useTenantStore();
const { assignments, activeTenantId } = storeToRefs(tenantStore);
const hasMultipleTenants = computed(() => tenantStore.hasMultipleTenants);
// The role names the user holds in the active tenant (multi-role), shown in the header.
const activeRoles = computed(() => tenantStore.activeRoles);
const activeTenantLabel = computed(() => {
  const t = tenantStore.activeTenant;
  return t?.name || t?.identifier || "Tenant";
});

// Confirm-before-discard when an open form has unsaved changes (AC-UI-007.4 guard).
const confirmDiscard = () => new Promise((resolve) => {
  Dialog.create({
    title: "Unsaved changes",
    message: "Switching tenant will discard your unsaved changes. Continue?",
    cancel: { label: "Cancel", flat: true, noCaps: true },
    ok: { label: "Continue", color: "primary", unelevated: true, noCaps: true },
    persistent: true
  }).onOk(() => resolve(true)).onCancel(() => resolve(false));
});

const onSwitchTenant = async (tenantId) => {
  const switched = await tenantStore.switchTenant(tenantId, { confirm: confirmDiscard });
  if (switched) {
    // Active page components listen and re-fetch their data for the new tenant.
    window.dispatchEvent(new CustomEvent("tenant-switched", { detail: { tenantId } }));
  }
};
</script>

<style scoped>
  .q-item.q-router-link--active, .q-item--active {
    color: #3ba5e5;
    font-weight: 500;
  }
  .no-underline {
    text-decoration: none !important;
  }
</style>
