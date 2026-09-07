<template>
  <q-page padding>
    <!-- Common page header card: breadcrumb on the left, all actions on the right. -->
    <app-list-header :breadcrumbs="[{ label: 'Dashboard', icon: 'o_home' }]">
      <template #actions>
        <q-btn-dropdown outline no-caps color="primary" :label="dateRangeLabel" icon="o_calendar_today">
          <q-list>
            <q-item v-for="opt in DATE_RANGES" :key="opt.value" v-close-popup clickable @click="setDateRange(opt.value)">
              <q-item-section>{{ opt.label }}</q-item-section>
              <q-item-section v-if="opt.value === dateRange" side><q-icon name="o_check" color="primary" /></q-item-section>
            </q-item>
          </q-list>
        </q-btn-dropdown>
        <q-btn outline no-caps color="primary" icon="o_refresh" label="Refresh" :loading="anyLoading" @click="refreshAll" />
        <q-btn-dropdown outline no-caps color="primary" icon="o_download" label="Export">
          <q-list>
            <q-item v-close-popup clickable @click="exportCsv">
              <q-item-section avatar><q-icon name="o_table_view" /></q-item-section>
              <q-item-section>Export CSV</q-item-section>
            </q-item>
            <q-item v-close-popup clickable @click="exportPdf">
              <q-item-section avatar><q-icon name="o_print" /></q-item-section>
              <q-item-section>Print / PDF</q-item-section>
            </q-item>
          </q-list>
        </q-btn-dropdown>
        <q-btn
          outline
          no-caps
          :color="customiseOpen ? 'secondary' : 'primary'"
          icon="o_tune"
          label="Customise"
          @click="customiseOpen = !customiseOpen"
        />
      </template>
    </app-list-header>

    <!-- Reorder hint while customising. -->
    <q-banner v-if="customiseOpen" dense rounded class="bg-teal-1 text-primary q-mb-md no-print">
      <template #avatar><q-icon name="o_drag_indicator" color="primary" /></template>
      Drag widgets by the handle to reorder them. Toggle visibility in the panel on the right.
    </q-banner>

    <!-- Widget grid -->
    <div class="row q-col-gutter-md">
      <div
        v-for="(widget, index) in layout.visibleWidgets.value"
        :key="widget.key"
        class="col-12 col-md-6 col-lg-4"
        :draggable="customiseOpen"
        :class="['dashboard-grid-item', { 'dashboard-grid-item--over': dragOverIndex === index, 'dashboard-grid-item--drag': dragIndex === index }]"
        @dragstart="onDragStart(index, $event)"
        @dragover.prevent="onDragOver(index)"
        @drop.prevent="onDrop(index)"
        @dragend="onDragEnd"
      >
        <div v-if="customiseOpen" class="dashboard-grid-item__handle no-print">
          <q-icon name="o_drag_indicator" class="text-grey-6" />
          <q-tooltip>Drag to reorder</q-tooltip>
        </div>
        <component
          :is="resolveComponent(widget)"
          :widget-key="widget.key"
          :title="widget.title"
          v-bind="dataFor(widget)"
          :collapsed="layout.isCollapsed(widget.key)"
          @update:collapsed="(val) => layout.setCollapsed(widget.key, val)"
          @retry="() => retryFor(widget)"
        />
      </div>
    </div>

    <dashboard-customise-panel v-model="customiseOpen" :role="role" :layout="layout" />

    <div v-if="!layout.visibleWidgets.value.length" class="column flex-center q-pa-xl text-grey-6">
      <q-icon name="o_dashboard" size="40px" class="q-mb-sm" />
      <div class="text-subtitle1">No widgets to display.</div>
    </div>
  </q-page>
</template>

<script setup>
import { ref, computed, defineAsyncComponent, onMounted } from "vue";
import AppListHeader from "components/common/AppListHeader.vue";
import DashboardCustomisePanel from "modules/dashboard/DashboardCustomisePanel.vue";
import { usePreferences } from "composables/usePreferences";
import { usePermissions, Permissions } from "composables/usePermissions";
import { useDashboardLayout } from "composables/useDashboardLayout";
import { useNotify } from "composables/useNotify";
import {
  useUserDashboard,
  usePlatformDashboard
} from "composables/useDashboardData";

