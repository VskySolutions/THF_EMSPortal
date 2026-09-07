import { useAuthStore } from "stores/auth";

// Permission catalogue keys (mirror EmsPortal.Shared.Security.Permissions). Centralised so
// components reference a constant rather than scattering magic strings.
export const Permissions = Object.freeze({
  TenantsRead: "tenants.read",
  TenantsWrite: "tenants.write",
  TenantsArchive: "tenants.archive",
  PersonsRead: "persons.read",
  PersonsWrite: "persons.write",
  PersonsDelete: "persons.delete",
  UsersRead: "users.read",
  UsersWrite: "users.write",
  UsersResetPassword: "users.reset_password",
  UsersGroupManagement: "users.groupManagement",
  RolesRead: "roles.read",
  RolesWrite: "roles.write",
  RolesAssign: "roles.assign",
  GroupsManage: "groups.manage",
  EmailManage: "email.manage",
  // Universal Features (Phase 14/15).
  SettingsManage: "settings.manage",
  RecordsAdminDelete: "records.adminDelete",
  // Option Sets (tenant-configurable input value lists).
  OptionSetsRead: "optionSets.read",
  OptionSetsManage: "optionSets.manage",
  // REMS (Phase 15) — role-aware navigation is gated per permission, not per role name.
  RemsRequestsRead: "rems.requests.read",
  RemsRequestsCreate: "rems.requests.create",
  RemsRequestsUpdate: "rems.requests.update",
  RemsRequestsDelete: "rems.requests.delete",
  RemsRequestsAssign: "rems.requests.assign",
  RemsFormsManage: "rems.forms.manage",
  RemsFormsSend: "rems.forms.send",
  RemsEmailLogRead: "rems.emailLog.read",
  RemsEngagementsManage: "rems.engagements.manage",
  RemsApprovalsSend: "rems.approvals.send",
  // Tenant-wide REMS setup (the department-to-director map), kept apart from working the engagements
  // that flow through it.
  RemsSettingsManage: "rems.settings.manage",
  // Arranging cover for somebody ELSE. Naming your own delegates is self-service and needs no key.
  RemsDelegationsManage: "rems.delegations.manage"
  // No "approvals.act" key: deciding an approval task is authorised by owning the task, not by a
  // permission.
});

// Reactive permission checks for the active tenant. `has`/`hasAny` read the auth store's
// permissions (decoded from the JWT), so they stay reactive across tenant switches.
export function usePermissions () {
  const auth = useAuthStore();
  const has = (permission) => auth.hasPermission(permission);
  const hasAny = (permissions) => auth.hasAnyPermission(permissions);
  return { has, hasAny };
}
