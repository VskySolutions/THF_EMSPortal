import { ref, computed } from "vue";
import { useAuthStore } from "stores/auth";
import { Permissions } from "composables/usePermissions";
import { optionSetApi } from "services/api";
import { useRemsOptionCatalog, ensureRemsOptionsLoaded } from "modules/rems/useRemsOptionCatalog";
import { REMS_STATUS } from "modules/rems/remsStatus";

// EVERY REMS VALUE IS AN OPTION SET. There is no label, colour, icon or description for any of them in this
// file — all four come from the tenant's own list.
export const REMS_TYPE_BRAND_NEW_CLIENT = "brand_new_client";
export const REMS_TYPE_EXISTING_CLIENT = "existing_client";

// The three seats an engagement names, as ROLE names — the value each people-picker scopes itself by
// (remsApi.admins(role)).
export const REMS_SEAT_ROLES = Object.freeze({
  CSE: "CSE",
  ENGAGEMENT_EXECUTIVE: "Engagement Executive",
  BILLING_MANAGER: "Billing Manager"
});

// Type codes that mean "an existing client is referenced" (drives the client-lookup type marking).
export const REMS_EXISTING_CLIENT_TYPES = [REMS_TYPE_EXISTING_CLIENT];

// The industry groups that ask the BUSINESS questions — EIN, and the Primary / Financial / Billing /
// Other contacts.
export const REMS_BUSINESS_ENTITY_TYPES = Object.freeze([
  "not_for_profit", "insurance", "commercial", "trust_estate", "business"
]);

export const isBusinessEntityType = (group) => REMS_BUSINESS_ENTITY_TYPES.includes(group);

// The Entity Type code (REMS.EntityType value) for a person rather than an organisation.
export const REMS_ENTITY_TYPE_INDIVIDUAL = "individual";

/** Whether a request's entity type is the individual one — see REMS_ENTITY_TYPE_INDIVIDUAL. */
export const isIndividualEntityType = (entityType) => entityType === REMS_ENTITY_TYPE_INDIVIDUAL;

// A trust or estate. In the business family, but the only one asked for a contact of its own.
// Mirrors RemsFormPayloadValidator.TrustEstate.
export const REMS_ENTITY_TYPE_TRUST_ESTATE = "trust_estate";

// ---- Dependent dropdowns ----
// Which values a parent offers is data now: each option carries the ids of the parent values it is valid
// under (Administration → Option Sets, "Valid under"). Industry follows Entity Type, Service Line follows
// Department and Job Type follows Service Line.
const parentIdOf = (parentOptions, parent) => parentOptions.find((o) => o.value === parent)?.id;

/**
 * The options a parent value offers: the ones linked to it, any linked to no parent at all, and whatever
 * is currently stored — always kept, so a record never loses its answer. Nothing but the stored value is
 * offered until the parent is answered.
 */
export function remsDependentOptions (options, parentOptions, parent, selected = null) {
  if (!parent) return options.filter((o) => o.value === selected);
  const parentId = parentIdOf(parentOptions, parent);
  return options.filter((o) =>
    !o.parentIds?.length || o.parentIds.includes(parentId) || o.value === selected);
}

/** Whether a stored value is one its parent offers — what decides if a changed parent clears it. */
export function remsFitsParent (options, parentOptions, parent, value) {
  if (!value || !parent) return true;
  const parentIds = options.find((o) => o.value === value)?.parentIds;
  return !parentIds?.length || parentIds.includes(parentIdOf(parentOptions, parent));
}

// Not offered on Engagement Setup (user, 13 Nov 2026). Still on the option lists, and a request already
// filed under either keeps showing it.
export const REMS_HIDDEN_DEPARTMENTS = Object.freeze(["admin"]);
export const REMS_HIDDEN_SERVICE_LINES = Object.freeze(["non_chargeable_internal"]);
export const remsWithoutHidden = (options, hidden, selected = null) =>
  options.filter((o) => !hidden.includes(o.value) || o.value === selected);