const { has } = usePermissions();
const prefs = usePreferences("dashboard");
const notify = useNotify();

// ---- Role resolution (must mirror DashboardController.ResolveDashboardRole) ----
// Super Admin = platform admin (can manage tenants). Tenant Admin = manages users + reads tenants.
const role = computed(() => {
  if (has(Permissions.TenantsWrite)) return "superAdmin";
  if (has(Permissions.UsersRead) && has(Permissions.TenantsRead)) return "tenantAdmin";
  return "common";
}).value;

// ---- Date range ----
const DATE_RANGES = [
  { label: "Today", value: "today" },
  { label: "Last 7 days", value: "7d" },
  { label: "Last 30 days", value: "30d" },
  { label: "Last 90 days", value: "90d" }
];
const dateRange = ref(prefs.get("dateRange", "7d"));
const dateRangeLabel = computed(() => DATE_RANGES.find((r) => r.value === dateRange.value)?.label || "Last 7 days");
const setDateRange = (val) => {
  dateRange.value = val;
  prefs.set("dateRange", val);
};

// ---- Layout ----
const layout = useDashboardLayout(role);

// ---- Data composables (instantiated once per category the role needs) ----
const isTenantAdmin = role === "tenantAdmin" || role === "superAdmin";
const isSuperAdmin = role === "superAdmin";
const users = isTenantAdmin ? useUserDashboard(dateRange) : null;
const platform = isSuperAdmin ? usePlatformDashboard(dateRange) : null;

const sources = [users, platform].filter(Boolean);
const anyLoading = computed(() => sources.some((s) => s.loading.value));

// ---- Component resolution (lazy, cached per key) ----
const componentCache = {};
const resolveComponent = (widget) => {
  if (!componentCache[widget.key]) {
    componentCache[widget.key] = defineAsyncComponent(widget.component);
  }
  return componentCache[widget.key];
};

// ---- Map a widget to its data slice + loading/error ----
// Keyed by widget.key so widget WOs can bind exactly the fields they need.
const dataFor = (widget) => {
  switch (widget.category) {
    case "users":
      return usersProps(widget.key);
    case "platform":
      return platformProps(widget.key);
    default:
      return {};
  }
};

const usersProps = (key) => {
  if (!users) return {};
  const base = { loading: users.loading.value, error: users.error.value };
  switch (key) {
    case "userSummary": return { ...base, kpis: users.kpis.value, activityFeed: users.activityFeed.value };
    case "userRoleDistribution": return { ...base, roleDistribution: users.roleDistribution.value };
    default: return base;
  }
};

const platformProps = (key) => {
  if (!platform) return {};
  const base = { loading: platform.loading.value, error: platform.error.value };
  switch (key) {
    case "tenantKpiCards": return { ...base, tenantKpis: platform.tenantKpis.value };
    case "tenantHealthTable": return { ...base, tenantHealth: platform.tenantHealth.value };
    case "platformGrowthChart": return { ...base, growth: platform.growth.value };
    case "tenantOnboardingPanel": return { ...base, onboarding: platform.onboarding.value };
    case "systemAlertsPanel": return { ...base, systemAlerts: platform.systemAlerts.value };
    case "platformUserAnalytics": return { ...base, userAnalytics: platform.userAnalytics.value };
    default: return base;
  }
};

const retryFor = (widget) => {
  switch (widget.category) {
    case "users": return users?.refresh();
    case "platform": return platform?.refresh(true);
    default: return undefined;
  }
};

// ---- Controls ----
const customiseOpen = ref(false);

const refreshAll = () => {
  users?.refresh();
  platform?.refresh(true);
};

