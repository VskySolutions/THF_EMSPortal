export default [
  {
    path: "/error-log",
    component: () => import("layouts/layout.vue"),
    children: [
      {
        path: "",
        name: "error_log",
        component: () => import("modules/error-log/pages/index.vue"),
        meta: { requiresAuth: true, permissions: ["logs.read"], title: "Error Log" }
      }
    ]
  }
];
