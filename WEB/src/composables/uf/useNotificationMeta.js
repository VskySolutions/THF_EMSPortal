// NotificationType enum mirror (backend EmsPortal.Domain.Enums.NotificationType).
export const NotificationType = Object.freeze({
  Mention: 1,
  ReminderDue: 2,
  // REMS (Phase 15, WO-111..114). All in-app only; each carries the REMS request id via EntityType.Rems.
  // The three an APPROVER can receive send them to their own task instead — see useNotificationRoute.
  RemsRequestAssigned: 7,
  RemsCseAssigned: 8,
  RemsFormSent: 9,
  RemsFormSubmitted: 10,
  RemsApprovalRequested: 11,
  RemsEngagementApproved: 12,
  RemsEngagementRejected: 13,
  RemsRequestSubmitted: 15,
  RemsRequestPickedUp: 16
});

const META = {
  [NotificationType.Mention]: { label: "Mention", icon: "o_alternate_email", color: "primary" },
  [NotificationType.ReminderDue]: { label: "Reminder", icon: "o_alarm", color: "orange-8" },
  // Named for what it carries NOW: a client's answers landing on a request no admin has claimed, sent to
  // every admin. The number was minted for "assigned to you", back when an initiator named one.
  [NotificationType.RemsRequestAssigned]: { label: "Waiting for pickup", icon: "o_pan_tool_alt", color: "amber-8" },
  [NotificationType.RemsCseAssigned]: { label: "CSE assigned", icon: "o_support_agent", color: "primary" },
  [NotificationType.RemsFormSent]: { label: "EMS form sent", icon: "o_send", color: "teal-7" },
  [NotificationType.RemsFormSubmitted]: { label: "EMS form submitted", icon: "o_assignment_turned_in", color: "deep-purple-6" },
  [NotificationType.RemsApprovalRequested]: { label: "Approval requested", icon: "o_approval", color: "orange-8" },
  [NotificationType.RemsEngagementApproved]: { label: "Engagement approved", icon: "o_verified", color: "positive" },
  [NotificationType.RemsEngagementRejected]: { label: "Engagement rejected", icon: "o_cancel", color: "negative" },
  // Both are named for what they carry NOW rather than for the numbers they were minted under.
  [NotificationType.RemsRequestSubmitted]: { label: "Sent back for rework", icon: "o_assignment_return", color: "orange-9" },
  [NotificationType.RemsRequestPickedUp]: { label: "Picked up & rework updates", icon: "o_how_to_reg", color: "teal-7" }
};

const FALLBACK = { label: "Notification", icon: "o_notifications", color: "grey-7" };

export function useNotificationMeta () {
  const metaFor = (type) => META[Number(type)] || FALLBACK;
  // The full set of types for the preferences matrix.
  const allTypes = Object.values(NotificationType).map((value) => ({ value, ...(META[value] || FALLBACK) }));
  return { metaFor, allTypes, NotificationType };
}
