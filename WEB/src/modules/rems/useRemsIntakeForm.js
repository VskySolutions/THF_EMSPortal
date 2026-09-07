import { reactive } from "vue";
import {
  blankAddress, toAddress, fromAddress, addressComplete, addressHasAny, addressHasAnyContent,
  billingAddressList, copyPostalInto
} from "modules/rems/remsAddress";
import {
  ALL_ROLE_KEYS, GROUP_ROLES, groupKey, normalizeRoles, roleDefsFor
} from "modules/rems/remsContactRoles";
// Only the group predicate, which is a plain frozen list. useRemsMeta reaches for the auth store inside
// its composable, never at import time — this form renders on an anonymous page and must not wake one.
import { isBusinessEntityType } from "modules/rems/useRemsMeta";
import { nameIssue } from "utils/personName";

// The client intake form's DATA, in one place — its shape, how a stored payload is read into it, how it
// is written back out, and what still has to be filled in before it can be sent.

const s = (v) => (v == null ? "" : String(v));
const filled = (v) => !!String(v ?? "").trim();
const emailOk = (v) => /^\S+@\S+\.\S+$/.test(String(v ?? "").trim());
const dateOrNull = (v) => (filled(v) ? v : null);
// Adds a validator's complaint to the issue list, and nothing at all when it had none.
const pushIf = (out, issue) => { if (issue) out.push(issue); };

// `prefix` is still in the shape although no box in the app asks for one any more: a submission saved when
// the contact block asked for a courtesy title carries.
const blankRole = () => ({ prefix: "", suffix: "", firstName: "", lastName: "", email: "", phone: "" });
const blankRoles = () => Object.fromEntries(ALL_ROLE_KEYS.map((k) => [k, blankRole()]));

/** A blank editable payload — the RemsFormPayloadV1 camelCase wire shape (field names match
    RemsPublicFormModels.cs exactly), EXCEPT the addresses, which are held canonically. */
export const blankIntakePayload = () => reactive({
  version: 1,
  // The client's name, in one field and in two. An individual fills the two and `clientName` is built
  // from them on the way out; a business or government body fills `clientName` and leaves the two blank.
  clientName: "",
  // The generational particle on an individual's name — Jr., Sr., III. Held apart from the name, and
  // deliberately not folded into it: the name is what THF files and searches the client under.
  clientSuffix: "",
  // Retired from the form, which asked for a courtesy title here before it asked for the suffix. Kept in
  // the shape for the reason blankRole gives: a submission saved under the old box carries one.
  clientPrefix: "",
  clientFirstName: "",
  clientLastName: "",
  email: "",            // LOCKED (to the request's customer email; ignored on submit)
  mobileNumber: "",
  referralSource: "",
  referralSourceDetail: "",
  physicalAddress: blankAddress(),
  // Whether the mailing address IS the physical one.
  mailingSameAsPhysical: true,
  mailingAddress: blankAddress(),
  // Where invoices go, and who each one is addressed to — a LIST, because a client invoiced at two places
  // has two, and the form should not be the thing that decides they have one.
  billingAddresses: openBillingAddresses(null),
  // The retired billing CONTACT answers, kept in the shape and echoed back untouched.
  billingContactName: "",
  billingEmail: "",
  additionalBillingContacts: [],
  spouseName: "",
  spousePhone: "",
  spouseEmail: "",
  // The other people on this individual's return — a spouse, a child, anybody else THF will be preparing
  // for. Empty until the client answers "Yes" to the question above them.
  additionalIndividuals: [],
  ein: "",
  contractStartDate: "",
  contractEndDate: "",
  originalTerm: "",
  renewalTerms: "",
  poStartDate: "",
  poEndDate: "",
  roles: blankRoles(),
  relatedEntities: []   // [{ sourceKey, fullName, emailAddress, phoneNumber }]
});

/** Copy one address into another, ONCE. Deliberately not a live mirror: the client can correct the copy
    afterwards. */
export const copyIntakeAddress = (payload, fromKey, target) => {
  copyPostalInto(typeof target === "string" ? payload[target] : target, payload[fromKey]);
};

/** How many places one client may be invoiced at. */
export const MAX_BILLING_ADDRESSES = 10;

// A stable identity for each billing row, so removing the second of three does not make Vue re-use the
// third's inputs for the second.
let billingAddressSeq = 0;
export const newBillingAddress = (stored = null) => ({
  key: `billing-address-${++billingAddressSeq}`,
  ...toAddress(stored)
});

