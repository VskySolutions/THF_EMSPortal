// The client-intake contact roles, in ONE place.
import { remsOptionCatalog } from "modules/rems/useRemsOptionCatalog";
// Plain constants and a frozen-list predicate only: this file is read on the anonymous client form, which
// must not wake the auth store that the rest of useRemsMeta reaches for.
import { REMS_ENTITY_TYPE_TRUST_ESTATE, isBusinessEntityType } from "modules/rems/useRemsMeta";

// The REMS.ContactRole code each payload key is filed under. The label each role is asked and read under,
// and the note in the tooltip beside it, are that option's label and description.
const ROLE_CODES = {
  self: "self",
  spouse: "spouse",
  primaryContact: "primary_client_contact",
  financialContact: "financial_contact",
  // Retired with the Billing Contact block.
  billingContact: "billing_contact",
  otherContact: "other_contact",
  financeDirector: "finance_director",
  // Trust and Estate only — the person who ACTS for it. Optional for now.
  trustEstateContact: "trust_estate_contact",
  // Retired.
  banker: "banker",
  lawyer: "lawyer"
};

const roleOption = (roleKey) =>
  remsOptionCatalog.contactRole.find((o) => o.value === ROLE_CODES[roleKey]);

// Until the list arrives, or for a role switched off in it, the key is read out: "primaryContact" →
// "Primary Contact".
const keyAsWords = (roleKey) =>
  String(roleKey).replace(/([A-Z])/g, " $1").replace(/^./, (c) => c.toUpperCase());

export const contactRoleLabel = (roleKey) => roleOption(roleKey)?.label || keyAsWords(roleKey);
export const contactRoleHint = (roleKey) => roleOption(roleKey)?.description || "";

/// The keys the payload can carry, in the order they are asked. Used to seed and to iterate a payload
/// whose group is not known (a submission rendered before its entity type is read).
export const ALL_ROLE_KEYS = [
  "self", "spouse",
  "primaryContact", "financialContact", "billingContact", "otherContact",
  "financeDirector",
  "trustEstateContact",
  "banker", "lawyer"
];

// / What each industry group is asked, in display order.
export const GROUP_ROLES = {
  individual: [],
  business: ["primaryContact", "financialContact", "otherContact"],
  [REMS_ENTITY_TYPE_TRUST_ESTATE]:
    ["primaryContact", "financialContact", "trustEstateContact", "otherContact"],
  government: ["financeDirector", "otherContact"]
};

/// Which of them must be filled in. Mirrors RemsFormPayloadValidator. Keyed by ENTITY TYPE where one
/// differs from its family, with "business" the family's default — see requiredRolesFor. The Trust and
/// Estate contact is deliberately not among them, and a government body owes none at all.
export const REQUIRED_ROLES = {
  individual: [],
  business: ["primaryContact", "financialContact"],
  // A commercial client owes only its primary contact.
  commercial: ["primaryContact"],
  [REMS_ENTITY_TYPE_TRUST_ESTATE]: ["primaryContact", "financialContact"],
  government: []
};

/** The roles an entity type owes: its own entry where it has one, otherwise its family's. */
export const requiredRolesFor = (entityType) => {
  if (REQUIRED_ROLES[entityType]) return REQUIRED_ROLES[entityType];
  return isBusinessEntityType(entityType) ? REQUIRED_ROLES.business : [];
};

/// The entity types whose contacts must carry a phone number as well as a name and an email: a trust or
/// estate on every contact it gives, a government body on any it chooses to give. Mirrors
/// RemsFormPayloadValidator.RequiresContactPhone.
const PHONE_REQUIRED_ENTITY_TYPES = [REMS_ENTITY_TYPE_TRUST_ESTATE, "government"];
export const contactPhoneRequired = (entityType) => PHONE_REQUIRED_ENTITY_TYPES.includes(entityType);

// / The payload keys these roles used to be stored under.
export const LEGACY_ROLE_ALIASES = {
  ceo: "primaryContact",
  cfo: "financialContact",
  accountsPayable: "billingContact"
};

// `prefix` and `suffix` are deliberately NOT counted.
const hasAny = (role) =>
  !!role && [role.firstName, role.lastName, role.name, role.email, role.phone]
    .some((v) => v != null && String(v).trim() !== "");

/** A roles node with the legacy keys folded into their successors, so everything downstream reads one
    shape. */
export const normalizeRoles = (roles) => {
  const out = { ...(roles || {}) };
  Object.entries(LEGACY_ROLE_ALIASES).forEach(([legacy, current]) => {
    if (!hasAny(out[current]) && hasAny(out[legacy])) out[current] = out[legacy];
    delete out[legacy];
  });
  return out;
};

/** A contact's name as one string: the two boxes joined, falling back to the single `name` a payload saved
    before the split carries. */
export const roleDisplayName = (role) => {
  const joined = [role?.firstName, role?.lastName]
    .filter((v) => v != null && String(v).trim() !== "")
    .map((v) => String(v).trim())
    .join(" ");
  return joined || String(role?.name ?? "").trim();
};

export const roleHasAny = hasAny;

/** The contact as they are addressed — the joined name with its particles on it. */
/** The same two halves, unjoined, for a surface that RENDERS the name rather than needing a string —
    AppNameWithSuffix draws the particle in bold after the name, and cannot find it inside a joined one. */
export const roleNameParts = (role) => ({
  name: [String(role?.prefix ?? "").trim(), roleDisplayName(role)].filter(Boolean).join(" "),
  suffix: roleDisplayName(role) ? String(role?.suffix ?? "").trim() : ""
});

export const roleAddressedName = (role) => {
  const name = roleDisplayName(role);
  const prefix = String(role?.prefix ?? "").trim();
  const suffix = String(role?.suffix ?? "").trim();
  // A particle with no name beside it is not a name — the caller renders the em dash for that instead.
  return name ? [prefix, name, suffix].filter(Boolean).join(" ") : "";
};

/** Which role set an industry group is asked. The business groups share one, except a trust or estate. */
export const groupKey = (entityType, isBusiness) => {
  if (entityType === REMS_ENTITY_TYPE_TRUST_ESTATE) return REMS_ENTITY_TYPE_TRUST_ESTATE;
  return isBusiness ? "business" : entityType;
};

/** The roles to render for an entity type, as [{ key, label, hint, required, phoneRequired }]. */
export const roleDefsFor = (entityType, extraKeys = []) => {
  const order = GROUP_ROLES[groupKey(entityType, isBusinessEntityType(entityType))] || [];
  const required = requiredRolesFor(entityType);
  const phoneRequired = contactPhoneRequired(entityType);
  const extras = extraKeys.filter((k) => !order.includes(k));
  return [...order, ...extras].map((roleKey) => ({
    key: roleKey,
    label: contactRoleLabel(roleKey),
    hint: contactRoleHint(roleKey),
    required: required.includes(roleKey),
    phoneRequired
  }));
};

/** The keys a payload actually carries an answer under — what drives `extraKeys` above. */
export const answeredRoleKeys = (roles) =>
  ALL_ROLE_KEYS.filter((k) => hasAny(roles?.[k]));

/** A client's name as it reads — the suffix AFTER the name ("John Smith Jr."). Mirrors
    REMS.ClientDisplayName. After. */
export const clientDisplayName = (name, suffix) =>
  [String(name ?? "").trim(), String(suffix ?? "").trim()].filter(Boolean).join(" ");
