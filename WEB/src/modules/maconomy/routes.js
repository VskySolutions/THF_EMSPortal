export default [
  {
    path: "/settings/maconomy",
    component: () => import("layouts/layout.vue"),
    children: [
      {
        path: "",
        name: "maconomy_settings",
        component: () => import("modules/maconomy/pages/MaconomySettingsPage.vue"),
        meta: { requiresAuth: true, permissions: ["integrations.maconomy.manage"], title: "Maconomy" }
      }
    ]
  }
];