/** The billing rows a form OPENS with: whatever the stored payload carries, or one blank row where it
    carries none. */
const openBillingAddresses = (stored) => {
  const rows = billingAddressList(stored).map((a) => newBillingAddress(a));
  return rows.length ? rows : [newBillingAddress()];
};

// A contact answered at all. `prefix` is deliberately not counted — see roleHasAny in remsContactRoles.
const roleAny = (r) =>
  filled(r?.firstName) || filled(r?.lastName) || filled(r?.name) || filled(r?.email) || filled(r?.phone);

// A payload written before the name was two boxes carries `name` alone.
const rolePreSplit = (r) => !filled(r?.firstName) && !filled(r?.lastName) && filled(r?.name);
// Phone is captured when known but never required — a contact is a name and a valid email.
const roleComplete = (r) =>
  (rolePreSplit(r) || (filled(r?.firstName) && filled(r?.lastName))) && emailOk(r?.email);

/** A draft saved before the name was split keeps its single `name` until somebody edits the two boxes. */
function fillRole (target, src) {
  target.prefix = src?.prefix ?? "";
  target.suffix = src?.suffix ?? "";
  target.firstName = src?.firstName ?? "";
  target.lastName = src?.lastName ?? "";
  target.name = src?.firstName || src?.lastName ? "" : (src?.name ?? "");
  target.email = src?.email ?? "";
  target.phone = src?.phone ?? "";
}

function makeEntity (e, i) {
  return {
    sourceKey: e?.sourceKey || `related-${Date.now()}-${i}`,
    fullName: e?.fullName ?? "",
    emailAddress: e?.emailAddress ?? "",
    phoneNumber: e?.phoneNumber ?? ""
  };
}

/** A fresh, empty related-entity row. */
export const newRelatedEntity = (index = 0) => ({
  sourceKey: `related-${Date.now()}-${index}`,
  fullName: "",
  emailAddress: "",
  phoneNumber: ""
});

// ---------------------------------------------------------------------------------------------------
// Spouse & more individuals — the other people on this client's return.

/** What relation this person is to the client. */
export const INDIVIDUAL_TYPES = [
  { value: "spouse", label: "Spouse" },
  { value: "child", label: "Child" },
  { value: "other", label: "Other" }
];

/** How their return is filed. */
export const INDIVIDUAL_FILING_TYPES = [
  { value: "joint", label: "Joint" },
  { value: "individual", label: "Individual" }
];

/** Who is invoiced for their return. */
export const INDIVIDUAL_BILLING_PREFERENCES = [
  { value: "primary", label: "Bill To Primary" },
  { value: "separate", label: "Bill Separately" }
];

/** How many extra individuals one client may declare. */
export const MAX_ADDITIONAL_INDIVIDUALS = 10;

/** A child's filing type is not a choice: a child files individually. */
export const individualFilingLocked = (row) => row?.type === "child";

/** "Is this child a minor?" — asked of a child and of nobody else. */
export const individualAsksMinor = (row) => row?.type === "child";

/** Whose billing preference is decided for them: a spouse on a JOINT return, and a child who is still a
    minor. */
// `!== "individual"` rather than `=== "joint"`: joint is the default a row opens with, and a row seeded
// from a payload that predates the filing question carries none at all.
export const individualBillingLocked = (row) =>
  (row?.type === "spouse" && row?.filingType !== "individual") ||
  (row?.type === "child" && row?.isMinor === true);

/** Whether this row CARRIES a separate-billing name — no longer whether one is asked for. */
export const individualHasBillingName = (row) =>
  !individualBillingLocked(row) && row?.billingPreference === "separate" &&
  !!(s(row?.billingFirstName).trim() || s(row?.billingLastName).trim());

/** Force the firm's rules onto a row, in place. */
export function applyIndividualRules (row) {
  if (row.type === "child") {
    row.filingType = "individual";
    // Defaults to a minor.
    if (typeof row.isMinor !== "boolean") row.isMinor = true;
  } else {
    row.isMinor = null;
  }
  if (individualBillingLocked(row)) row.billingPreference = "primary";
  // The pair is no longer asked for at all.
  if (individualBillingLocked(row) || row.billingPreference !== "separate") {
    row.billingFirstName = "";
    row.billingLastName = "";
  }
  return row;
}

let additionalIndividualSeq = 0;

/** A fresh individual — Type unanswered, filed jointly and billed to the primary until told otherwise.
    `lastName` is seeded from the CLIENT's own surname. */
