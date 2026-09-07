// REMS (Phase 16, initiator-first rebuild).
export default [
  {
    path: "/rems",
    component: () => import("layouts/layout.vue"),
    children: [
      {
        // Open to every signed-in user, like the Approvals inbox and for the same reason: what somebody
        // sees here is decided by the records, not by a permission.
        path: "partner",
        name: "rems_partner",
        component: () => import("modules/rems/pages/PartnerDashboard.vue"),
        meta: { requiresAuth: true, title: "My REMS Requests" }
      },
      {
        // The Admin's only surface: requests whose clients have answered, opened for review of BOTH the
        // client's intake and the engagement setup.
        path: "ems-review",
        name: "rems_ems_review",
        component: () => import("modules/rems/pages/EmsReview.vue"),
        meta: { requiresAuth: true, permissions: ["rems.engagements.manage"], title: "EMS Review" }
      },
      { path: "client-forms", redirect: { name: "rems_ems_review" } },
      {
        // Related Entities: every submitted request whose client declared somebody ALONGSIDE themselves,
        // and how far each of those related clients has got.
        path: "related-entities",
        name: "rems_related_entities",
        component: () => import("modules/rems/pages/RelatedEntities.vue"),
        meta: { requiresAuth: true, title: "Related Entities" }
      },
      // THE form (WO-118): one tabbed page covering client information and the complete engagement setup.
      {
        // A request that does not exist yet has no id to live under and nothing to read — it is the form
        // and only the form. Gated on CREATE: reading requests is not permission to raise one.
        path: "requests/new",
        name: "rems_request_new",
        component: () => import("modules/rems/pages/RemsRequestForm.vue"),
        meta: { requiresAuth: true, permissions: ["rems.requests.create"], title: "New REMS Request" }
      },
      {
        path: "requests/edit/:id",
        name: "rems_request_edit",
        component: () => import("modules/rems/pages/RemsRequestForm.vue"),
        meta: { requiresAuth: true, title: "REMS Request" }
      },
      {
        // The read-only request detail.
        path: "requests/:id",
        name: "rems_request",
        component: () => import("modules/rems/pages/RemsRequestForm.vue"),
        meta: { requiresAuth: true, title: "REMS Request" }
      },
      // Links minted before the split: /rems/requests/:id/form, with ?mode=edit deciding which of the two
      // it meant.
      {
        path: "requests/:id/form",
        redirect: (to) => {
          const { mode, ...query } = to.query;
          if (to.params.id === "new") return { name: "rems_request_new", query };
          const name = mode === "edit" ? "rems_request_edit" : "rems_request";
          return { name, params: { id: to.params.id }, query };
        }
      },
      { path: "engagements/:id", redirect: (to) => ({ name: "rems_request", params: { id: to.params.id } }) },
      {
        // The task-isolated Approval Inbox (WO-117 Part B): the approver's OWN pending + historical tasks.
        path: "approvals",
        name: "rems_approvals",
        component: () => import("modules/rems/pages/ApprovalInbox.vue"),
        meta: { requiresAuth: true, title: "REMS Approvals" }
      },
      {
        // The approval-task review packet, the caller's checklist, and the checklist-gated Approve /
        // reason-required Reject decision.
        path: "approvals/:taskId",
        name: "rems_approval_task",
        component: () => import("modules/rems/pages/ApprovalTaskDetail.vue"),
        meta: { requiresAuth: true, title: "Approval Task" }
      }
    ]
  },
  // PUBLIC client EMS form (WO-113/116) — the anonymous, no-login onboarding form reached from the
  // emailed invite link ({App:BaseUrl}/rems/form/{inviteCode}).
  {
    path: "/rems/form/:inviteCode",
    component: () => import("layouts/public_layout.vue"),
    children: [
      {
        path: "",
        name: "rems_public_form",
        component: () => import("modules/rems/pages/PublicEmsForm.vue"),
        meta: { requiresAuth: false, title: "EMS Form" }
      }
    ]
  }
];
