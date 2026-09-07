import { route } from "quasar/wrappers";
import { createRouter, createMemoryHistory, createWebHistory, createWebHashHistory } from "vue-router";
import routes from "./routes";
import { useAuthStore } from "stores/auth";
import { isJwtExpired } from "services/jwt";
import { useNotify } from "composables/useNotify";

/*
 * If not building with SSR mode, you can
 * directly export the Router instantiation;
 *
 * The function below can be async too; either use
 * async/await or return a Promise which resolves
 * with the Router instance.
 */

import authRoutes from "modules/auth/routes";
import accountRoutes from "modules/account/routes";
import tenantRoutes from "modules/tenant/routes";
import personRoutes from "modules/person/routes";
import userRoutes from "modules/user/routes";
import roleRoutes from "modules/role/routes";
import permissionGroupRoutes from "modules/permission-group/routes";
import userGroupRoutes from "modules/user-group/routes";
import dashboardRoutes from "modules/dashboard/routes";
import smtpRoutes from "modules/smtp/routes";
import emailTemplateRoutes from "modules/email-template/routes";
import optionSetRoutes from "modules/option-set/routes";
import universalRoutes from "modules/universal/routes";
import remsRoutes from "modules/rems/routes";

routes.push(...accountRoutes);
routes.push(...authRoutes);
routes.push(...tenantRoutes);
routes.push(...personRoutes);
routes.push(...userRoutes);
routes.push(...roleRoutes);
routes.push(...permissionGroupRoutes);
routes.push(...userGroupRoutes);
routes.push(...dashboardRoutes);
routes.push(...smtpRoutes);
routes.push(...emailTemplateRoutes);
routes.push(...optionSetRoutes);
routes.push(...universalRoutes);
routes.push(...remsRoutes);

export default route(function ({ store }) {
  const createHistory = process.env.SERVER
    ? createMemoryHistory
    : (process.env.VUE_ROUTER_MODE === "history" ? createWebHistory : createWebHashHistory);

  const Router = createRouter({
    scrollBehavior: () => ({ left: 0, top: 0 }),
    routes,
    history: createHistory(process.env.VUE_ROUTER_BASE)
  });

  Router.beforeEach(async (to, from, next) => {
    const authStore = useAuthStore(store);
    const requiresAuth = to.matched.some((record) => record.meta.requiresAuth);

    // Resolve the session before anything renders: a tab left open overnight holds an expired token,
    // and checking only that a token EXISTS is what renders the dashboard and then bounces to login.
    if (requiresAuth) {
      const signedIn = await authStore.ensureSession();
      if (!signedIn) {
        const isDefaultLanding = to.fullPath === "/" || to.fullPath === "/dashboard";
        return next({ name: "login", query: isDefaultLanding ? {} : { redirect: to.fullPath } });
      }
    }

    // Already logged in but on an auth page → the dashboard. An expired token does not count.
    if (to.path.startsWith("/auth") && authStore.isAuthenticated && !isJwtExpired(authStore.token)) {
      return next("/dashboard");
    }

    if (authStore.isAuthenticated) {
      // mustChangePassword gate: force the change-password page first.
      if (authStore.mustChangePassword && to.name !== "change_password") {
        return next({ name: "change_password" });
      }

      // Permission gate: route meta `permissions` requires any one of the listed permission keys
      // (decoded from the active-tenant JWT). The deepest matched record's list wins.
      const requiredPermissions = to.matched.reduce((permissions, record) => {
        return Array.isArray(record.meta.permissions) ? record.meta.permissions : permissions;
      }, null);

      if (Array.isArray(requiredPermissions) && requiredPermissions.length) {
        if (!authStore.hasAnyPermission(requiredPermissions)) {
          useNotify().notifyWarning("You do not have permission to access that page.");
          return next("/");
        }
      }
    }

    next();
  });
  return Router;
});