export const newAdditionalIndividual = (lastName = "") => ({
  sourceKey: `individual-${Date.now()}-${++additionalIndividualSeq}`,
  type: "",
  filingType: "joint",
  firstName: "",
  lastName: s(lastName).trim(),
  // NOT seeded from the client's own particle, unlike the surname above.
  suffix: "",
  email: "",
  phone: "",
  isMinor: null,
  billingPreference: "primary",
  billingFirstName: "",
  billingLastName: ""
});

/** One read off a stored payload, with the rules re-applied — an older row may predate one of them. */
const makeAdditionalIndividual = (row, i) => applyIndividualRules({
  sourceKey: row?.sourceKey || `individual-${Date.now()}-${i}`,
  type: s(row?.type),
  filingType: s(row?.filingType) || "joint",
  firstName: s(row?.firstName),
  lastName: s(row?.lastName),
  suffix: s(row?.suffix),
  email: s(row?.email),
  phone: s(row?.phone),
  isMinor: typeof row?.isMinor === "boolean" ? row.isMinor : null,
  billingPreference: s(row?.billingPreference) || "primary",
  billingFirstName: s(row?.billingFirstName),
  billingLastName: s(row?.billingLastName)
});

/** Whether a row carries anything — what decides if clearing them all needs confirming. */
export const additionalIndividualHasData = (row) =>
  filled(row?.type) || filled(row?.firstName) || filled(row?.lastName) ||
  filled(row?.email) || filled(row?.phone);

/** What to call one of these people on a checklist, a review card or a validation message: the name they
    gave, falling back to their relation and their position. */
export const individualLabel = (row, i = 0) => {
  const name = [row?.lastName, row?.firstName, row?.suffix]
    .map((v) => s(v).trim()).filter(Boolean).join(" ");
  if (name) return name;
  const type = INDIVIDUAL_TYPES.find((o) => o.value === row?.type)?.label;
  return type ? `${type} ${i + 1}` : `Individual ${i + 1}`;
};

// The stored code read back as the word the client chose. Falls back to the code itself: a payload
// written under a value this list no longer offers should still say what was answered.
const optionLabel = (list, value) => {
  const v = s(value).trim();
  if (!v) return "—";
  return list.find((o) => o.value === v)?.label || v;
};

/** One additional individual as a read-only CARD. Here rather than in either surface that renders it,
    because BOTH do: the client's review step and the staff panel show one submission. */
export const individualSummary = (row, i = 0) => ({
  key: row?.sourceKey || `individual-${i}`,
  name: individualLabel(row, i),
  // Their relation, drawn as a badge beside the name rather than as a labelled row: it is a category, and
  // a category is what a badge is for.
  type: optionLabel(INDIVIDUAL_TYPES, row?.type),
  email: s(row?.email).trim(),
  phone: s(row?.phone).trim(),
  // The two answers the firm acts on, plus the minor flag where it was asked — one line, because they are
  // read together ("Joint, billed to the primary") and separately mean less.
  filing: optionLabel(INDIVIDUAL_FILING_TYPES, row?.filingType),
  minor: individualAsksMinor(row) ? (row?.isMinor === true ? "Minor" : "Not a minor") : "",
  billing: optionLabel(INDIVIDUAL_BILLING_PREFERENCES, row?.billingPreference),
  // Only on a submission that actually carried one — the form stopped asking, but a form answered before
  // it stopped still reads back complete.
  billedTo: individualHasBillingName(row)
    ? [row?.billingFirstName, row?.billingLastName].map((v) => s(v).trim()).filter(Boolean).join(" ")
    : ""
});

// A retired billing contact read back off a stored payload.
let billingContactSeq = 0;
const newBillingContact = (stored = null) => {
  const row = { key: `billing-${++billingContactSeq}`, ...blankRole() };
  if (stored) fillRole(row, stored);
  return row;
};

/** Read a stored payload into the editable one, in place. `prefill` is the public form's locked intake data
    (the name staff typed and the address the invite was sent to). */
