import { http, http2 } from "boot/axios";

// Shared API access points.
// `api`     → authenticated instance (Bearer token + tenant + correlation-id headers).
// `anonApi` → anonymous instance (login, refresh).
export const api = http;
export const anonApi = http2;

// Platform-wide stable error codes — mirrors EmsPortal.Shared.Contracts.ApiErrorCodes.
export const ApiErrorCodes = Object.freeze({
  ValidationFailed: "VALIDATION_FAILED",
  Unauthorized: "UNAUTHORIZED",
  Forbidden: "FORBIDDEN",
  NotFound: "NOT_FOUND",
  DuplicateIdentifier: "DUPLICATE_IDENTIFIER",
  DuplicateGroupName: "DUPLICATE_GROUP_NAME",
  PermissionCeilingExceeded: "PERMISSION_CEILING_EXCEEDED",
  CapacityBelowUsage: "CAPACITY_BELOW_USAGE",
  CapacityLimitReached: "CAPACITY_LIMIT_REACHED",
  TenantInactive: "TENANT_INACTIVE",
  TenantNotFound: "TENANT_NOT_FOUND",
  TenantArchived: "TENANT_ARCHIVED",
  InternalError: "INTERNAL_ERROR"
});

/**
 * @typedef {Object} PaginatedMeta
 * @property {number} page
 * @property {number} limit
 * @property {number} totalRecords
 */
/**
 * @template T
 * @typedef {Object} ApiResponse
 * @property {boolean} success
 * @property {string} message
 * @property {T} [data]
 * @property {PaginatedMeta} [meta]
 */

/** Extract a caller-safe message from an Axios error. */
export function getApiErrorMessage (error, fallback = "Something went wrong. Please try again.") {
  return (
    error?.response?.data?.error?.details ||
    error?.response?.data?.message ||
    (typeof error?.response?.data === "string" ? error.response.data : null) ||
    error?.message ||
    fallback
  );
}

/** Extract the stable machine-readable error code, if present. */
export function getApiErrorCode (error) {
  return error?.response?.data?.error?.code || null;
}

/**
 * Completes a link that points at this web app, using WEB_BASE_URL from config/env.<mode>.cjs.
 *
 * The API mints shareable links (the REMS client form link) from its own `App:BaseUrl`, which is a
 * separate setting and is empty in a fresh environment — leaving a relative "/rems/form/abc123" that is
 * useless once copied out of the browser. The SPA always knows its own origin, so it fills the gap.
 * An already-absolute URL is returned untouched, so a correctly configured API still wins.
 */