// ---------------------------------------------------------------------------------------------------
// THERE ARE NO LABEL, COLOUR OR DESCRIPTION MAPS IN THIS FILE. Every word, colour and icon a REMS value is
// rendered with comes from its OPTION SET — the tenant's own copy.

// A code that resolves to nothing. Renders the code itself rather than a blank badge — an unrecognised
// value is worth seeing, and grey with no tooltip says "this is not one of the values on the list".
const unknownOption = (value) => ({
  value,
  label: value || "—",
  description: "",
  backgroundColor: "",
  textColor: "",
  icon: ""
});

/** The full option for a code — label, description, colours and icon — from a resolved list. */
const optionFrom = (options, value) =>
  (value ? options.find((o) => o.value === value) : null) || unknownOption(value);

const labelFrom = (options, value) => optionFrom(options, value).label;

// The option item's own Description, or "" when it has none — the caller's cue to render no tooltip.
// Unlike labelFrom there is no falling back to the raw value: a code is not an explanation.
const hintFrom = (options, value) => (value ? optionFrom(options, value).description : "");

// The REMS.Status value that is NOT a stored status. `customer_submitted` covers both "an admin has this"
// and "nobody has picked it up yet".
export const REMS_STATUS_WAITING_FOR_PICKUP = "waiting_for_pickup";

// The one value on REMS.ApprovalRoundStatus the enum does not have.
export const REMS_ROUND_PARTIALLY_APPROVED = "partially_approved";

// Once a request has left its initiator it is with "the admins", which on its own says nothing about the
// one thing anyone wants to know at that stage.
const AWAITING_ADMIN_STATUSES = [REMS_STATUS.ADMIN_REVIEW, REMS_STATUS.AWAITING_ADMIN_CONFIRMATION];
const awaitingPickUp = (row) => AWAITING_ADMIN_STATUSES.includes(row?.status) && !row?.assignedAdmin;

