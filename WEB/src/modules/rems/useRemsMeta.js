import { ref, computed } from "vue";
import { useAuthStore } from "stores/auth";
import { optionSetApi, EntityType } from "services/api";
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

// ---- Which industries belong to which entity type ----
// Entity type and trade do not partition cleanly — a hospital is Health Care whether it is Commercial or
// Not-for-Profit — which is why this is a map of OVERLAPPING sets rather than a tree.
export const REMS_INDUSTRIES_BY_ENTITY_TYPE = Object.freeze({
  individual: Object.freeze(["individual"]),
  government: Object.freeze(["state_government", "local_government", "federal_government", "government"]),
  not_for_profit: Object.freeze([
    "trade_associations", "charitable_organizations_foundations", "other_not_for_profit",
    "educational_institutions", "health_care"
  ]),
  insurance: Object.freeze([
    "insurance_health", "insurance_property_casualty", "insurance_life", "insurance_other"
  ]),
  commercial: Object.freeze([
    "affordable_housing", "agribusiness", "auto_dealers", "construction", "entertainment",
    "financial_institutions_banking", "hospitality", "manufacturing", "professional_service_firms",
    "real_estate", "retail", "health_care", "oil_gas_distribution", "wholesale", "technology",
    "educational_institutions", "distribution"
  ])
  // `trust_estate` and `business` are deliberately absent: neither has a stated list, so both are offered every trade.
});

// Every industry code this map places somewhere. Anything outside it is a tenant's own addition.
const CLAIMED_INDUSTRY_CODES = new Set(Object.values(REMS_INDUSTRIES_BY_ENTITY_TYPE).flat());

/** The Industry options to offer for an entity type, out of the tenant's resolved list. `selected` is the
    value currently stored, which is always kept. */
export function remsIndustryOptions (options, entityType, selected = null) {
  // Rule 1 — nothing to offer until the entity type is answered. Rule 4 still holds: a record that
  // somehow carries an industry without an entity type keeps showing the one it has.
  if (!entityType) return options.filter((o) => o.value === selected);
  const allowed = REMS_INDUSTRIES_BY_ENTITY_TYPE[entityType];
  if (!allowed) return options;
  return options.filter((o) =>
    allowed.includes(o.value) || !CLAIMED_INDUSTRY_CODES.has(o.value) || o.value === selected);
}

/** Whether an industry is one this entity type offers — what decides if a changed entity type clears it. */
export const remsIndustryFitsEntityType = (entityType, industry) => {
  if (!industry) return true;
  const allowed = REMS_INDUSTRIES_BY_ENTITY_TYPE[entityType];
  return !allowed || allowed.includes(industry) || !CLAIMED_INDUSTRY_CODES.has(industry);
};

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
  const industryLabel = (v) => labelFrom(options.industry, v);
  // GCS staffing level. Resolved here because the approver's packet carries the CODE — that screen's
  // option labels are resolved server-side only for the sets keyed by item id (marketing, tax forms).
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

  // The EMS engagement/detail action becomes available only once the customer has submitted their
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
  // The email-log / EMS-inbox action is meaningful once a form has been sent to the customer.
  const emsFormActivity = (row) =>
    !!row?.clientSubmissionState || ["Sent", "Submitted"].includes(row?.emsFormState);

  // Live option lists for pickers and column filters.
  const typeOptions = computed(() => options.type);
  const referralSourceOptions = computed(() => options.referralSource);
  const statusOptions = computed(() => options.status);
  const entityTypeOptions = computed(() => options.entityType);
  const departmentOptions = computed(() => options.department);
  const serviceLineOptions = computed(() => options.serviceLine);
  const industryOptions = computed(() => options.industry);
  // "Waiting For Pickup" is dropped from the FILTER: it is a value on the list, but not one any request is
  // stored under, so filtering by it would match nothing.
  const statusFilterOptions = computed(() => {
    const pickup = options.status.find((o) => o.value === REMS_STATUS_WAITING_FOR_PICKUP);
    return options.status
      .filter((o) => o.value !== REMS_STATUS_WAITING_FOR_PICKUP)
      .map((option) => (option.value === REMS_STATUS.ADMIN_REVIEW && pickup
        ? { ...option, label: `${option.label}/${pickup.label}` }
        : option));
  });

  // The approval-decision filter on the Approvals inbox, from the same list its badges are rendered from.
  const approvalStatusFilterOptions = computed(() => options.approvalStatus);

  // The Related Entities list's status column: the same list drives the dropdown on every row and the
  // filter in the drawer, so a firm that adds a fifth position can both set it and filter by it.
  const relatedEntityStatusOptions = computed(() => options.relatedEntityStatus);

  return {
    typeLabel,
    typeHint,
    referralSourceLabel,
    referralSourceHint,
    statusLabel,
    statusHint,
    departmentLabel,
    serviceLineLabel,
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
    industryOptions,
    statusFilterOptions,
    approvalStatusFilterOptions,
    relatedEntityStatusOptions,
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

// Loads the engagement's code-valued option sets (Department, Service Line and Industry) plus Marketing /
// Tax Form for the workspace.
export function useRemsEngagementOptionSets () {
  // The code-valued lists come from the shared catalogue (the same ones the labels read); Marketing and Tax
  // Form are resolved here because they are keyed by OptionSetItem *id* rather than by code.
  const catalog = useRemsOptionCatalog();
  const departmentOptions = computed(() => catalog.department);
  const marketingGroups = ref([]);
  const marketingUnavailable = ref(false);
  const taxFormOptions = ref([]);
  const taxFormUnavailable = ref(false);

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
        entityType: EntityType.Rems,
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

  const resolveTaxForms = async () => {
    try {
      const items = await optionSetApi.resolve({ entityType: EntityType.Rems, key: "REMS.TaxForm" });
      taxFormOptions.value = (items || []).map((i) => ({ value: i.id, label: i.label }));
      taxFormUnavailable.value = taxFormOptions.value.length === 0;
    } catch {
      taxFormOptions.value = [];
      taxFormUnavailable.value = true;
    }
  };

  const load = () => Promise.all([
    ensureRemsOptionsLoaded(),
    resolveMarketing(),
    resolveTaxForms()
  ]);

  return {
    departmentOptions,
    // Service Line and Industry — still keyed serviceLine / industry in the data, per the note at
    // the top of this file. Code-valued, so they come from the shared catalogue too.
    serviceLineOptions: computed(() => catalog.serviceLine),
    industryOptions: computed(() => catalog.industry),
    // How often the client is billed (REMS.BillingPeriod). Code-valued like Department and Service Line,
    // so it comes from the shared catalogue rather than being resolved by id.
    billingPeriodOptions: computed(() => catalog.billingPeriod),
    // How a GCS engagement is staffed (REMS.PersonnelLevel) — code-valued, so likewise from the catalogue.
    personnelLevelOptions: computed(() => catalog.personnelLevel),
    marketingGroups,
    marketingUnavailable,
    taxFormOptions,
    taxFormUnavailable,
    load
  };
}

// The Build-EMS entity-type picker (AC-REMS-007.3), from the shared catalogue.
export function useRemsEntityTypes () {
  const catalog = useRemsOptionCatalog();
  return {
    entityTypeOptions: computed(() => catalog.entityType),
    load: ensureRemsOptionsLoaded
  };
}