export function seedIntakePayload (payload, stored, prefill = null) {
  const d = stored || {};

  payload.clientName = d.clientName ?? prefill?.clientName ?? "";
  // From the prefill where the client has not answered yet: the staff intake asks for the particle, and the
  // client's form has a box of its own.
  payload.clientSuffix = d.clientSuffix ?? prefill?.clientSuffix ?? "";
  payload.clientPrefix = d.clientPrefix ?? "";
  // The two parts come from the stored answer where they were given.
  payload.clientFirstName = d.clientFirstName ?? prefill?.clientFirstName ?? "";
  payload.clientLastName = d.clientLastName ?? prefill?.clientLastName ?? "";
  payload.email = prefill?.email ?? d.email ?? "";
  payload.mobileNumber = d.mobileNumber ?? prefill?.mobileNumber ?? "";
  payload.referralSource = d.referralSource ?? "";
  payload.referralSourceDetail = d.referralSourceDetail ?? "";
  payload.billingContactName = d.billingContactName ?? "";
  payload.billingEmail = d.billingEmail ?? "";
  payload.spouseName = d.spouseName ?? "";
  payload.spousePhone = d.spousePhone ?? "";
  payload.spouseEmail = d.spouseEmail ?? "";
  payload.ein = d.ein ?? "";
  payload.originalTerm = d.originalTerm ?? "";
  payload.renewalTerms = d.renewalTerms ?? "";
  payload.contractStartDate = d.contractStartDate ?? "";
  payload.contractEndDate = d.contractEndDate ?? "";
  payload.poStartDate = d.poStartDate ?? "";
  payload.poEndDate = d.poEndDate ?? "";

  payload.physicalAddress = toAddress(d.physicalAddress);
  payload.mailingAddress = toAddress(d.mailingAddress);
  // The flag where the payload carries one; otherwise inferred from what is in it.
  payload.mailingSameAsPhysical = typeof d.mailingSameAsPhysical === "boolean"
    ? d.mailingSameAsPhysical
    : !addressHasAny(payload.mailingAddress);
  // The list, with the single billing address a payload written before it carries folded in as the first
  // row — the same courtesy normalizeRoles does for the renamed contact roles.
  payload.billingAddresses = openBillingAddresses(d);

  // Normalized first: a payload filled in under the old business role names still has its contacts, and
  // they belong in the boxes those roles are called by now.
  const storedRoles = normalizeRoles(d.roles);
  ALL_ROLE_KEYS.forEach((k) => fillRole(payload.roles[k], storedRoles[k]));
  payload.additionalBillingContacts = (d.additionalBillingContacts || []).map((r) => newBillingContact(r));

  payload.additionalIndividuals = (d.additionalIndividuals || []).map(makeAdditionalIndividual);
  payload.relatedEntities = (d.relatedEntities || []).map(makeEntity);
}

// `name` is sent alongside the two parts, not instead of them: it is the pair already joined, so every
// reader of "the contact's name" — the review summary, the staff panel.
const outRole = (r) => {
  const joined = [r.firstName, r.lastName].map((v) => s(v).trim()).filter(Boolean).join(" ");
  return {
    prefix: s(r.prefix),
    suffix: s(r.suffix),
    firstName: s(r.firstName),
    lastName: s(r.lastName),
    name: joined || s(r.name),
    email: s(r.email),
    phone: s(r.phone)
  };
};

/** The client's name as one string: the two boxes joined SURNAME FIRST for an individual — "Smith John"
    — and the single box otherwise. */
export function intakeClientName (payload) {
  const joined = [payload.clientLastName, payload.clientFirstName]
    .map((v) => s(v).trim()).filter(Boolean).join(" ");
  return joined || s(payload.clientName);
}

