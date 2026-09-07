import { ref } from "vue";
import { usePermissions, Permissions } from "composables/usePermissions";

// Drives the admin-only "Show Deleted" toggle on a list page.
export function useShowDeleted (reload) {
  const { has } = usePermissions();
  const canShowDeleted = has(Permissions.RecordsAdminDelete);
  const showDeleted = ref(false);

  const toggle = (value) => {
    showDeleted.value = typeof value === "boolean" ? value : !showDeleted.value;
    if (reload) reload();
  };

  const isDeleted = (row) => !!(row?.deleted || row?.isDeleted || row?.deletedOnUtc);
  const isOverdue = (row) => !!row?.isRetentionOverdue;

  return { canShowDeleted, showDeleted, toggle, isDeleted, isOverdue };
}
