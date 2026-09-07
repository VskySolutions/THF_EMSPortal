<template>
  <!-- Toolbar control, styled like the tenant switcher beside it. -->
  <q-btn-dropdown
    v-if="canScopeTenant"
    flat no-caps dense
    icon="o_travel_explore"
    :label="buttonLabel"
    :class="isScoped ? 'text-orange-9' : 'text-grey-9'"
  >
    <q-tooltip>Super Admin: view and manage another tenant</q-tooltip>
    <q-list style="min-width: 240px;">
      <q-item-label header class="text-grey-7">View tenant</q-item-label>

      <q-item v-if="loadingTenants">
        <q-item-section class="text-grey-6">Loading tenants…</q-item-section>
      </q-item>

      <q-item
        v-for="t in tenantOptions" :key="t.value"
        v-close-popup clickable :active="t.value === selectedTenantId"
        @click="setScope(t.value)"
      >
        <q-item-section><q-item-label>{{ t.label }}</q-item-label></q-item-section>
        <q-item-section v-if="t.value === selectedTenantId" side>
          <q-icon name="o_check" color="primary" />
        </q-item-section>
      </q-item>

      <template v-if="isScoped">
        <q-separator />
        <q-item v-close-popup clickable @click="clearScope">
          <q-item-section avatar><q-icon name="o_undo" color="primary" /></q-item-section>
          <q-item-section>Back to my tenant</q-item-section>
        </q-item>
      </template>
    </q-list>
  </q-btn-dropdown>
</template>

<script setup>
// The Super-Admin tenant scope picker. Every instance shares one selection (useTenantScope keeps it at
// module scope + LocalStorage), so this stays consistent wherever it is rendered.
import { computed, onMounted } from "vue";
import { useTenantScope } from "composables/useTenantScope";

const {
  canScopeTenant, selectedTenantId, isScoped, scopedTenantName,
  tenantOptions, loadingTenants, loadTenants, setScope, clearScope
} = useTenantScope();

// Only name the tenant while it is somebody else's — otherwise the toolbar would repeat what the switcher
// next to it already says.
const buttonLabel = computed(() => (isScoped.value ? scopedTenantName.value || "Another tenant" : "View as"));

onMounted(loadTenants);
</script>