export function webUrl (pathOrUrl) {
  if (!pathOrUrl) return "";
  if (/^[a-z][a-z0-9+.-]*:\/\//i.test(pathOrUrl)) return pathOrUrl;
  const base = (process.env.WEB_BASE_URL || "").replace(/\/+$/, "");
  return `${base}${pathOrUrl.startsWith("/") ? "" : "/"}${pathOrUrl}`;
}

// Unwrap the standard ApiResponse envelope to its `data` (and attach `meta` for lists).
const unwrap = (response) => response?.data?.data;
const envelope = (response) => response?.data;

// ---------------------------------------------------------------------------
// Resource groups (mapped to the EMS Portal API controllers)
// ---------------------------------------------------------------------------

export const authApi = {
  login: (credentials) => anonApi.post("/api/auth/login", credentials).then(envelope),
  refresh: (refreshToken) => anonApi.post("/api/auth/refresh", { refreshToken }).then(envelope),
  logout: (refreshToken) => api.post("/api/auth/logout", { refreshToken }).then(envelope),
  logoutAll: () => api.post("/api/auth/logout-all").then(envelope),
  switchTenant: (tenantId) => api.post("/api/auth/switch-tenant", { tenantId }).then(envelope),
  profile: () => api.get("/api/auth/profile").then(unwrap),
  changePassword: (currentPassword, newPassword) =>
    api.put("/api/users/me/change-password", { currentPassword, newPassword }).then(envelope),
  updateMe: (displayName) => api.put("/api/users/me", { displayName }).then(unwrap),
  effectivePermissions: () => api.get("/api/auth/effective-permissions").then(unwrap),
  // Self-service reset. `forgotPassword` always resolves the same way whether or not the address exists —
  // never branch the UI on its response, that would leak which accounts are real.
  forgotPassword: (email) => anonApi.post("/api/auth/forgot-password", { email }).then(envelope),
  resetPassword: (token, newPassword) =>
    anonApi.post("/api/auth/reset-password", { token, newPassword }).then(envelope)
};

export const tenantApi = {
  list: (params) => api.get("/api/admin/tenants", { params }).then(envelope),
  get: (id) => api.get(`/api/admin/tenants/${id}`).then(unwrap),
  create: (payload) => api.post("/api/admin/tenants", payload).then(unwrap),
  update: (id, payload) => api.put(`/api/admin/tenants/${id}`, payload).then(unwrap),
  setStatus: (id, isActive) => api.put(`/api/admin/tenants/${id}/status`, { isActive }).then(unwrap),
  archive: (id) => api.put(`/api/admin/tenants/${id}/archive`).then(unwrap)
};

export const personApi = {
  list: (params) => api.get("/api/admin/persons", { params }).then(envelope),
  get: (id) => api.get(`/api/admin/persons/${id}`).then(unwrap),
  create: (payload) => api.post("/api/admin/persons", payload).then(unwrap),
  update: (id, payload) => api.put(`/api/admin/persons/${id}`, payload).then(unwrap),
  remove: (id) => api.delete(`/api/admin/persons/${id}`).then(envelope),
  // Lightweight options for the user-create Person dropdown (each carries isUser). `tenantId` reads
  // ANOTHER tenant's people — what the tenant-management screen creates accounts from — and is honoured
  // only for a caller holding tenants.write; everyone else gets their own tenant either way.
  selectable: (tenantId) => api.get("/api/admin/persons/selectable", { params: { tenantId } }).then(unwrap)
};

export const userApi = {
  // params: { page, limit, search?, isActive?, name?, email?, phone?, role?, group?, tenantId? }.
  // Without `tenantId` this is the caller's ACTIVE tenant, whoever they are. `tenantId` names one other
  // tenant — the tenant-management screen listing that tenant's own accounts — and is honoured only for a
  // caller holding tenants.write; for anybody else it is ignored, not refused.
  list: (params) => api.get("/api/admin/users", { params }).then(envelope),
  get: (id) => api.get(`/api/admin/users/${id}`).then(unwrap),
  // payload: { personId, email?, phoneNumber?, countryCode?, tenantId, roleIds[] } — promotes a Person
  // to a login account with one or more RBAC roles in the tenant (multi-role, WO-123).
  create: (payload) => api.post("/api/admin/users", payload).then(unwrap),
  update: (id, payload) => api.put(`/api/admin/users/${id}`, payload).then(unwrap),
  setStatus: (id, isActive) => api.put(`/api/admin/users/${id}/status`, { isActive }).then(unwrap),
  // Admin password reset (REQ-ADM-013) — returns a new temporary password.
  resetPassword: (id) => api.post(`/api/admin/users/${id}/reset-password`).then(unwrap),
  // payload: { tenantId, roleIds[] } — reconciles the full set of roles the user holds in the tenant
  // (adds/removes to match; an empty set removes tenant access). WO-123 multi-role.
  assignTenantRole: (id, payload) =>
    api.post(`/api/admin/users/${id}/tenant-assignments`, payload).then(unwrap),
  removeTenantRole: (id, tenantId) =>
    api.delete(`/api/admin/users/${id}/tenant-assignments/${tenantId}`).then(envelope),
  // Replace the user's group memberships with the given set of group ids.
  setGroups: (id, groupIds) => api.put(`/api/admin/users/${id}/groups`, { groupIds }).then(unwrap),
  // Picker data for the department section: the tenant's REMS.Department list and the current head of
  // each → { departments: [{ value, label }], heads: [{ department, userId, fullName }] }.
  departments: () => api.get("/api/admin/users/departments").then(unwrap),
  // payload: { department: code|null, isHead } — a null department unassigns the user. Marking a head
  // demotes the incumbent and repoints the REMS department-director mapping (WO-114), so the response
  // carries { department, isHead, demotedHeadName } for reporting the handover.
  setDepartment: (id, payload) => api.put(`/api/admin/users/${id}/department`, payload).then(unwrap),
};

// Tenant-scoped user groups (segmentation/tagging, independent of RBAC roles).
export const userGroupApi = {
  // params: { search?, sortBy?, descending? } — ordering is the server', not the browser's.
  list: (params) => api.get("/api/admin/user-groups", { params }).then(unwrap),
  // payload: { name, description? } → created (or existing) group
  create: (payload) => api.post("/api/admin/user-groups", payload).then(unwrap),
  remove: (id) => api.delete(`/api/admin/user-groups/${id}`).then(envelope),
  // ---- Members ----
  members: (id) => api.get(`/api/admin/user-groups/${id}/members`).then(unwrap),
  addMembers: (id, userIds) => api.post(`/api/admin/user-groups/${id}/members`, { userIds }).then(unwrap),
  removeMember: (id, userId) => api.delete(`/api/admin/user-groups/${id}/members/${userId}`).then(envelope)
};

export const roleApi = {
  list: (params) => api.get("/api/admin/roles", { params }).then(unwrap),
  get: (id) => api.get(`/api/admin/roles/${id}`).then(unwrap),
  create: (payload) => api.post("/api/admin/roles", payload).then(unwrap),
  update: (id, payload) => api.put(`/api/admin/roles/${id}`, payload).then(unwrap),
  remove: (id) => api.delete(`/api/admin/roles/${id}`).then(envelope),
  // Full permission catalogue, for the role permission picker.
  permissions: () => api.get("/api/admin/permissions").then(unwrap),
  // Roles assignable within a tenant (system roles + the tenant's custom roles) — drives user role pickers.
  tenantRoles: (tenantId) => api.get(`/api/admin/tenants/${tenantId}/roles`).then(unwrap),
  // Tenant ids a role is currently available to (for the role↔tenant availability editor).
  roleTenants: (id) => api.get(`/api/admin/roles/${id}/tenants`).then(unwrap),
  assignToTenant: (tenantId, roleId) =>
    api.post(`/api/admin/tenants/${tenantId}/roles`, { roleId }).then(unwrap),
  unassignFromTenant: (tenantId, roleId) =>
    api.delete(`/api/admin/tenants/${tenantId}/roles/${roleId}`).then(envelope),
  // ---- Role membership: who holds the role in the caller's active tenant (roles.assign) ----
  // Membership is tenant data even when the role is not, so these work on a platform role too.
  users: (roleId) => api.get(`/api/admin/roles/${roleId}/users`).then(unwrap),
  userCandidates: (roleId) => api.get(`/api/admin/roles/${roleId}/users/candidates`).then(unwrap),
  // The envelope, not just the data: the message says how many were granted, or that they already held it.
  addUsers: (roleId, userIds) => api.post(`/api/admin/roles/${roleId}/users`, { userIds }).then(envelope),
  removeUser: (roleId, userId) => api.delete(`/api/admin/roles/${roleId}/users/${userId}`).then(envelope),
  // ---- Role ↔ Permission Group composition (WO-70) ----
  // The role's assigned groups + the role's effective permission set.
  getGroups: (roleId) => api.get(`/api/admin/roles/${roleId}/groups`).then(unwrap),
  assignGroups: (roleId, groupIds) => api.post(`/api/admin/roles/${roleId}/groups`, { groupIds }).then(unwrap),
  removeGroup: (roleId, groupId) => api.delete(`/api/admin/roles/${roleId}/groups/${groupId}`).then(unwrap),
  // Union of effective permissions for a role → { permissions, sources }.
  previewPermissions: (roleId) => api.get(`/api/admin/roles/${roleId}/permissions/preview`).then(unwrap)
};

// Permission Groups (WO-70): the RBAC composition layer (Permission Keys → Groups → Roles → Users).
// Tenant-scoped with a Super Admin `tenantId` override (query on list, body on create); everyone
// else is auto-scoped server-side. Mutations require `groups.manage`.
export const permissionGroupApi = {
  list: (params) => api.get("/api/admin/permission-groups", { params }).then(envelope),
  get: (id) => api.get(`/api/admin/permission-groups/${id}`).then(unwrap),
  // payload: { tenantId?, name, description?, permissionKeys[] }
  create: (payload) => api.post("/api/admin/permission-groups", payload).then(unwrap),
  // payload: { name, description?, permissionKeys[] }
  update: (id, payload) => api.put(`/api/admin/permission-groups/${id}`, payload).then(unwrap),
  setStatus: (id, isActive) => api.put(`/api/admin/permission-groups/${id}/status`, { isActive }).then(unwrap),
  remove: (id) => api.delete(`/api/admin/permission-groups/${id}`).then(envelope),
  // ---- Templates ----
  // Read only: nothing in the app creates a template (POST .../templates is served, unwrapped).
  templates: () => api.get("/api/admin/permission-groups/templates").then(unwrap),
  // Full permission key catalogue (string[]) — drives the key picker.
  permissionCatalog: () => api.get("/api/admin/permissions").then(unwrap)
};

export const profileApi = {
  // Current user's person profile. The admin-side pair (GET/PUT /api/admin/users/{id}/profile) has no
  // caller — a user's person is edited on the People screen — so only the self endpoints are wrapped.
  getMine: () => api.get("/api/users/me/profile").then(unwrap),
  updateMine: (payload) => api.put("/api/users/me/profile", payload).then(unwrap)
};

export const mediaApi = {
  // Uploads a file (multipart) and returns the stored media (incl. publicUrl).
  // `entity` ({ type, id }) names the record the file belongs to and is what files it under
  // media-uploads/{tenant}/{EntityType}/{recordKey}/{purpose}/. Omitting it is not an error but lands
  // the file in the swept `_unassigned` holding pen, so pass it wherever the parent id is known.
  upload: (file, mediaCategory = "Profile", entity = null) => {
    const form = new FormData();
    form.append("file", file);
    form.append("mediaCategory", mediaCategory);
    if (entity?.type && entity?.id) {
      form.append("entityType", entity.type);
      form.append("entityId", entity.id);
    }
    return api.post("/api/media", form, { headers: { "Content-Type": "multipart/form-data" } }).then(unwrap);
  },
  // Absolute URL for a media public path (the API serves public media anonymously).
  // ONLY profile pictures are public — everything else 401s on a bare URL, so use `content` below
  // (or openStoredFile in composables/useFilePreview) for attachments.
  absoluteUrl: (publicUrl) => (publicUrl ? `${process.env.API_BASE_URL || ""}${publicUrl}` : null),
  // The file's bytes, fetched through the AUTHENTICATED client. /api/media/{id}/content refuses an
  // anonymous caller for anything but a profile picture, and a browser following a plain href — an
  // <a target="_blank">, an <img src>, a new tab — sends no Authorization header, which is what made
  // every attachment answer {"code":"UNAUTHORIZED","details":"Authentication required."}. Fetching the
  // blob here and handing THAT to the tab is what makes an attachment openable.
  content: (mediaId) => api.get(`/api/media/${mediaId}/content`, { responseType: "blob" }).then((r) => r?.data)
};

// Per-tenant SMTP email accounts (WO-80/81). Reads require users.read; writes require email.manage.
// Super Admins target a tenant via the `tenantId` query (list/get/write) or create body; everyone
// else is auto-scoped to their active tenant. Passwords are write-only and never returned.
export const smtpAccountApi = {
  // params: { tenantId?, status? } — status is "active" | "inactive".
  list: (params) => api.get("/api/admin/smtp-accounts", { params }).then(envelope),
  get: (id, tenantId) => api.get(`/api/admin/smtp-accounts/${id}`, { params: { tenantId } }).then(unwrap),
  // payload: { tenantId?, accountName, host, port, encryptionType, authType, username?, password?, fromName, fromEmail }
  create: (payload) => api.post("/api/admin/smtp-accounts", payload).then(unwrap),
  // payload: same as create minus tenantId; omit password to preserve the existing one.
  update: (id, payload, tenantId) =>
    api.put(`/api/admin/smtp-accounts/${id}`, payload, { params: { tenantId } }).then(unwrap),
  remove: (id, tenantId) => api.delete(`/api/admin/smtp-accounts/${id}`, { params: { tenantId } }).then(envelope),
  activate: (id, tenantId) => api.put(`/api/admin/smtp-accounts/${id}/activate`, null, { params: { tenantId } }).then(unwrap),
  // body: { recipientEmail } → { success, sentAtUtc?, serverResponse?, errorCategory?, errorDetail? }
  test: (id, recipientEmail, tenantId) =>
    api.post(`/api/admin/smtp-accounts/${id}/test`, { recipientEmail }, { params: { tenantId } }).then(unwrap)
};

// Transactional email templates (WO email templates). Reads require users.read; writes require
// email.manage. Tenant Admins manage their tenant overrides; Super Admins manage the platform
// defaults (`global: true`) or any tenant (`tenantId`). `params` carries `{ tenantId?, global? }`.
export const emailTemplateApi = {
  list: (params) => api.get("/api/admin/email-templates", { params }).then(envelope),
  get: (key, params) => api.get(`/api/admin/email-templates/${key}`, { params }).then(unwrap),
  // payload: { subject, body }
  save: (key, payload, params) => api.put(`/api/admin/email-templates/${key}`, payload, { params }).then(unwrap),
  reset: (key, params) => api.delete(`/api/admin/email-templates/${key}`, { params }).then(unwrap),
  // payload: { subject?, body? } — renders the draft (or the effective template) with sample data.
  preview: (key, payload, params) => api.post(`/api/admin/email-templates/${key}/preview`, payload, { params }).then(unwrap)
};

// How an option list orders its items. Mirrors EmsPortal.Domain.Enums.OptionItemSortMode.
export const OptionItemSortMode = Object.freeze({
  AlphabeticalAsc: "AlphabeticalAsc",
  AlphabeticalDesc: "AlphabeticalDesc",
  Custom: "Custom"
});

// Tenant-configurable input value lists (e.g. Payment Terms). Reads require optionSets.read; writes
// require optionSets.manage. Standard (seeded) lists are returned read-only; only a tenant's own
// lists can be modified. Scoped to the caller's active tenant.
export const optionSetApi = {
  // params: { entityType? } — EntityType enum value.
  list: (params) => api.get("/api/option-sets", { params }).then(unwrap),
  get: (id) => api.get(`/api/option-sets/${id}`).then(unwrap),
  // Effective active values for a key: { entityType, key, parentItemId? }.
  resolve: (params) => api.get("/api/option-sets/resolve", { params }).then(unwrap),
  // payload: { entityType, key, name, parentSetId?, itemSortMode }
  create: (payload) => api.post("/api/option-sets", payload).then(unwrap),
  // payload: { name, itemSortMode, isActive }
  update: (id, payload) => api.put(`/api/option-sets/${id}`, payload).then(unwrap),
  remove: (id) => api.delete(`/api/option-sets/${id}`).then(unwrap),
  // payload: { value, label, parentItemId?, isDefault, backgroundColor?, textColor?, metadataJson? }
  createItem: (setId, payload) => api.post(`/api/option-sets/${setId}/items`, payload).then(unwrap),
  // payload: { value, label, parentItemId?, isDefault, isActive, backgroundColor?, textColor?, metadataJson? }
  updateItem: (setId, itemId, payload) => api.put(`/api/option-sets/${setId}/items/${itemId}`, payload).then(unwrap),
  removeItem: (setId, itemId) => api.delete(`/api/option-sets/${setId}/items/${itemId}`).then(unwrap),
  // payload: itemIds in the desired order.
  reorderItems: (setId, itemIds) => api.put(`/api/option-sets/${setId}/items/reorder`, { itemIds }).then(unwrap)
};

// ---------------------------------------------------------------------------
// Universal Features (Phase 14/15). Attach to any entity via (entityType, entityId).
// EntityType — mirrors EmsPortal.Domain.Enums.EntityType. Values are append-only: they are persisted on
// every Universal Feature row (notes, tags, attachments, pins, …), so renumbering would silently
// re-point existing data.
// ---------------------------------------------------------------------------

export const EntityType = Object.freeze({
  Tenant: 3,
  User: 4,
  UserGroup: 5,
  Rems: 6,
  Person: 7,
  Role: 8,
  OptionSet: 9,
  PermissionGroup: 10,
  SmtpAccount: 11,
  EmailTemplate: 12,
  Tag: 13,
  // 14 was SavedView, retired with the feature — the number stays reserved (see EntityType.cs).
  StickyNote: 15
});

// Conversations — a record's @mention-aware thread. There is no conversation resource of its own: the
// thread IS the messages sharing an (entityType, entityId), which is what `list` asks for.
export const ufConversationApi = {
  list: (params) => api.get("/api/uf/conversation-messages", { params }).then(envelope),
  create: (payload) => api.post("/api/uf/conversation-messages", payload).then(unwrap),
  update: (id, payload) => api.put(`/api/uf/conversation-messages/${id}`, payload).then(unwrap),
  remove: (id) => api.delete(`/api/uf/conversation-messages/${id}`).then(envelope),
  // Tenant users for the @mention autocomplete.
  mentionCandidates: (search) => api.get("/api/uf/mention-candidates", { params: { search } }).then(unwrap)
};

// Tags (admin CRUD + entity application).
export const ufTagsApi = {
  // params: { search?, sortBy?, descending? }
  list: (params) => api.get("/api/admin/tags", { params }).then(unwrap),
  // Read-only picker list available to any tenant user (for applying tags).
  picker: (search) => api.get("/api/uf/tags", { params: { search } }).then(unwrap),
  create: (payload) => api.post("/api/admin/tags", payload).then(unwrap),
  update: (id, payload) => api.put(`/api/admin/tags/${id}`, payload).then(unwrap),
  remove: (id) => api.delete(`/api/admin/tags/${id}`).then(envelope),
  entityTags: (entityType, entityId) => api.get("/api/uf/entity-tags", { params: { entityType, entityId } }).then(unwrap),
  apply: (payload) => api.post("/api/uf/entity-tags", payload).then(unwrap),
  removeApplication: (id) => api.delete(`/api/uf/entity-tags/${id}`).then(envelope)
};

// Attachments.
export const ufAttachmentsApi = {
  list: (entityType, entityId) => api.get("/api/uf/attachments", { params: { entityType, entityId } }).then(unwrap),
  upload: (entityType, entityId, file) => {
    const form = new FormData();
    form.append("file", file);
    form.append("entityType", entityType);
    form.append("entityId", entityId);
    return api.post("/api/uf/attachments", form, { headers: { "Content-Type": "multipart/form-data" } }).then(unwrap);
  },
  download: (id) => api.get(`/api/uf/attachments/${id}/download`, { responseType: "blob" }).then((r) => r?.data),
  remove: (id) => api.delete(`/api/uf/attachments/${id}`).then(envelope)
};

// Activity timeline (read-only).
export const ufActivityApi = {
  list: (params) => api.get("/api/uf/activity", { params }).then(envelope)
};

// Reminders (personal).
export const ufReminderApi = {
  list: (params) => api.get("/api/uf/reminders", { params }).then(envelope),
  create: (payload) => api.post("/api/uf/reminders", payload).then(unwrap),
  update: (id, payload) => api.put(`/api/uf/reminders/${id}`, payload).then(unwrap),
  remove: (id) => api.delete(`/api/uf/reminders/${id}`).then(envelope)
};

// Notification centre + preferences.
export const ufNotificationApi = {
  // params: { page?, limit?, isRead?, type?, search?, createdFrom?, createdTo? } — always the caller's
  // own notifications. `type` is a NotificationType id, `search` matches the title or body, and
  // createdFrom/To are UTC instants (a date-only picker converts its own day boundaries — see
  // zonedDayBoundaryUtc). Filtering is server-side, so the pager reports the filtered total.
  list: (params) => api.get("/api/notifications", { params }).then(envelope),
  unreadCount: () => api.get("/api/notifications/unread-count").then(unwrap),
  markRead: (id) => api.put(`/api/notifications/${id}/read`).then(envelope),
  markAllRead: () => api.put("/api/notifications/read-all").then(envelope),
  getPreferences: () => api.get("/api/notifications/preferences").then(unwrap),
  updatePreferences: (preferences) => api.put("/api/notifications/preferences", { preferences }).then(envelope),
  // Mention inbox.
  mentions: (params) => api.get("/api/uf/mentions", { params }).then(envelope),
  markMentionRead: (id) => api.put(`/api/uf/mentions/${id}/read`).then(envelope)
};

// Pins (bookmarks).
export const ufPinApi = {
  list: (params) => api.get("/api/uf/pins", { params }).then(envelope),
  create: (payload) => api.post("/api/uf/pins", payload).then(unwrap),
  remove: (id) => api.delete(`/api/uf/pins/${id}`).then(envelope)
};

// Colour codes (row tinting).
export const ufColourApi = {
  batch: (entityType, entityIds) => api.get("/api/uf/colour-codes", { params: { entityType, entityIds }, paramsSerializer: { indexes: null } }).then(unwrap),
  upsert: (payload) => api.put("/api/uf/colour-codes", payload).then(unwrap)
};

// PDF export (binary stream).
export const ufPdfApi = {
  export: (payload) => api.post("/api/uf/pdf-export", payload, { responseType: "blob" }).then((r) => r?.data)
};

// Checklists.
export const ufChecklistApi = {
  list: (entityType, entityId) => api.get("/api/uf/checklists", { params: { entityType, entityId } }).then(unwrap),
  create: (payload) => api.post("/api/uf/checklists", payload).then(unwrap),
  addItem: (id, text) => api.post(`/api/uf/checklists/${id}/items`, { text }).then(unwrap),
  toggleItem: (id, itemId, isCompleted) => api.patch(`/api/uf/checklists/${id}/items/${itemId}`, { isCompleted }).then(unwrap),
  editItem: (id, itemId, text) => api.put(`/api/uf/checklists/${id}/items/${itemId}`, { text }).then(unwrap),
  reorder: (id, itemIds) => api.put(`/api/uf/checklists/${id}/reorder`, { itemIds }).then(unwrap),
  removeItem: (id, itemId) => api.delete(`/api/uf/checklists/${id}/items/${itemId}`).then(envelope),
  remove: (id) => api.delete(`/api/uf/checklists/${id}`).then(envelope)
};

// Sticky notes (personal + tenant broadcast) and per-user layout state.
export const ufStickyNoteApi = {
  list: (scope) => api.get("/api/uf/sticky-notes", { params: { scope } }).then(unwrap),
  create: (payload) => api.post("/api/uf/sticky-notes", payload).then(unwrap),
  update: (id, payload) => api.put(`/api/uf/sticky-notes/${id}`, payload).then(unwrap),
  remove: (id) => api.delete(`/api/uf/sticky-notes/${id}`).then(envelope),
  dismiss: (id) => api.post(`/api/uf/sticky-notes/${id}/dismiss`).then(envelope),
  saveState: (noteId, payload) => api.put(`/api/uf/sticky-note-states/${noteId}`, payload).then(envelope),
  // params: { sortBy?, descending? }
  adminList: (params) => api.get("/api/admin/sticky-notes", { params }).then(unwrap)
};

// Deleted records management.
export const ufDeletedApi = {
  list: (params) => api.get("/api/uf/deleted", { params }).then(envelope),
  restore: (payload) => api.post("/api/uf/restore", payload).then(envelope),
  restoreBulk: (payload) => api.post("/api/uf/restore/bulk", payload).then(unwrap),
  hardDelete: (payload) => api.delete("/api/uf/hard-delete", { data: payload }).then(envelope),
  hardDeleteBulk: (payload) => api.delete("/api/uf/hard-delete/bulk", { data: payload }).then(envelope),
  getRetention: (tenantId) => api.get("/api/admin/retention-config", { params: { tenantId } }).then(unwrap),
  updateRetention: (retentionDays, tenantId) => api.put("/api/admin/retention-config", { retentionDays }, { params: { tenantId } }).then(unwrap),
  overdue: (tenantId) => api.get("/api/admin/retention-overdue", { params: { tenantId } }).then(unwrap)
};

// Modified log (field change history).
export const ufModifiedLogApi = {
  history: (params) => api.get("/api/uf/modified-log", { params }).then(envelope),
  iconCounts: (entityType, entityId) => api.get("/api/uf/modified-log/icon-counts", { params: { entityType, entityId } }).then(unwrap),
  config: (entityType) => api.get("/api/admin/modified-log-config", { params: { entityType } }).then(unwrap),
  toggleConfig: (fieldKey, isEnabled) => api.patch(`/api/admin/modified-log-config/${fieldKey}`, { isEnabled }).then(unwrap)
};

// Dashboard (WO-73). Role-aware analytics endpoints + per-user layout persistence. Tenant-scoped
// endpoints auto-scope to the active tenant; Super Admins may target a tenant via `tenantId`.
// `params` carries `{ dateRange, tenantId? }`. All responses use the standard ApiResponse envelope.
export const dashboardApi = {
  users: (params) => api.get("/api/dashboard/users", { params }).then(unwrap),
  // Super Admin platform overview. `forceRefresh` bypasses the server cache via a request header.
  platform: (params, forceRefresh = false) =>
    api.get("/api/dashboard/platform", {
      params,
      headers: forceRefresh ? { "X-Dashboard-Force-Refresh": "1" } : undefined
    }).then(unwrap),
  // Per-user widget layout (order / hidden / collapsed).
  getLayout: () => api.get("/api/dashboard/layout").then(unwrap),
  // payload: { widgetOrder, hiddenWidgets, collapsedWidgets }
  saveLayout: (payload) => api.put("/api/dashboard/layout", payload).then(unwrap)
};

// REMS (Phase 15, WO-111/115). Request lifecycle: the Partner Dashboard + EMS Review lists, the
// create/edit/pick-up/delete actions, and the client + people pickers. Row visibility and the
// per-row `actions` flags are enforced server-side; the UI additionally gates on permission keys.
// The conversation thread / activity / attachments reuse the Universal Features endpoints keyed on
// EntityType.Rems (see ufConversationApi) — this object deliberately does not duplicate them.
export const remsApi = {
  // params: { scope?, poolScope?, ownership?, clientName?, contact?, status?, type?,
  //           assignedAdminUserId?, createdFrom?, createdTo?, page?, limit? }
  // scope: "partner" | "pool"; poolScope: "unassigned" | "mine" | "all". ownership is the My Requests
  // toggle — "mine" (the default) or "all"; it is a view, not a right, so "all" widens the list only for
  // a REMS Admin, who may see the tenant's requests including other people's drafts. type/status are
  // option-set CODES, matched exactly; clientName/contact are "contains"; createdFrom/To are UTC
  // instants (a date-only picker must convert its own day boundaries — see zonedDayBoundaryUtc).
  // Returns the standard envelope.
  list: (params) => api.get("/api/rems/requests", { params }).then(envelope),
  get: (id) => api.get(`/api/rems/requests/${id}`).then(unwrap),
  // payload: { existingClientReferenceId?, clientName, type, description?,
  //            customerEmail?, customerMobileNumber?, mediaId? }
  // No reviewing admin is named: a request is raised for the admins as a body, and it stays unassigned
  // until one picks it up (pickUp below). There is no `submit` either — a request is always created as a
  // draft, and what moves it on is the initiator sending the intake link (sendForm below).
  create: (payload) => api.post("/api/rems/requests", payload).then(unwrap),
  // payload: any subset of { description, type, clientName, customerEmail,
  //            customerMobileNumber, existingClientReferenceId } — null fields are unchanged.
  // Assignment does NOT travel here. Who reviews a request changes only through pickUp / handBack.
  update: (id, payload) => api.put(`/api/rems/requests/${id}`, payload).then(unwrap),
  // Attach already-uploaded media (POST /api/media first) to an existing request. Media the request
  // already carries is ignored, so a retried save cannot file the same document twice. Returns the detail.
  addFiles: (id, mediaIds) => api.post(`/api/rems/requests/${id}/files`, { mediaIds }).then(unwrap),
  // Takes one attached file off the request (the link row, not the stored blob). Returns the detail.
  removeFile: (id, fileId) => api.delete(`/api/rems/requests/${id}/files/${fileId}`).then(unwrap),

  // ---- The admin ↔ initiator rework loop ----
  // The admin returns a request for engagement-setup rework. Only valid while the request is with the
  // admin (Admin Review / Awaiting Confirmation).
  // payload: { reason, returnTo? } — `reason` is required and is shown to whoever it goes to; `returnTo`
  // is "initiator" (the default) or "cse", and names who OWNS the rework. Both of them can work a returned
  // request either way, and both are notified; the choice decides which of them is being asked.
  sendBack: (id, payload) => api.post(`/api/rems/requests/${id}/send-back`, payload).then(unwrap),
  // The initiator hands the revised setup back. Valid from BOTH rework states — the admin's send-back and
  // a round the approvers declined leave the setup with the initiator the same way.
  returnToAdmin: (id) => api.post(`/api/rems/requests/${id}/return-to-admin`).then(unwrap),
  // Every return, oldest first: [{ id, reason, returnedBy, returnedOnUtc, resolvedOnUtc }].
  sendBacks: (id) => api.get(`/api/rems/requests/${id}/send-backs`).then(unwrap),

  // ---- Delegation (Concur-style) ----
  // Self-service: every call here reads or writes the CALLER's own delegations, so nothing is gated on an
  // admin permission — their identity is the whole boundary.
  // The delegates I have named: [{ id, delegateUserId, delegateName, canPrepare, canSend, startsOn,
  //   endsOn, isActive }]. A dated grant stays listed outside its window with isActive false.
  myDelegates: () => api.get("/api/rems/delegations/mine").then(unwrap),
  // The administrative door onto the same rows: a Tenant Admin arranging cover from a user's own page.
  userDelegates: (userId) => api.get(`/api/admin/users/${userId}/rems-delegates`).then(unwrap),
  userDelegateCandidates: (userId) =>
    api.get(`/api/admin/users/${userId}/rems-delegates/candidates`).then(unwrap),
  saveUserDelegate: (userId, payload) =>
    api.put(`/api/admin/users/${userId}/rems-delegates`, payload).then(unwrap),
  removeUserDelegate: (userId, id) =>
    api.delete(`/api/admin/users/${userId}/rems-delegates/${id}`).then(envelope),
  // Who I may act for TODAY: [{ principalUserId, principalName, canPrepare, canSend }].
  actingFor: () => api.get("/api/rems/delegations/acting-for").then(unwrap),
  // payload: { delegateUserId, canPrepare, canSend, startsOn?, endsOn? } — upserts on the pair.
  saveDelegate: (payload) => api.put("/api/rems/delegations", payload).then(unwrap),
  removeDelegate: (id) => api.delete(`/api/rems/delegations/${id}`).then(envelope),

  // ---- Pick up / hand back (who reviews a request) ----
  // The calling admin claims the request. No body: the caller IS the assignee — there is no assigning
  // somebody else. Needs rems.requests.assign. 409s on a draft, and on one another admin holds. Returns the refreshed detail.
  pickUp: (id) => api.post(`/api/rems/requests/${id}/pick-up`).then(unwrap),
  // The holding admin returns it to the queue, where it reads "Waiting for pickup" again and any admin may
  // take it. The only way a request loses its reviewing admin.
  handBack: (id) => api.post(`/api/rems/requests/${id}/hand-back`).then(unwrap),

  remove: (id) => api.delete(`/api/rems/requests/${id}`).then(envelope),
  // Client picker: [{ id, name, email, phone, suffix }]. Any non-empty term searches; the server caps the
  // result set at 20. The suffix is beside the name, never joined into it — see RemsClientLookupItem.
  // Client picker: [{ id, name, email, phone, suffix, firstName, lastName, corporateName, isOrganisation }].
  // `name` is the name as it READS — "Smith John Jr." for a person, the legal name for an organisation —
  // and the parts beside it are what picking a result fills the form's own boxes from.
  //
  // `entityType` is the REMS.IndustryGroup code the request is being raised under, and it decides which
  // KIND of client is offered: "individual" offers people, anything else offers organisations. Omit it
  // before the entity type has been answered and the picker offers both, which is the honest answer at
  // that point. Any non-empty term searches; the server caps the result set at 20.
  clientLookup: (q, entityType) =>
    api.get("/api/rems/clients/lookup", { params: { q, entityType } }).then(unwrap),
  // Users in the active tenant, by role: [{ id, name, email }].
  // Without `role`: Admin + Super Admin users. With `role`: the holders of that role — how the CSE /
  // Engagement Executive / Billing Manager pickers are scoped (see REMS_SEAT_ROLES). A role nobody holds
  // returns [] rather than falling back to every admin, so the picker can say the role needs somebody in
  // it. This took a user GROUP name until the four seats became roles.
  admins: (role) => api.get("/api/rems/admins", { params: role ? { role } : undefined }).then(unwrap),

  // ---- EMS form build / send (WO-112, WO-116) ----
  // payload: { cseUserId, industryGroup } — both required (AC-REMS-007.7). Returns the build screen.
  saveForm: (remsId, payload) => api.post(`/api/rems/requests/${remsId}/form`, payload).then(unwrap),
  // Pre-send preview: { destinationEmail, formLink } (AC-REMS-008.1).
  previewForm: (remsId) => api.get(`/api/rems/requests/${remsId}/form/preview`).then(unwrap),
  // Sends the form-link email; returns the refreshed (now Sent/locked) build screen.
  // payload: { subject?, body? } — the email as the admin left it in the send dialog. Either omitted
  // keeps the template's own version of that part; the template is still resolved server-side.
  sendForm: (remsId, payload) => api.post(`/api/rems/requests/${remsId}/form/send`, payload || {}).then(unwrap),
  // Reminder to a client who has the link but has not submitted. Same shapes as preview/send — the send
  // dialog drives both — but repeatable, and it changes no state on the form: the invite code, the Sent
  // timestamp and the request status all stay put. Each call adds a `Reminder` row to the email log.
  previewReminder: (remsId) => api.get(`/api/rems/requests/${remsId}/form/reminder/preview`).then(unwrap),
  sendReminder: (remsId, payload) =>
    api.post(`/api/rems/requests/${remsId}/form/reminder`, payload || {}).then(unwrap),
  // The request's email history and whether THIS caller may chase the client from it:
  //   { canRemind, remindBlockedReason, events: [{ id, eventType, recipientEmail, occurredOnUtc,
  //     detail, sentBy, subject, body }] , clientFormLink } — newest first.
  // `subject`/`body` are the message AS SENT and are present only on the rows this portal raised (Sent,
  // Reminder) — a provider callback reports on a message rather than being one. The transport message id
  // is deliberately not returned: it is how callbacks find the row, not something a reader can act on.
  // `clientFormLink` is the client's intake link, non-null only while the form is sent and unanswered.
  // eventType is a RemsFormEmailEventType name (Sent | Reminder | Delivered | Opened | Failed);
  // `sentBy` names the person who pressed Send/Remind and is null on provider-reported events; `detail`
  // explains a Failed event this portal recorded itself. `canRemind` is the reminder endpoint's own
  // decision asked ahead of the click, and `remindBlockedReason` is set only where the answer is worth
  // showing — the request's state, or its being with somebody else.
  emailLog: (remsId) => api.get(`/api/rems/requests/${remsId}/email-log`).then(unwrap),

  // ---- EMS Review + submitted-form review (WO-114, WO-116) ----
  // The admins' shared queue (paginated envelope), NOT one admin's own list — every request whose
  // initiator has sent it to their client, whoever holds it. Drafts never appear: they are still their
  // author's private working copy.
  // params: { page?, limit?, search?, submitted?, requestStatus?, assignment? } — search covers the REMS
  // number and client name; `submitted` is a bool; `assignment` is the quick filter, "mine" for the
  // requests this caller has picked up and anything else (the default) for the whole queue. Filtering is
  // server-side. Rows:
  //   { remsId, remsNumber, clientName, requestStatus, hasForm,
  //     submitted, submittedOnUtc, assignedAdmin:{id,name}|null, cse:{id,name}|null, canPickUp }.
  // A null `assignedAdmin` is a request waiting for pickup; `canPickUp` is whether THIS caller may take it.
  clientForms: (params) => api.get("/api/rems/client-forms", { params }).then(envelope),
  // The submitted-form snapshot: { submissionId, remsId, remsNumber, industryGroup, lockedEmail,
  //   clientNameSuffix, submittedOnUtc, payload, editedBy, editedOnUtc, canEdit } — payload is the
  //   RemsFormPayloadV1 wire shape, rendered read-only and grouped. `clientNameSuffix` is the REQUEST's
  //   generational suffix, which the payload does not carry (the intake form never asks for one), so a
  //   screen showing the client's own name can read it as the rest of REMS does.
  //   `editedBy`/`editedOnUtc` are null while the answers are still exactly as the client sent them;
  //   `canEdit` is whether THIS caller may correct them.
  submission: (remsId) => api.get(`/api/rems/requests/${remsId}/submission`).then(unwrap),
  // Correct the client's answers in place — Admin only (rems.engagements.manage), and refused once the
  // engagement is pending approval or approved. Send the WHOLE payload: it replaces the stored one, and
  // it is validated exactly as the client's own submit was. Returns the refreshed submission view.
  // It does NOT re-materialise the client record, its entities or its contact Persons — those were
  // written by the submit and are edited through their own endpoints.
  updateSubmission: (remsId, payload) =>
    api.put(`/api/rems/requests/${remsId}/submission`, payload).then(unwrap),

  // ---- Engagement workspace (WO-117 / WO-114) ----
  // Addresses travel in the REMS wire shape — { street, addressLine2, city, state, stateCode, zip,
  // countryCode, countryName } (street is address line 1) — mapped to/from the AppAddressFields model by
  // modules/rems/remsAddress. Send the WHOLE shape: an update writes every line it is given.
  //
  // The editable engagement workspace graph:
  //   { remsId, remsNumber, requestStatus, client, entities[], engagement, industryGroup,
  //     additionalEntities[], departmentDirectors[] }.
  // client: { id, name, email(locked), mobileNumber, referralSource, billingContactName, billingEmail } —
  // NULL until the client submits their intake form. The engagement is there from the moment the request
  // is created, because the initiator fills its setup before the client is ever contacted, so a workspace
  // with no client is the ordinary state of an unanswered request rather than an error.
  // entities[]: { id, name, ein, isMainEntity, addresses:[{ id, addressType, address }] — addressType is
  //   Physical | Mailing | Billing — contacts:[{ id, role, isRequired, name, email, phone }] }.
  // engagement is the request's ONE engagement:
  //   { id, department, subServiceLine, subIndustry, departmentDirector, engagementExecutive,
  //     billingManager, firstYearFeeEstimate, realizationPercentage, billingPeriod,
  //     billingProcessDescription, status, marketingMethodIds[], commissionSplits[], audit,
  //     government, tax } | null.
  // What the setup form calls Service Line is `subServiceLine`, and what it calls Industry is
  // `subIndustry` (see the note at the top of modules/rems/useRemsMeta).
  engagement: (remsId) => api.get(`/api/rems/requests/${remsId}/engagement`).then(unwrap),
  // Correcting the client's own answers — the client record (`updateClient`), the main entity's three
  // addresses (`updateEntityAddresses`) and its role contacts (`updateEntityContacts`) — has no screen any
  // more: what the client submitted is read from the immutable snapshot rather than edited. The three
  // endpoints stand; add the wrapper back with whatever screen wants them.
  // payload: any subset of { department, subServiceLine, subIndustry, departmentDirectorId,
  //   engagementExecutiveId, billingManagerId, firstYearFeeEstimate, engagementFee, realizationPercentage,
  //   billingPeriod, billingProcessDescription } — null fields are left unchanged. Service Line
  //   (`subServiceLine`), Industry (`subIndustry`) and the billing description are optional and therefore
  //   CLEARED with an empty string, not with null. `engagementFee` is Assurance's own fee and a column of
  //   its own — it is not a relabelled `firstYearFeeEstimate`.
  // Returns { engagement, mappedDepartmentDirectorId } — the director the chosen department maps to (hint).
  updateEngagement: (id, payload) => api.put(`/api/rems/engagements/${id}`, payload).then(unwrap),
  // Link a previously-uploaded media id as the signed client-acceptance form (Audit and Assurance).
  uploadCaf: (id, mediaId) => api.post(`/api/rems/engagements/${id}/audit/client-acceptance-form`, { mediaId }).then(unwrap),
  // Take the signed client-acceptance form off the engagement. The link goes; the stored file itself is
  // left where it is. Idempotent — an engagement carrying none answers 200, not 404.
  removeCaf: (id) => api.delete(`/api/rems/engagements/${id}/audit/client-acceptance-form`).then(unwrap),
  // payload: { clientFiscalYearEnd?, adminFeesApply?, adminFeesAmount? } — the Assurance half of the attest
  // detail the CAF above shares. Answering adminFeesApply=false clears the amount server-side.
  updateAuditDetail: (id, payload) => api.put(`/api/rems/engagements/${id}/audit`, payload).then(unwrap),
  // payload: { contractNumber?, floridaOnePercentStateFeeApplies?, contractStartDate?, contractEndDate?,
  //   originalTerm?, renewalTerms?, purchaseOrderStartDate?, purchaseOrderEndDate?,
  //   purchaseOrderNumber?, purchaseOrderAmount?, personnelLevel?, billRatePerHour? }.
  // ONE row holds the government audit's contract block and the GCS purchase order, and every field is
  // written from the payload — so send the whole row back, not just the half your card shows.
  updateGovernment: (id, payload) => api.put(`/api/rems/engagements/${id}/government`, payload).then(unwrap),
  // Link a previously-uploaded media id as the GCS engagement's purchase-order document.
  uploadPurchaseOrder: (id, mediaId) =>
    api.post(`/api/rems/engagements/${id}/government/purchase-order`, { mediaId }).then(unwrap),
  // Take the purchase-order document off the engagement. The link goes; the stored file itself is left
  // where it is. Idempotent — an engagement carrying none answers 200, not 404.
  removePurchaseOrder: (id) =>
    api.delete(`/api/rems/engagements/${id}/government/purchase-order`).then(unwrap),
  // payload: { fiscalYearEnd?, originalDueDate?, firstExtensionDueDate?, taxFormIds:[] }. A due date left
  // null is DERIVED from the fiscal year end rather than cleared — the rule is the default, not the only
  // answer — so the response carries the effective pair either way.
  updateTax: (id, payload) => api.put(`/api/rems/engagements/${id}/tax`, payload).then(unwrap),
  // Every approval round on an engagement, oldest first:
  //   [{ roundId, roundNumber, status, sentOnUtc, sentBy, completedOnUtc, declineThreshold, declineCount,
  //      decisions:[{ taskId, approver, role, status, decidedOnUtc, reason, checklistCompleted,
  //        checklistTotal }] }].
  // status Superseded on a decision means the round closed before that approver decided.
  approvalHistory: (engagementId) => api.get(`/api/rems/engagements/${engagementId}/approval/history`).then(unwrap),
  // Set the marketing tags (≥1 required to save; saving makes approval reachable). Returns the engagement.
  updateMarketing: (id, marketingMethodIds) => api.put(`/api/rems/engagements/${id}/marketing`, { marketingMethodIds }).then(unwrap),
  // Set the commission splits (≤10 recipients, each > 0 and ≤ 100). splits: [{ employeeId, percentage }].
  updateCommission: (id, splits) => api.put(`/api/rems/engagements/${id}/commission`, { splits }).then(unwrap),
  // The full approver list: { engagementId, engagementStatus, approvers:[{ user:{id,name}, role }],
  // selectedApproverIds }. approvers = the automatic ones (the firm's shareholders, the department
  // director, the CSE and the commission recipients), ordered for reading, plus the added ones;
  // selectedApproverIds = only the added ones, which is what the picker binds to — so the automatic ones
  // cannot be unpicked.
  approvers: (id) => api.get(`/api/rems/engagements/${id}/approvers`).then(unwrap),
  // Users selectable as EXTRA approvers — every active user in the tenant, with the roles they hold there
  // for the picker label → [{ userId, name, email, roles: [] }].
  approverOptions: (id) => api.get(`/api/rems/engagements/${id}/approver-options`).then(unwrap),
  // Replace the ADDED approvers. An empty array removes the additions; the automatic ones always route.
  setApprovers: (id, userIds) => api.put(`/api/rems/engagements/${id}/approvers`, { userIds }).then(unwrap),
  // Route the engagement for approval; returns the (now locked) approver list with engagementStatus updated.
  sendApproval: (id) => api.post(`/api/rems/engagements/${id}/approval/send`).then(unwrap),
  // Resubmit a rejected engagement for a fresh approval round (staff, rems.approvals.send). Returns the
  // regenerated (locked) approver list with engagementStatus now PendingApproval again.
  resubmitApproval: (engagementId) => api.post(`/api/rems/engagements/${engagementId}/approval/resubmit`).then(unwrap),

  // ---- Related Entities ----
  // Every submitted request whose client declared somebody ALONGSIDE themselves — the other people on an
  // individual's return ("Spouse & More Individuals") and the other businesses every other entity type
  // names ("Other Entities") — one row per REQUEST with its related clients nested.
  //
  // Open to every signed-in user and NOT narrowed to the caller's own requests, unlike every other REMS
  // list: it is a shared tracking board.
  //
  // params: { page?, limit?, search?, entityType?, relatedStatus?, sortBy?, descending? } — search covers
  // the REMS number, the client's name AND the related clients' names; entityType is a REMS.IndustryGroup
  // code; relatedStatus is a REMS.RelatedEntityStatus code and matches a request holding at least one row
  // at it. Server-side throughout. Returns the standard paginated envelope. Rows:
  //   { remsId, remsNumber, clientName, clientNameSuffix, clientEmail, entityType,
  //     requestStatus, submittedOnUtc, relatedCount,
  //     parent: { name, suffix, jointWith: { name, suffix, relation } | null },
  //     relatedClients: [{ kind, id, name, suffix, relation, email, phoneNumber, status, reference,
  //                        createdRemsId }],
  //     createdBy, createdOnUtc, updatedBy, updatedOnUtc }.
  // `parent.jointWith` is a spouse filing JOINTLY — the same client, so they are named in the header
  // rather than given a row. `reference` is the request a row produced, or a derived "REMS-1042-C1", and
  // is null while the row is still Not Initiated and has produced nothing.
  //
  // Every `name` here — the parent's, the joint filer's, each related client's — reads SURNAME FIRST for a
  // person ("Smith Jane") with the particle beside it in `suffix`, which is the one order a client is
  // named in anywhere on the platform. A business is its plain declared name and carries no suffix.
  relatedEntities: (params) => api.get("/api/rems/related-entities", { params }).then(envelope),
  // Move one related client along — the ONLY write on that list, and the only thing that changes a status:
  // nothing in the workflow advances it. `kind` is the row's own ("individual" | "entity"), `status` a
  // REMS.RelatedEntityStatus code. Returns the refreshed row (its reference appears with the status).
  setRelatedEntityStatus: (kind, id, status) =>
    api.put(`/api/rems/related-entities/${kind}/${id}/status`, { status }).then(unwrap),

  // ---- Approval inbox (WO-117 Part B / WO-114) — the caller's OWN approval tasks only ----
  // The caller's own approval tasks (pending + historical), newest round first. Paged and filtered
  // SERVER-side — params: { page?, limit?, search?, role?, status? }, where search covers the REMS number,
  // client and entity name, and role/status take the RemsApproverRole / RemsApprovalTaskStatus names.
  // Returns the standard paginated envelope. Rows:
  //   { taskId, roundId, roundNumber, role, status, sentOnUtc, decidedOnUtc, roundStatus,
  //     engagementId, remsId, remsNumber, clientName, entityName,
  //     approvedCount, rejectedCount, approverCount }.
  // The three counts describe the whole ROUND (how many approvers, how many have decided), not the
  // caller's own task — they drive the inbox's "1/4" progress column. Still awaiting =
  // approverCount - approvedCount - rejectedCount; a rejection ends the round, so the rest never decide.
  myApprovalTasks: (params) => api.get("/api/rems/approval-tasks", { params }).then(envelope),
  // The caller's own approval task with the full review packet (404 for anyone else's task) — the same
  // material as the staff engagement workspace, since that is what is being signed off. Shape:
  //   { taskId, roundId, roundNumber, role, status, decidedOnUtc, rejectionReason, canDecide,
  //     checklist:[{ id, displayOrder, label, isCompleted, completedOnUtc }],
  //     request:{ remsId, remsNumber, title, description, clientName, type, status,
  //       customerEmail, customerMobileNumber, industryGroup, emsFormState, clientSubmissionState,
  //       assignedAdmin, cse, requestedBy, createdOnUtc, files:[{ id, mediaId, fileName, mimeType, fileSize, url }] },
  //     engagement:{ engagementId, status, department, subServiceLine, subIndustry,
  //       client:{ id, name, email, mobileNumber, referralSource, billingContactName, billingEmail,
  //         entities:[{ id, name, ein, isMainEntity }] },
  //       entity:{ id, name, ein, isMainEntity, addresses:[{ id, addressType, address }],
  //         contacts:[{ id, role, isRequired, name, email, phone }] },
  //       departmentDirector, engagementExecutive, billingManager,
  //       firstYearFeeEstimate, realizationPercentage, financialsRestricted,
  //       audit:{ id, clientAcceptanceFormMediaId, fileName, url }|null,
  //       government:{ ... }|null,
  //       tax:{ id, fiscalYearEnd, dueDates:{ originalDueDate, extendedDueDate }|null, taxForms:[{ id, label, group }] }|null,
  //       marketingMethods:[{ id, label, group }], commissionSplits:[{ id, employee:{ id, name }, percentage }] },
  //     round:{ id, roundNumber, status, sentOnUtc, sentBy, completedOnUtc, rejectionReason,
  //       decisions:[{ taskId, approver, role, status, decidedOnUtc, rejectionReason, isYou }] } }.
  // Option-set references arrive resolved to labels — the approver roles do not carry optionSets.read.
  // `firstYearFeeEstimate`/`realizationPercentage` are null with `financialsRestricted` true for roles
  // other than Department Director (AC-REMS-019.10).
  approvalTask: (taskId) => api.get(`/api/rems/approval-tasks/${taskId}`).then(unwrap),
  // The caller's OWN approval task on a request — `{ taskId }`, or a 404 when they hold none. REMS
  // notifications carry the REQUEST id (the one id every recipient shares), so an approver following one
  // needs the task resolved before it can send them to the right screen. The 404 is the ordinary answer
  // for every non-approver recipient and the caller's cue to fall back to the request.
  myApprovalTaskForRequest: (remsId) =>
    api.get(`/api/rems/approval-tasks/for-request/${remsId}`).then(unwrap),
  // Check / uncheck one checklist item on the caller's own task. Returns the updated item view.
  setChecklistItem: (taskId, itemId, isCompleted) =>
    api.put(`/api/rems/approval-tasks/${taskId}/checklist/${itemId}`, { isCompleted }).then(unwrap),
  // Approve the caller's own task. 409 (REMS_CHECKLIST_INCOMPLETE) when the server re-verify finds an
  // incomplete checklist. Returns the (now read-only) task view.
  approveTask: (taskId) => api.post(`/api/rems/approval-tasks/${taskId}/approve`).then(unwrap),
  // Reject the caller's own task; payload { reason } is required. Returns the (now read-only) task view.
  rejectTask: (taskId, payload) => api.post(`/api/rems/approval-tasks/${taskId}/reject`, payload).then(unwrap)
};

// REMS public client EMS form (Phase 15, WO-113/116). The anonymous, no-login client onboarding flow
// reached via the emailed invite link ({App:BaseUrl}/rems/form/{inviteCode}). These call the
// AllowAnonymous `api/rems/public/forms` endpoints on the UNAUTHENTICATED `anonApi` instance (no Bearer
// token / tenant headers) — the form is resolved by its unguessable invite code alone. All bodies are the
// RemsFormPayloadV1 wire shape (mirrors EmsPortal.Api.Models.Rems.RemsPublicFormModels); the backend
// re-validates every payload at review + submit, so partial drafts round-trip freely.
export const remsPublicApi = {
  // → { state: "Invalid"|"Unavailable"|"Submitted"|"Editable", clientName?, industryGroup?,
  //     prefill?:{ clientName, email, mobileNumber }, draftPayload?:RemsFormPayloadV1 }. Always HTTP 200.
  load: (inviteCode) => anonApi.get(`/api/rems/public/forms/${encodeURIComponent(inviteCode)}`).then(unwrap),
  // Auto-save the single in-progress draft (partial payload allowed) → { lastSavedOnUtc }.
  saveDraft: (inviteCode, payload) =>
    anonApi.put(`/api/rems/public/forms/${encodeURIComponent(inviteCode)}/draft`, payload).then(unwrap),
  // Validate + return the grouped read-only review model, or a 400 VALIDATION_FAILED envelope (per-field
  // messages in error.details) when the form is incomplete.
  review: (inviteCode, payload) =>
    anonApi.post(`/api/rems/public/forms/${encodeURIComponent(inviteCode)}/review`, payload).then(unwrap),
  // Transactionally submit (re-validates server-side); idempotent → returns the Submitted thank-you state.
  submit: (inviteCode, payload) =>
    anonApi.post(`/api/rems/public/forms/${encodeURIComponent(inviteCode)}/submit`, payload).then(unwrap),
  // Non-destructive client cancellation acknowledgement (the draft is kept; the link stays usable).
  cancel: (inviteCode) => anonApi.post(`/api/rems/public/forms/${encodeURIComponent(inviteCode)}/cancel`).then(unwrap)
};
