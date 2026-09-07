// Universal Features platform-level routes (Phase 15).
export default [
  {
    path: "/",
    component: () => import("layouts/layout.vue"),
    children: [
      {
        path: "notifications",
        name: "uf_notifications",
        component: () => import("modules/universal/pages/NotificationsPage.vue"),
        meta: { requiresAuth: true, title: "Notifications" }
      },
      {
        path: "notifications/preferences",
        name: "uf_notification_preferences",
        component: () => import("modules/universal/pages/NotificationPreferencesPage.vue"),
        meta: { requiresAuth: true, title: "Notification Preferences" }
      },
      {
        path: "mentions",
        name: "uf_mentions",
        component: () => import("modules/universal/pages/MentionInbox.vue"),
        meta: { requiresAuth: true, title: "My Mentions" }
      },
      {
        path: "pinned",
        name: "uf_pinned",
        component: () => import("modules/universal/pages/PinnedRecordsPage.vue"),
        meta: { requiresAuth: true, title: "My Pinned" }
      },
      {
        path: "settings/tags",
        name: "uf_tags",
        component: () => import("modules/universal/pages/TagManagementPage.vue"),
        meta: { requiresAuth: true, permissions: ["settings.manage"], title: "Tag Management" }
      },
      {
        path: "settings/sticky-notes",
        name: "uf_sticky_notes_admin",
        component: () => import("modules/universal/pages/StickyNotesAdminPage.vue"),
        meta: { requiresAuth: true, permissions: ["settings.manage"], title: "Sticky Notes" }
      },
      {
        path: "settings/retention",
        name: "uf_retention",
        component: () => import("modules/universal/pages/RetentionSettingsPage.vue"),
        meta: { requiresAuth: true, permissions: ["records.adminDelete"], title: "Deleted Records Retention" }
      },
      {
        path: "settings/modified-log-config",
        name: "uf_modified_log_config",
        component: () => import("modules/universal/pages/ModifiedLogConfigPage.vue"),
        meta: { requiresAuth: true, permissions: ["settings.manage"], title: "Modified Log Configuration" }
      },
      {
        // Permalink convention: /entity/{type}/{id} → resolves to the entity's detail page.
        path: "entity/:type/:id",
        name: "uf_permalink",
        component: () => import("modules/universal/pages/PermalinkResolver.vue"),
        meta: { requiresAuth: true, title: "Open Record" }
      }
    ]
  }
];