// Label/colour helpers for rendering REMS rows and detail cards.
export function useRemsMeta () {
  const options = useRemsOptionCatalog();
  const auth = useAuthStore();

  const typeLabel = (v) => labelFrom(options.type, v);
  const statusLabel = (v) => labelFrom(options.status, v);
  const referralSourceLabel = (v) => labelFrom(options.referralSource, v);
  // The stage's own Description, from Administration → Option Sets — what every status badge in REMS
  // now carries as a tooltip.
  const statusHint = (v) => hintFrom(options.status, v);

  // Tooltips come from the option ITEM's Description, maintained in Administration → Option Sets, so a
  // tenant who rewords a value rewords its explanation in the same place.
  const typeHint = (v) => hintFrom(options.type, v);
  const referralSourceHint = (v) => hintFrom(options.referralSource, v);
  const entityTypeLabel = (v) => labelFrom(options.entityType, v);
  const departmentLabel = (v) => labelFrom(options.department, v);
  const serviceLineLabel = (v) => labelFrom(options.serviceLine, v);
  const jobTypeLabel = (v) => labelFrom(options.jobType, v);
  const industryLabel = (v) => labelFrom(options.industry, v);
  // GCS staffing level. Resolved here because the approver's packet carries the CODE — that screen's
  // option labels are resolved server-side only for the sets keyed by item id (marketing).
  const personnelLevelLabel = (v) => labelFrom(options.personnelLevel, v);
  // How often a CAS engagement is billed, resolved for the same reason.
  const billingPeriodLabel = (v) => labelFrom(options.billingPeriod, v);

  // ---- The badges ----
  // Each returns the whole OPTION — label, description, colours, icon — which is what AppOptionBadge
  // renders.
  const formStatusOption = (v) => optionFrom(options.formStatus, v);
  const submissionStateOption = (v) => optionFrom(options.clientSubmissionState, v);
  const approverRoleOption = (v) => optionFrom(options.approverRole, v);
  const approvalStatusOption = (v) => optionFrom(options.approvalStatus, v);
  const engagementStatusOption = (v) => optionFrom(options.engagementStatus, v);
  const emailEventOption = (v) => optionFrom(options.emailEvent, v);
  // How far one related client has got. Rendered as a badge AND offered as the choices behind it — the
  // status moves only by hand, so the same option is what the row shows and what the dropdown sets.
  const relatedEntityStatusOption = (v) => optionFrom(options.relatedEntityStatus, v);
  // What kind of entity the client is.
  const entityTypeOption = (v) => optionFrom(options.entityType, v);

  // The label-only forms, for a table column's `field` (which sorts and searches on a string) and for the
  // few places a value is read as plain text rather than as a badge.
  const emsStateLabel = (v) => labelFrom(options.formStatus, v);
  const submissionStateLabel = (v) => (v ? labelFrom(options.clientSubmissionState, v) : "—");
  const approverRoleLabel = (v) => labelFrom(options.approverRole, v);
  const approvalStatusLabel = (v) => labelFrom(options.approvalStatus, v);

  /** Where a whole approval ROUND stands, refined by how many of its approvers have signed. */
  const roundStatusOption = (status, approved = 0, total = 0) => {
    const partial = status === "Pending" && total > 0 && approved > 0;
    const option = optionFrom(options.approvalRoundStatus, partial ? REMS_ROUND_PARTIALLY_APPROVED : status);
    if (!total) return option;
    const tally = `${approved} of ${total} approvers have signed.`;
    return { ...option, description: [tally, option.description].filter(Boolean).join(" ") };
  };

  // Status badge for a request ROW (or detail) rather than a bare code: the status, except that a request
  // sitting with the admins says whether one has actually taken it.
  const requestStatusOption = (row) =>
    optionFrom(options.status, awaitingPickUp(row) ? REMS_STATUS_WAITING_FOR_PICKUP : row?.status);
  const requestStatusLabel = (row) => requestStatusOption(row).label;

  // The COP engagement/detail action becomes available only once the customer has submitted their
  // form (AC-REMS-002.5 / 005.6); until then it stays disabled.
  const emsDetailAvailable = (row) => row?.clientSubmissionState === "Submitted";

  // Why engagement setup is closed to this user on this row, or null when it is theirs to work.
  const isElevated = () => auth.roles.includes("SuperAdmin") || auth.roles.includes("TenantAdmin");
  const engagementOwnerDenial = (row) => {
    if (isElevated()) return null;
    const assignee = row?.assignedAdmin?.id;
    if (!assignee) return "Waiting for pickup — pick this request up to work its engagement setup";
    if (assignee !== auth.user?.userId) {
      return `Picked up by ${row.assignedAdmin?.name || "another Admin"} — only they can work its engagement setup`;
    }
    return null;
  };
  // The row action that opens a request as a FORM, named for who presses it: whoever runs the review
  // (rems.engagements.manage) manages a request — picks it up, works it, sends it back, routes it — and
  // everyone else edits their own. View, beside it, reads the whole request and is everybody's.
  const workAction = computed(() => (auth.hasPermission(Permissions.RemsEngagementsManage)
    ? { label: "Manage", icon: "o_edit_note" }
    : { label: "Edit", icon: "o_edit" }));
  // The email-log / COP-inbox action is meaningful once a form has been sent to the customer — never on a
  // request that reused the client's intake on file, which emailed nothing.
  const emsFormActivity = (row) =>
    !row?.intakeReused && (!!row?.clientSubmissionState || ["Sent", "Submitted"].includes(row?.emsFormState));

  // Live option lists for pickers and column filters.
  const typeOptions = computed(() => options.type);
  const referralSourceOptions = computed(() => options.referralSource);
  const statusOptions = computed(() => options.status);
  const entityTypeOptions = computed(() => options.entityType);
  const departmentOptions = computed(() => options.department);
  const serviceLineOptions = computed(() => options.serviceLine);
  const jobTypeOptions = computed(() => options.jobType);
  const industryOptions = computed(() => options.industry);
  // "Waiting For Pickup" is a filter value of its own now. It is not a stored status, but the server reads
  // it the way the column does — with the admins, and nobody holding it — and reads the two admin stages
  // as the rows an admin actually holds, so the filter and the badge agree.
  const statusFilterOptions = computed(() => options.status);

  // The approval-decision filter on the Approvals inbox, from the same list its badges are rendered from.
  const approvalStatusFilterOptions = computed(() => options.approvalStatus);
  // The round's standing, from the list the Approval Status badge is rendered from — "Partially Approved"
  // included, which the server answers as a pending round some approvers have signed.
  const roundStatusFilterOptions = computed(() => options.approvalRoundStatus);

  // The Related Entities list's status column: the same list drives the dropdown on every row and the
  // filter in the drawer, so a firm that adds a fifth position can both set it and filter by it.
  const relatedEntityStatusOptions = computed(() => options.relatedEntityStatus);

  // The COP State column's values: the form-status list minus Draft and Saved, which the list reports as
  // Not started (RemsWorkspaceMapper.FormState) — offering them would match nothing.
  const formStateFilterOptions = computed(() =>
    options.formStatus.filter((o) => !["Draft", "Saved"].includes(o.value)));

  // The Client Submission column's values — where the client's answers stand — for its filter.
  const submissionStateFilterOptions = computed(() => options.clientSubmissionState);

  return {
    typeLabel,
    typeHint,
    referralSourceLabel,
    referralSourceHint,
    statusLabel,
    statusHint,
    departmentLabel,
    serviceLineLabel,
    jobTypeLabel,
    industryLabel,
    personnelLevelLabel,
    billingPeriodLabel,
    entityTypeLabel,
    typeOptions,
    referralSourceOptions,
    statusOptions,
    entityTypeOptions,
    departmentOptions,
    serviceLineOptions,
    jobTypeOptions,
    industryOptions,
    statusFilterOptions,
    approvalStatusFilterOptions,
    relatedEntityStatusOptions,
    formStateFilterOptions,
    submissionStateFilterOptions,
    roundStatusFilterOptions,
    // The badges: each hands back the whole option, for AppOptionBadge.
    requestStatusOption,
    formStatusOption,
    submissionStateOption,
    approverRoleOption,
    approvalStatusOption,
    roundStatusOption,
    engagementStatusOption,
    emailEventOption,
    relatedEntityStatusOption,
    entityTypeOption,
    // …and the label-only forms, for a column's sort key or a line of plain text.
    requestStatusLabel,
    emsStateLabel,
    submissionStateLabel,
    approverRoleLabel,
    approvalStatusLabel,
    engagementOwnerDenial,
    workAction,
    emsDetailAvailable,
    emsFormActivity
  };
}