/** Build the outgoing wire payload (dates: "" → null so DateOnly binds; addresses converted). */
export function buildIntakePayload (payload, entityType) {
  const key = intakeRoleSetKey(entityType);
  // The roles this client is ASKED, plus any they have already answered under a role the form has since
  // retired — dropping those on the next save would delete an answer the client gave us.
  const asked = GROUP_ROLES[key] || ALL_ROLE_KEYS;
  const answeredElsewhere = ALL_ROLE_KEYS.filter((k) => !asked.includes(k) && roleAny(payload.roles[k]));
  const roles = {};
  [...asked, ...answeredElsewhere].forEach((k) => { roles[k] = outRole(payload.roles[k]); });

  return {
    version: 1,
    clientName: intakeClientName(payload),
    clientSuffix: s(payload.clientSuffix),
    clientPrefix: s(payload.clientPrefix),
    clientFirstName: s(payload.clientFirstName),
    clientLastName: s(payload.clientLastName),
    email: s(payload.email),
    mobileNumber: s(payload.mobileNumber),
    referralSource: s(payload.referralSource),
    referralSourceDetail: s(payload.referralSourceDetail),
    physicalAddress: fromAddress(payload.physicalAddress),
    // The flag travels AND the address is copied.
    mailingSameAsPhysical: !!payload.mailingSameAsPhysical,
    mailingAddress: fromAddress(
      payload.mailingSameAsPhysical ? payload.physicalAddress : payload.mailingAddress),
    // Blank rows are dropped rather than sent: adding a block and leaving it empty is somebody changing
    // their mind, not an answer, and it would otherwise become a placeless.
    billingAddresses: (payload.billingAddresses || []).filter(addressHasAnyContent).map(fromAddress),
    // The retired billing CONTACT answers, echoed back exactly as they arrived.
    billingContactName: s(payload.billingContactName),
    billingEmail: s(payload.billingEmail),
    additionalBillingContacts: (payload.additionalBillingContacts || []).filter(roleAny).map(outRole),
    spouseName: s(payload.spouseName),
    spousePhone: s(payload.spousePhone),
    spouseEmail: s(payload.spouseEmail),
    // The other people on this return.
    additionalIndividuals: (payload.additionalIndividuals || [])
      .filter(additionalIndividualHasData)
      .map((row) => {
        const out = applyIndividualRules({ ...row });
        return {
          sourceKey: s(out.sourceKey),
          type: s(out.type),
          filingType: s(out.filingType),
          firstName: s(out.firstName),
          lastName: s(out.lastName),
          suffix: s(out.suffix),
          email: s(out.email),
          phone: s(out.phone),
          isMinor: typeof out.isMinor === "boolean" ? out.isMinor : null,
          billingPreference: s(out.billingPreference),
          billingFirstName: s(out.billingFirstName),
          billingLastName: s(out.billingLastName)
        };
      }),
    ein: s(payload.ein),
    contractStartDate: dateOrNull(payload.contractStartDate),
    contractEndDate: dateOrNull(payload.contractEndDate),
    originalTerm: s(payload.originalTerm),
    renewalTerms: s(payload.renewalTerms),
    poStartDate: dateOrNull(payload.poStartDate),
    poEndDate: dateOrNull(payload.poEndDate),
    roles,
    relatedEntities: payload.relatedEntities.map((e, i) => ({
      sourceKey: e.sourceKey || `related-${i + 1}`,
      fullName: s(e.fullName),
      emailAddress: s(e.emailAddress),
      phoneNumber: s(e.phoneNumber)
    }))
  };
}

/** Which role set an entity type is asked. The business family shares one — see remsContactRoles. */
export function intakeRoleSetKey (entityType) {
  return groupKey(entityType, isBusinessEntityType(entityType));
}

/** Every role this entity type is asked, as [{ key, label, hint, required }]. */
export const intakeRoleDefs = (entityType) => {
  const key = intakeRoleSetKey(entityType);
  return key ? roleDefsFor(key) : [];
};

