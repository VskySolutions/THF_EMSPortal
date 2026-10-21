import { LANDING_PATH } from "modules/auth/returnPath";

const routes = [
  // Home is My REMS Requests; the Dashboard stays reachable from the menu.
  { path: "/", redirect: LANDING_PATH },
  { path: "/not-authorized", name: "not_authorized", component: () => import("src/pages/not_authorized.vue"), meta: { title: "Not Authorized" } },
  { path: "/:catchAll(.*)*", component: () => import("pages/error.vue"), meta: { title: "Error" } }
];

export default routes;