// The Type picker.
export function useRemsOptionSets () {
  const catalog = useRemsOptionCatalog();
  return {
    typeOptions: computed(() => catalog.type),
    load: ensureRemsOptionsLoaded
  };
}

// ---- Engagement workspace (WO-117) option sets + conditional logic ----

// Department + Entity Type are stored as string codes, so — like Type — the closed seed lists are a safe
// fallback when the resolve endpoint 403s (the REMS Admin role lacks optionSets.read).
export const REMS_DEPARTMENT_CODES = Object.freeze({
  CAS: "cas", TAX: "tax", AUDIT: "audit", GCS: "gcs", ASSURANCE: "assurance", ADMIN: "admin"
});

// The Entity Type code (REMS.EntityType value) that makes an audit a GOVERNMENT audit.
export const REMS_ENTITY_TYPE_GOVERNMENT = "government";

// The REMS.Marketing groups (from each item's MetadataJson `group` tag), in display order.
const MARKETING_GROUPS = [
  { key: "Global", label: "Global" },
  { key: "Geography", label: "Geography" },
  { key: "Service/Education", label: "Service / Education" },
  { key: "Event", label: "Event" }
];

// Conditional engagement-detail predicates — mirror the backend RemsEngagementCodes helper exactly.
export const isAuditDepartment = (department) => department === REMS_DEPARTMENT_CODES.AUDIT;
export const isTaxDepartment = (department) => department === REMS_DEPARTMENT_CODES.TAX;
// An audit engagement for a government ENTITY — `entityType` is the request's entityType code, which
// lives on the form record rather than on the engagement, so callers pass it in.
export const isGovernmentAudit = (department, entityType) =>
  isAuditDepartment(department) && entityType === REMS_ENTITY_TYPE_GOVERNMENT;

