export default [
  {
    path: "/background-jobs",
    component: () => import("layouts/layout.vue"),
    children: [
      {
        path: "",
        name: "background_jobs",
        component: () => import("modules/background-jobs/pages/index.vue"),
        meta: { requiresAuth: true, permissions: ["jobs.manage"], title: "Background Jobs" }
      }
    ]
  }
];
