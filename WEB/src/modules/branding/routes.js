export default [
  {
    path: "/settings/branding",
    component: () => import("layouts/layout.vue"),
    children: [
      {
        path: "",
        name: "branding_settings",
        component: () => import("modules/branding/pages/BrandingSettingsPage.vue"),
        meta: { requiresAuth: true, permissions: ["branding.manage"], title: "Branding" }
      }
    ]
  }
];
