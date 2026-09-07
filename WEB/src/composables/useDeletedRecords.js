import { ref, computed } from "vue";
import { usePermissions, Permissions } from "composables/usePermissions";

// The "Show deleted?" state and permission gate a list needs to offer Deleted Records Management. const {
// showDeleted, canManageDeleted } = useDeletedRecords().
export function useDeletedRecords () {
  const { has } = usePermissions();
  const showDeleted = ref(false);
  const canManageDeleted = computed(() => has(Permissions.RecordsAdminDelete));
  return { showDeleted, canManageDeleted };
}
