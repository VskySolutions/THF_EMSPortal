import { ref, computed } from "vue";
import { debounce } from "quasar";
import { dashboardApi, getApiErrorMessage } from "services/api";
import { useConfirm } from "composables/useConfirm";
import { useNotify } from "composables/useNotify";
import { widgetsForRole, defaultLayoutForRole, defaultHiddenForRole, WIDGETS_BY_KEY } from "modules/dashboard/widgets/registry";

// Per-user dashboard layout state (WO-73).
export function useDashboardLayout (role) {
  const { confirm } = useConfirm();
  const notify = useNotify();

  const widgetOrder = ref([]);
  const hiddenWidgets = ref([]);
  const collapsedWidgets = ref([]);

  const isHidden = (key) => hiddenWidgets.value.includes(key);
  const isCollapsed = (key) => collapsedWidgets.value.includes(key);

  // Registry entries visible to the role, ordered by widgetOrder, excluding hidden widgets.
  const visibleWidgets = computed(() => {
    const allowed = new Set(widgetsForRole(role).map((w) => w.key));
    return widgetOrder.value
      .filter((key) => allowed.has(key) && !isHidden(key) && WIDGETS_BY_KEY[key])
      .map((key) => ({ key, ...WIDGETS_BY_KEY[key] }));
  });

  const persist = debounce(async () => {
    try {
      await dashboardApi.saveLayout({
        widgetOrder: widgetOrder.value,
        hiddenWidgets: hiddenWidgets.value,
        collapsedWidgets: collapsedWidgets.value
      });
    } catch (err) {
      notify.error(getApiErrorMessage(err, "Could not save your dashboard layout."));
    }
  }, 500);

  const saveLayout = () => persist();

  const loadLayout = async () => {
    try {
      const layout = await dashboardApi.getLayout();
      widgetOrder.value = Array.isArray(layout?.widgetOrder) ? [...layout.widgetOrder] : [];
      hiddenWidgets.value = Array.isArray(layout?.hiddenWidgets) ? [...layout.hiddenWidgets] : [];
      collapsedWidgets.value = Array.isArray(layout?.collapsedWidgets) ? [...layout.collapsedWidgets] : [];
    } catch {
      // Fall through to the default layout on any load error.
      widgetOrder.value = [];
      hiddenWidgets.value = [];
      collapsedWidgets.value = [];
    }
    if (!widgetOrder.value.length) {
      widgetOrder.value = defaultLayoutForRole(role);
      // Apply the default-hidden set only when the server gave us no layout at all (fresh/errored),
      // so a saved layout that legitimately hides nothing is never overridden.
      if (!hiddenWidgets.value.length) {
        hiddenWidgets.value = defaultHiddenForRole(role);
      }
    }
  };

  const setOrder = (keys) => {
    widgetOrder.value = [...keys];
    saveLayout();
  };

  const toggleHidden = (key) => {
    hiddenWidgets.value = isHidden(key)
      ? hiddenWidgets.value.filter((k) => k !== key)
      : [...hiddenWidgets.value, key];
    saveLayout();
  };

  const setCollapsed = (key, val) => {
    const next = !!val;
    if (next && !isCollapsed(key)) {
      collapsedWidgets.value = [...collapsedWidgets.value, key];
    } else if (!next && isCollapsed(key)) {
      collapsedWidgets.value = collapsedWidgets.value.filter((k) => k !== key);
    } else {
      return;
    }
    saveLayout();
  };

  const resetToDefault = async () => {
    const ok = await confirm({
      title: "Reset dashboard",
      message: "Restore the default widget layout? Your customisations will be lost.",
      confirmLabel: "Reset"
    });
    if (!ok) return;
    widgetOrder.value = defaultLayoutForRole(role);
    hiddenWidgets.value = defaultHiddenForRole(role);
    collapsedWidgets.value = [];
    saveLayout();
    notify.success("Dashboard layout reset.");
  };

  return {
    widgetOrder,
    hiddenWidgets,
    collapsedWidgets,
    visibleWidgets,
    isHidden,
    isCollapsed,
    loadLayout,
    saveLayout,
    resetToDefault,
    setOrder,
    toggleHidden,
    setCollapsed
  };
}
