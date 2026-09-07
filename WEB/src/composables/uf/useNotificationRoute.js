// Where a notification sends its reader.
import { useEntityMeta } from "composables/uf/useEntityMeta";
import { NotificationType } from "composables/uf/useNotificationMeta";
import { remsApi, EntityType } from "services/api";

// The three REMS types an approver can be a recipient of. RemsApprovalRequested is only ever sent to
// approvers; the other two are sent to a mixed set, and the lookup sorts them out.
const APPROVER_TYPES = new Set([
  NotificationType.RemsApprovalRequested,
  NotificationType.RemsEngagementApproved,
  NotificationType.RemsEngagementRejected
]);

export function useNotificationRoute () {
  const { routeFor } = useEntityMeta();

  // The route to open for a notification row. Async because the approver case needs the task resolved;
  // every other notification answers without a request.
  const routeForNotification = async (n) => {
    const fallback = routeFor(n.entityType, n.entityId);
    if (Number(n.entityType) !== EntityType.Rems || !APPROVER_TYPES.has(Number(n.type))) {
      return fallback;
    }
    // A failure here is not worth an error: the request is a correct destination for this reader too,
    // just not the best one. Falling back beats stranding them on the notification they clicked.
    const ref = await remsApi.myApprovalTaskForRequest(n.entityId).catch(() => null);
    return ref?.taskId
      ? { name: "rems_approval_task", params: { taskId: ref.taskId } }
      : fallback;
  };

  return { routeForNotification };
}