/** What still has to be filled in. */
export function intakeIssues (payload, entityType) {
  const out = [];
  const individual = entityType === "individual";

  if (individual) {
    if (!filled(payload.clientFirstName)) out.push("First name is required.");
    if (!filled(payload.clientLastName)) out.push("Last name is required.");
    // A name that is filled in but is not a name — digits, punctuation, "N/A" — fails the same gate the
    // missing one does, rather than being caught only by the server after the client presses Submit.
    pushIf(out, nameIssue(payload.clientFirstName, "First name"));
    pushIf(out, nameIssue(payload.clientLastName, "Last name"));
  } else if (!filled(payload.clientName)) {
    out.push("Client / entity name is required.");
  }

  const addressIssue = "needs country, state, city, address line 1 and zip code.";
  if (!addressComplete(payload.physicalAddress)) out.push(`Physical address ${addressIssue}`);
  // Only when the client has said it differs. Ticked — which is how the form opens — the mailing address
  // IS the physical one, and buildIntakePayload sends it as such.
  if (!payload.mailingSameAsPhysical && !addressComplete(payload.mailingAddress)) {
    out.push(`Mailing address ${addressIssue}`);
  }
  // Billing is now a whole answer in its own right, not an optional extra: where the invoice goes, and who
  // it is addressed to, in one block.
  const billing = payload.billingAddresses || [];
  const started = billing.filter(addressHasAnyContent);
  if (!started.length) {
    out.push("Billing information is required — give a name, an email and an address for the invoice.");
  }
  billing.forEach((row, i) => {
    if (!addressHasAnyContent(row)) return;
    const label = (billing.length > 1) ? `Billing information ${i + 1}` : "Billing information";
    if (!filled(row.firstName)) out.push(`${label} needs a first name.`);
    if (!filled(row.lastName)) out.push(`${label} needs a last name.`);
    if (!filled(row.email)) {
      out.push(`${label} needs an email address.`);
    } else if (!emailOk(row.email)) {
      out.push(`${label} has an invalid email address.`);
    }
    if (!addressComplete(row)) out.push(`${label} ${addressIssue}`);
    pushIf(out, nameIssue(row.firstName, `${label} first name`));
    pushIf(out, nameIssue(row.lastName, `${label} last name`));
  });

  // The other people on an individual's return. Asked of nobody else, so checked for nobody else — a
  // request whose entity type was changed afterwards must not be blocked on a card its form never showed.
  if (individual) {
    (payload.additionalIndividuals || []).forEach((row, i) => {
      if (!additionalIndividualHasData(row)) return;
      const label = individualLabel(row, i);
      if (!filled(row.type)) out.push(`${label} needs a type — spouse, child or someone else.`);
      if (!filled(row.filingType)) out.push(`${label} needs a filing type.`);
      if (!filled(row.firstName)) out.push(`${label} needs a first name.`);
      if (!filled(row.lastName)) out.push(`${label} needs a last name.`);
      pushIf(out, nameIssue(row.firstName, `${label} first name`));
      pushIf(out, nameIssue(row.lastName, `${label} last name`));
      // Required, and required to be an address rather than merely present: the phone beside it stays
      // optional, as it is on every contact on this form.
      if (!filled(row.email)) {
        out.push(`${label} needs an email address.`);
      } else if (!emailOk(row.email)) {
        out.push(`${label} has an invalid email address.`);
      }
      // Both of these open with an answer and are never cleared by the form, so this fires only for a
      // payload assembled somewhere else. It is here because the server checks the same two.
      if (!filled(row.billingPreference)) out.push(`${label} needs a billing preference.`);
      // No billing NAME is required any more — the form stopped asking for one, so a complete row can no
      // longer be missing it.
      pushIf(out, nameIssue(row.billingFirstName, `${label} billing first name`));
      pushIf(out, nameIssue(row.billingLastName, `${label} billing last name`));
    });
  }

  if (isBusinessEntityType(entityType) && !filled(payload.ein)) {
    out.push("EIN is required for a business.");
  }

  // Driven off the same role definitions the cards are rendered from.
  intakeRoleDefs(entityType).forEach(({ key, label, required }) => {
    const role = payload.roles[key];
    if (required) {
      if (!roleComplete(role)) out.push(`${label} needs a first name, a last name and a valid email.`);
    } else if (roleAny(role) && !roleComplete(role)) {
      out.push(`${label} is partly filled — complete the name and email, or clear it.`);
    }
    // Whatever HAS been typed into the two name boxes has to be a name, required contact or not.
    pushIf(out, nameIssue(role?.firstName, `${label} first name`));
    pushIf(out, nameIssue(role?.lastName, `${label} last name`));
  });

  // The retired billing contacts are not checked. The form stopped asking for them, so a complaint about
  // one would point at a box nobody can see; they are echoed back exactly as they arrived.

  // Name and email both required — the phone stays optional, as on every contact on this form.
  if (!individual) {
    payload.relatedEntities.forEach((e, i) => {
      if (!filled(e.fullName)) out.push(`Entity #${i + 1} needs a client / entity name.`);
      if (!filled(e.emailAddress)) {
        out.push(`Entity #${i + 1} needs an email address.`);
      } else if (!emailOk(e.emailAddress)) {
        out.push(`Entity #${i + 1} has an invalid email address.`);
      }
    });
  }

  return out;
}

/** Whether a related-entity row carries anything — what decides if clearing them needs confirming. */
export const relatedEntityHasData = (e) =>
  filled(e?.fullName) || filled(e?.emailAddress) || filled(e?.phoneNumber);

/** A validation failure from either intake endpoint, split into what the FIELDS need and what the banner
    shows: `{ fields, summary }`. The server sends one string of "path. */
export function parseIntakeFieldErrors (err) {
  const details = err?.response?.data?.error?.details || "";
  const fields = {};
  const summary = [];
  details.split(";").forEach((chunk) => {
    const piece = chunk.trim();
    if (!piece) return;
    const idx = piece.indexOf(":");
    if (idx === -1) { summary.push(piece); return; }
    fields[piece.slice(0, idx).trim()] = piece.slice(idx + 1).trim();
    summary.push(piece.slice(idx + 1).trim());
  });
  return { fields, summary: summary.length ? summary : ["One or more fields need your attention."] };
}
