// The client-intake contact roles, in ONE place.
import { NAME_SUFFIXES } from "utils/personName";
import { REMS_ENTITY_TYPE_TRUST_ESTATE } from "modules/rems/useRemsMeta";

// / The label each role is asked and read under.
export const CONTACT_ROLE_LABELS = {
  self: "Self",
  spouse: "Spouse",
  primaryContact: "Primary Client Contact",
  financialContact: "Financial Contact",
  // Retired with the Billing Contact block.
  billingContact: "Billing Contact",
  otherContact: "Other Contact",
  financeDirector: "Finance Director",

  // Trust and Estate only — the person who ACTS for it. Optional for now.
  trustEstateContact: "Trust and Estate Contact",

  // Retired.
  banker: "Banker",
  lawyer: "Lawyer"
};

/// A one-line note on each role, for the tooltip beside it. Only where the label leaves a real question:
/// "Self" and "Spouse" explain themselves.
export const CONTACT_ROLE_HINTS = {
  primaryContact: "Who we speak to about this engagement — the main person on your side.",
  financialContact: "Who we speak to about your finances and reporting.",
  otherContact: "Anyone else you would like us to have on file.",
  financeDirector: "The finance director for this entity.",
  trustEstateContact: "The trustee or personal representative who acts for the trust or estate."
};

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

/// Which of them must be filled in. Mirrors RemsFormPayloadValidator. The Trust and Estate contact is
/// deliberately not among them.
export const REQUIRED_ROLES = {
  individual: [],
  business: ["primaryContact", "financialContact"],
  [REMS_ENTITY_TYPE_TRUST_ESTATE]: ["primaryContact", "financialContact"],
  government: ["financeDirector"]
};

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

/** The roles to render for a group, as [{ key, label, hint. */
export const roleDefsFor = (key, extraKeys = []) => {
  const order = GROUP_ROLES[key] || [];
  const required = REQUIRED_ROLES[key] || [];
  const extras = extraKeys.filter((k) => !order.includes(k));
  return [...order, ...extras].map((roleKey) => ({
    key: roleKey,
    label: CONTACT_ROLE_LABELS[roleKey] || roleKey,
    hint: CONTACT_ROLE_HINTS[roleKey] || "",
    required: required.includes(roleKey)
  }));
};

/** The keys a payload actually carries an answer under — what drives `extraKeys` above. */
export const answeredRoleKeys = (roles) =>
  ALL_ROLE_KEYS.filter((k) => hasAny(roles?.[k]));

/** The generational suffixes offered beside a client's name. */
export const CLIENT_NAME_SUFFIXES = NAME_SUFFIXES;

/** A client's name as it reads — the suffix AFTER the name ("John Smith Jr."). Mirrors
    REMS.ClientDisplayName. After. */
export const clientDisplayName = (name, suffix) =>
  [String(name ?? "").trim(), String(suffix ?? "").trim()].filter(Boolean).join(" ");