// ---- Drag-to-reorder (only while the customise panel is open) ----
// Mirrors AppDataTable's native HTML5 column-reorder pattern: dragstart records the source index, dragover
// marks the hovered target, drop computes the new visible-key order and persists it.
const dragIndex = ref(null);
const dragOverIndex = ref(null);

const onDragStart = (index, e) => {
  if (!customiseOpen.value) return;
  dragIndex.value = index;
  if (e.dataTransfer) e.dataTransfer.effectAllowed = "move";
};
const onDragOver = (index) => {
  if (dragIndex.value === null) return;
  dragOverIndex.value = index;
};
const onDrop = (index) => {
  const from = dragIndex.value;
  if (from === null || from === index) {
    dragIndex.value = null;
    dragOverIndex.value = null;
    return;
  }
  // Reorder the visible keys, then merge back into the full widget order so hidden widgets keep
  // their relative positions.
  const visibleKeys = layout.visibleWidgets.value.map((w) => w.key);
  const moved = visibleKeys.splice(from, 1)[0];
  visibleKeys.splice(index, 0, moved);

  const fullOrder = [...layout.widgetOrder.value];
  let vi = 0;
  const newOrder = fullOrder.map((key) =>
    visibleKeys.includes(key) ? visibleKeys[vi++] : key);
  layout.setOrder(newOrder);

  dragIndex.value = null;
  dragOverIndex.value = null;
};
const onDragEnd = () => { dragIndex.value = null; dragOverIndex.value = null; };

// ---- Export ----
const csvEscape = (val) => {
  const s = val == null ? "" : String(val);
  return /[",\n]/.test(s) ? `"${s.replace(/"/g, "\"\"")}"` : s;
};
const rowsToCsv = (rows) => rows.map((r) => r.map(csvEscape).join(",")).join("\n");

const triggerDownload = (text, filename) => {
  const blob = new Blob([text], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
};

// Builds a CSV from the loaded tabular composable data for the widgets currently visible.
const exportCsv = () => {
  const visibleKeys = new Set(layout.visibleWidgets.value.map((w) => w.key));
  const sections = [];

  // Tenant health (super)
  if (visibleKeys.has("tenantHealthTable") && platform?.tenantHealth.value?.length) {
    const head = ["Tenant", "Active Users"];
    const body = platform.tenantHealth.value.map((t) => [
      t.tenantName ?? "", t.activeUsers ?? 0]);
    sections.push([["Tenant Health"], head, ...body]);
  }

  if (!sections.length) {
    notify.warning("No exportable tabular data is currently visible.");
    return;
  }

  // Join sections with a blank line between each.
  const csv = sections.map(rowsToCsv).join("\n\n");
  const stamp = new Date().toISOString().slice(0, 10);
  triggerDownload(csv, `dashboard-${stamp}.csv`);
  notify.success("Dashboard CSV exported.");
};

// Native print dialog — the @media print stylesheet hides controls so the widgets print cleanly,
// and the user can choose "Save as PDF".
const exportPdf = () => {
  notify.info("Opening print dialog. Choose \"Save as PDF\" to export.");
  window.print();
};

// ---- Lifecycle ----
onMounted(async () => {
  await layout.loadLayout();
});
</script>

<style scoped>
.dashboard-grid-item { position: relative; }
.dashboard-grid-item[draggable="true"] { cursor: grab; }
.dashboard-grid-item--drag { opacity: 0.5; }
.dashboard-grid-item--over :deep(.dashboard-widget) { outline: 2px dashed var(--q-primary); outline-offset: 2px; }
.dashboard-grid-item__handle {
  position: absolute;
  top: 14px;
  right: 18px;
  z-index: 2;
  cursor: grab;
}
</style>

<style>
/* Print / PDF: hide controls + chrome, expand widgets to full width. */
@media print {
  .no-print,
  .q-drawer,
  .q-banner,
  .q-breadcrumbs,
  .q-btn { display: none !important; }
  .dashboard-grid-item { width: 100% !important; max-width: 100% !important; page-break-inside: avoid; }
}
</style>
