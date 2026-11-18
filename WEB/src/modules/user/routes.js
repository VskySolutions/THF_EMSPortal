export default [
  {
    path: "/users",
    component: () => import("layouts/layout.vue"),
    children: [
      {
        path: "",
        name: "users",
        component: () => import("modules/user/pages/index.vue"),
        meta: { requiresAuth: true, permissions: ["users.read"], title: "Users" }
      },
      {
        path: "imports",
        name: "user_imports",
        component: () => import("modules/user/pages/imports.vue"),
        meta: { requiresAuth: true, permissions: ["users.read"], title: "User Import History" }
      },
      {
        path: ":id",
        name: "user_detail",
        component: () => import("modules/user/pages/detail.vue"),
        meta: { requiresAuth: true, permissions: ["users.read"], title: "User" }
      }
    ]
  }
];
