export default [
  {
    path: "/email-log",
    component: () => import("layouts/layout.vue"),
    children: [
      {
        path: "",
        name: "email_log",
        component: () => import("modules/email-log/pages/index.vue"),
        meta: { requiresAuth: true, permissions: ["email.manage"], title: "Email Log" }
      }
    ]
  }
];
