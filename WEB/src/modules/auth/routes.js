const routes = [
  {
    path: "/auth",
    component: () => import("layouts/auth_layout.vue"),
    children: [
      { path: "", component: () => import("modules/auth/pages/login.vue"), meta: { title: "Login" } },
      { path: "login", name: "login", component: () => import("modules/auth/pages/login.vue"), meta: { title: "Login" } },
      // Self-service password reset. Both are anonymous: the emailed token is the only authorisation.
      { path: "forgot-password", name: "forgot_password", component: () => import("modules/auth/pages/forgot_password.vue"), meta: { title: "Forgot Password" } },
      { path: "reset-password", name: "reset_password", component: () => import("modules/auth/pages/reset_password.vue"), meta: { title: "Reset Password" } },
      // Where the API's Microsoft sign-in sends the browser back; it trades the one-time code for a session.
      { path: "sso/callback", name: "sso_callback", component: () => import("modules/auth/pages/sso_callback.vue"), meta: { title: "Signing in" } }
    ]
  }
];
export default routes;