// Client Accounting Services.
export const isCasDepartment = (department) => department === REMS_DEPARTMENT_CODES.CAS;

// Attest work priced for the engagement rather than for its first year.
export const isAssuranceDepartment = (department) => department === REMS_DEPARTMENT_CODES.ASSURANCE;

// Government Consulting Services: set up against a purchase order rather than a fee.
export const isGcsDepartment = (department) => department === REMS_DEPARTMENT_CODES.GCS;

// The departments asked for a signed client-acceptance form. Mirrors
// RemsEngagementCodes.RequiresClientAcceptanceForm, which is what actually gates the approval.
export const requiresClientAcceptanceForm = (department) =>
  isAuditDepartment(department) || isAssuranceDepartment(department);

// Loads the engagement's code-valued option sets (Department, Service Line and Industry) plus Marketing
// for the workspace.
export function useRemsEngagementOptionSets () {
  // The code-valued lists come from the shared catalogue (the same ones the labels read); Marketing
  // is resolved here because it is keyed by OptionSetItem *id* rather than by code.
  const catalog = useRemsOptionCatalog();
  const departmentOptions = computed(() => catalog.department);
  const marketingGroups = ref([]);
  const marketingUnavailable = ref(false);

  const groupOf = (metadataJson) => {
    try {
      return JSON.parse(metadataJson || "{}").group || "Other";
    } catch {
      return "Other";
    }
  };

  const resolveMarketing = async () => {
    try {
      const items = await optionSetApi.resolve({
        key: "REMSMarketing_MarketingMethods.MarketingMethodId"
      });
      const byGroup = new Map();
      (items || []).forEach((i) => {
        const g = groupOf(i.metadataJson);
        if (!byGroup.has(g)) byGroup.set(g, []);
        byGroup.get(g).push({ value: i.id, label: i.label });
      });
      const known = MARKETING_GROUPS
        .map((g) => ({ key: g.key, label: g.label, items: byGroup.get(g.key) || [] }))
        .filter((g) => g.items.length);
      const extra = [...byGroup.keys()]
        .filter((k) => !MARKETING_GROUPS.some((g) => g.key === k))
        .map((k) => ({ key: k, label: k, items: byGroup.get(k) }));
      marketingGroups.value = [...known, ...extra];
      marketingUnavailable.value = marketingGroups.value.length === 0;
    } catch {
      marketingGroups.value = [];
      marketingUnavailable.value = true;
    }
  };

  const load = () => Promise.all([
    ensureRemsOptionsLoaded(),
    resolveMarketing()
  ]);

  return {
    departmentOptions,
    // Service Line and Industry — still keyed serviceLine / industry in the data, per the note at
    // the top of this file. Code-valued, so they come from the shared catalogue too.
    serviceLineOptions: computed(() => catalog.serviceLine),
    jobTypeOptions: computed(() => catalog.jobType),
    industryOptions: computed(() => catalog.industry),
    // How often the client is billed (REMS.BillingPeriod). Code-valued like Department and Service Line,
    // so it comes from the shared catalogue rather than being resolved by id.
    billingPeriodOptions: computed(() => catalog.billingPeriod),
    // How a GCS engagement is staffed (REMS.PersonnelLevel) — code-valued, so likewise from the catalogue.
    personnelLevelOptions: computed(() => catalog.personnelLevel),
    marketingGroups,
    marketingUnavailable,
    load
  };
}

// The Build-COP entity-type picker (AC-REMS-007.3), from the shared catalogue.
export function useRemsEntityTypes () {
  const catalog = useRemsOptionCatalog();
  return {
    entityTypeOptions: computed(() => catalog.entityType),
    load: ensureRemsOptionsLoaded
  };
}
