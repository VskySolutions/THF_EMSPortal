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
import { isBusinessIndustryGroup } from "modules/rems/useRemsMeta";
import { nameIssue } from "utils/personName";

// The client intake form's DATA, in one place — its shape, how a stored payload is read into it, how it
// is written back out, and what still has to be filled in before it can be sent.
//
// Two screens hold this form now: the client's own public page, and the dialog an Admin corrects their
// answers in. They are different hosts — one auto-saves against an invite code and walks a review step,
// the other opens over a request and saves once — but the form between them is the same form, and a
// second copy of six hundred lines of shape-and-validation is a copy that drifts. ClientIntakeFields
// renders it; this module knows what is in it.
//
// The addresses are the one place the two shapes differ. The payload travels in the frozen REMS wire
// names (street / zip / countryCode …) and AppAddressFields binds the canonical model, so toAddress() on
// the way in and fromAddress() on the way out — see modules/rems/remsAddress, which is the only module
// that knows both.

const s = (v) => (v == null ? "" : String(v));
const filled = (v) => !!String(v ?? "").trim();
const emailOk = (v) => /^\S+@\S+\.\S+$/.test(String(v ?? "").trim());
const dateOrNull = (v) => (filled(v) ? v : null);
// Adds a validator's complaint to the issue list, and nothing at all when it had none.
const pushIf = (out, issue) => { if (issue) out.push(issue); };

// `prefix` is still in the shape although no box in the app asks for one any more: a submission saved
// when the contact block asked for a courtesy title carries one, and a draft re-opened must not lose it.
// `suffix` is the one particle every name field asks for now.
const blankRole = () => ({ prefix: "", suffix: "", firstName: "", lastName: "", email: "", phone: "" });
const blankRoles = () => Object.fromEntries(ALL_ROLE_KEYS.map((k) => [k, blankRole()]));

/**
 * A blank editable payload — the RemsFormPayloadV1 camelCase wire shape (field names match
 * RemsPublicFormModels.cs exactly), EXCEPT the addresses, which are held canonically.
 */
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
  // Whether the mailing address IS the physical one. Ticked to start with, because for almost every
  // client it is: the form asks for one address and offers a box to untick, rather than asking for the
  // same address twice and offering a Copy button — which is what it did, and which meant the commonest
  // answer on the form was the one that took the most typing.
  //
  // A FLAG rather than a blank mailing address: "same as physical" is an answer, and one the client can
  // change later. On the way out the physical address is copied into the mailing one (see
  // buildIntakePayload), so everything downstream still reads two whole addresses.
  mailingSameAsPhysical: true,
  mailingAddress: blankAddress(),
  // Where invoices go, and who each one is addressed to — a LIST, because a client invoiced at two
  // places has two, and the form should not be the thing that decides they have one. Each row is a whole
  // address AND its addressee: "where does the invoice go?" and "who is it addressed to?" are one
  // question, and the answers used to live in two sections with nothing saying which belonged to which.
  // Opens with one row, which is required — see openBillingAddresses.
  billingAddresses: openBillingAddresses(null),
  // The retired billing CONTACT answers, kept in the shape and echoed back untouched. A submission is
  // the immutable record of what the client sent, and a form that no longer shows the box a thing was
  // typed into must not be the reason that answer disappears. Nothing writes them any more.
  //
  // The retired single `billingAddress` is NOT among them: it is folded into billingAddresses on the way
  // in, so re-saving through this form loses nothing and comes back in the one shape — exactly what
  // normalizeRoles does for the renamed contact roles.
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

/**
 * Copy one address into another, ONCE. Deliberately not a live mirror: the client can correct the copy
 * afterwards, and a later edit to the source must not silently drag the copy along with it — a live
 * mirror would move the billing address every time the physical one was corrected.
 *
 * Only the PLACE is copied. A billing row copied from the office is still addressed to whoever is in
 * accounts payable, so the addressee stays as typed — and so does the row's own local key, which is what
 * keeps Vue from re-using one row's inputs for another. See copyPostalInto.
 *
 * `target` is a payload key for the mailing address and the row itself for a billing one, which has no
 * key of its own to name.
 */
export const copyIntakeAddress = (payload, fromKey, target) => {
  copyPostalInto(typeof target === "string" ? payload[target] : target, payload[fromKey]);
};

/**
 * How many places one client may be invoiced at. Not a limit anybody should meet — it is the guard
 * against a stuck key adding four hundred blocks to one form. Mirrors the server's
 * RemsFormPayloadValidator.MaxBillingAddresses, which is what actually enforces it.
 */
export const MAX_BILLING_ADDRESSES = 10;

// A stable identity for each billing row, so removing the second of three does not make Vue re-use the
// third's inputs for the second. Local to the browser and never sent: fromAddress picks the fields it
// writes, and `key` is not one of them.
let billingAddressSeq = 0;
export const newBillingAddress = (stored = null) => ({
  key: `billing-address-${++billingAddressSeq}`,
  ...toAddress(stored)
});

/**
 * The billing rows a form OPENS with: whatever the stored payload carries, or one blank row where it
 * carries none.
 *
 * There is always at least one, because the section is REQUIRED now: the firm bills somebody, and
 * "whoever the post goes to" was a guess the form used to make on the client's behalf. Further rows are
 * the client's to add, for a client invoiced at more than one place.
 */
const openBillingAddresses = (stored) => {
  const rows = billingAddressList(stored).map((a) => newBillingAddress(a));
  return rows.length ? rows : [newBillingAddress()];
};

// A contact answered at all. `prefix` is deliberately not counted — see roleHasAny in remsContactRoles.
const roleAny = (r) =>
  filled(r?.firstName) || filled(r?.lastName) || filled(r?.name) || filled(r?.email) || filled(r?.phone);

// A payload written before the name was two boxes carries `name` alone. It is accepted as it stands
// rather than asking somebody to retype a name they already gave. Mirrors
// RemsFormPayloadValidator.ValidateRoleFields.
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
//
// A separate question from the contact roles, and it replaced them for an individual: the intake form
// used to ask an individual for a "Self" contact (their own name, email and phone, three boxes below the
// ones that had just asked for exactly that) and a "Spouse" contact. Neither said what the firm actually
// needs to know about the second person on a return — how they file, and who pays for it — and only one
// spouse and no children fitted.
//
// The rules below are the firm's, not the form's: a child files individually; a spouse on a JOINT return
// is billed to the primary client, and so is a minor child. They all come from the same place — one
// return means one invoice, and it goes to whoever the return is filed under. A spouse who files
// individually has a return of their own, so who pays for it is a question again.
//
// They are enforced here so the browser, the review step and the server all read one definition — the
// boxes are disabled on screen as well, but a disabled box is a courtesy and not a rule.
// ---------------------------------------------------------------------------------------------------

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

/**
 * How many extra individuals one client may declare. The same kind of guard MAX_BILLING_ADDRESSES is —
 * against a stuck key, not against a real family. Mirrors the server's
 * RemsFormPayloadValidator.MaxAdditionalIndividuals.
 */
export const MAX_ADDITIONAL_INDIVIDUALS = 10;

/** A child's filing type is not a choice: a child files individually. */
export const individualFilingLocked = (row) => row?.type === "child";

/** "Is this child a minor?" — asked of a child and of nobody else. */
export const individualAsksMinor = (row) => row?.type === "child";

/**
 * Whose billing preference is decided for them: a spouse on a JOINT return, and a child who is still a
 * minor. Everybody else may be billed separately.
 *
 * A spouse is only locked while the return is joint, because that is where the lock comes from: one
 * return, one invoice, and it goes to the primary client. A spouse who files individually has a return
 * of their own, and a return of their own can be billed to whoever pays for it.
 */
// `!== "individual"` rather than `=== "joint"`: joint is the default a row opens with, and a row seeded
// from a payload that predates the filing question carries none at all. Both are joint, and the server
// reads them the same way (RemsAdditionalIndividualPayload.EffectiveFilingType).
export const individualBillingLocked = (row) =>
  (row?.type === "spouse" && row?.filingType !== "individual") ||
  (row?.type === "child" && row?.isMinor === true);

/**
 * Whether this row CARRIES a separate-billing name — no longer whether one is asked for.
 *
 * The form used to open a Billing First Name / Billing Last Name pair as soon as "Bill Separately" was
 * chosen. It does not any more: the answer is the person the row is already about, and asking a client
 * to type their own child's name a second time to address that child's invoice was asking them to repeat
 * themselves. The two columns stay, so a submission that answered them still reads back complete — which
 * is what this predicate is for now, and why it also tests that something was actually written.
 */
export const individualHasBillingName = (row) =>
  !individualBillingLocked(row) && row?.billingPreference === "separate" &&
  !!(s(row?.billingFirstName).trim() || s(row?.billingLastName).trim());

/**
 * Force the firm's rules onto a row, in place. Called when the Type or the minor answer changes and again
 * on the way out, so a row cannot carry an answer the form would not have let anybody give — a client who
 * chose "Bill Separately" and then changed the type to Spouse must not leave a separate-billing row
 * behind the now-disabled control.
 */
export function applyIndividualRules (row) {
  if (row.type === "child") {
    row.filingType = "individual";
    // Defaults to a minor. It is the commoner case for a child whose return is prepared alongside a
    // parent's, and it is the safer default: it bills the primary client rather than inventing a
    // separate payer nobody named.
    if (typeof row.isMinor !== "boolean") row.isMinor = true;
  } else {
    row.isMinor = null;
  }
  if (individualBillingLocked(row)) row.billingPreference = "primary";
  // The pair is no longer asked for at all. Cleared wherever separate billing is not both open and
  // chosen, so a row that picked one up from an older draft does not carry it behind a question that is
  // no longer put.
  if (individualBillingLocked(row) || row.billingPreference !== "separate") {
    row.billingFirstName = "";
    row.billingLastName = "";
  }
  return row;
}

let additionalIndividualSeq = 0;

/**
 * A fresh individual — Type unanswered, filed jointly and billed to the primary until told otherwise.
 *
 * `lastName` is seeded from the CLIENT's own surname, because the people added here are a spouse and
 * children and they nearly always share it. A prefill, not a mirror: it is filled once when the block is
 * added and is the client's to overwrite, exactly as the address copy buttons work — a stepchild or an
 * "Other" who kept their own name types theirs over it, and correcting the client's own surname later
 * does not reach in and rewrite a name somebody has already given us.
 */
export const newAdditionalIndividual = (lastName = "") => ({
  sourceKey: `individual-${Date.now()}-${++additionalIndividualSeq}`,
  type: "",
  filingType: "joint",
  firstName: "",
  lastName: s(lastName).trim(),
  // NOT seeded from the client's own particle, unlike the surname above. A family shares a surname; it
  // does not share a generational particle — the whole point of one is that the son is Jr. where the
  // father is Sr., so copying the client's across would put the wrong answer in the box every time.
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

/**
 * What to call one of these people on a checklist, a review card or a validation message: the name they
 * gave, falling back to their relation and their position. "Individual 2" is a complaint a client has to
 * count blocks to act on; "Smith Jane" is one they can.
 *
 * SURNAME FIRST, with the particle after it, because this is a CLIENT's label: the same person appears on
 * the Related Entities list under exactly this reading, beside the client they were declared under. One
 * person, one name, wherever the platform prints it.
 */
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

/**
 * One additional individual as a read-only CARD. Here rather than in either surface that renders it,
 * because BOTH do: the client's review step and the staff panel show one submission, and a second copy of
 * this is how the two come to describe it differently.
 *
 * The shape is a card, not a list of label/value rows, and that is the point. Seven labelled rows apiece
 * turned a family of four into four tall blocks stacked down the page, and the labels — "Type", "Filing
 * Type", "Billing Preference" — cost more height than the answers and told a reader nothing they could
 * not read off the answer itself: nobody needs "Filing Type: Joint" to understand "Joint".
 *
 * So it comes back as four short lines that tile several to a row:
 *   name + what they are · how to reach them · how they file and who pays · who else pays, where anybody
 * does. Every one of them is a sentence a reader takes in at a glance rather than a table they scan.
 */
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

// A retired billing contact read back off a stored payload. Nothing on the form produces one any more —
// the addressee travels on the billing address — but a submission that carries them has to round-trip
// through the editor untouched. The local `key` is never sent: outRole picks the fields it writes.
let billingContactSeq = 0;
const newBillingContact = (stored = null) => {
  const row = { key: `billing-${++billingContactSeq}`, ...blankRole() };
  if (stored) fillRole(row, stored);
  return row;
};

/**
 * Read a stored payload into the editable one, in place.
 *
 * `prefill` is the public form's locked intake data (the name staff typed and the address the invite was
 * sent to); the Admin's correction dialog passes none, because the stored payload IS the answer. `email`
 * always wins from the prefill where there is one — it is the address the invite went to.
 */
export function seedIntakePayload (payload, stored, prefill = null) {
  const d = stored || {};

  payload.clientName = d.clientName ?? prefill?.clientName ?? "";
  // From the prefill where the client has not answered yet: the staff intake asks for the particle, and
  // the client's form has a box of its own for it, so it is carried across rather than smuggled into a
  // name column.
  payload.clientSuffix = d.clientSuffix ?? prefill?.clientSuffix ?? "";
  payload.clientPrefix = d.clientPrefix ?? "";
  // The two parts come from the stored answer where they were given, and from the prefill's own split of
  // the name staff typed at intake where they were not. `?? ""` rather than a fallback chain into
  // clientName: a business's single name is not a first name, and prefilling one into that box would put
  // "Acme Holdings" where a given name goes the moment somebody switched an entity type.
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
  // The flag where the payload carries one; otherwise inferred from what is in it. A draft or submission
  // written before the box existed gave two addresses because the form demanded two, so one that HAS a
  // mailing address re-opens with the box unticked and that address on screen — reading the flag as
  // "false by default" would be right for those and wrong for nothing, but a payload with no mailing
  // address at all is better served by the ticked default a new form gets.
  payload.mailingSameAsPhysical = typeof d.mailingSameAsPhysical === "boolean"
    ? d.mailingSameAsPhysical
    : !addressHasAny(payload.mailingAddress);
  // The list, with the single billing address a payload written before it carries folded in as the first
  // row — the same courtesy normalizeRoles does for the renamed contact roles. A payload holding both
  // keeps the list: it was written later, by a form that offered the single box nowhere. A payload with
  // no billing address at all re-opens the one blank row the form starts with.
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
// reader of "the contact's name" — the review summary, the staff panel, the Person that gets minted —
// has one field to read. A pre-split contact nobody has retouched keeps whatever it arrived with.
//
// Neither particle is folded into it. A title and a generational suffix are not part of the name a Person
// is FILED under — "Smith Jr." in a surname column is a contact nobody finds by searching for their name
// — so both travel in fields of their own and are joined back on only where the name is READ.
//
// `prefix` is echoed back although the form no longer asks for one, for the reason blankRole gives.
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

/**
 * The client's name as one string: the two boxes joined SURNAME FIRST for an individual — "Smith John" —
 * and the single box otherwise. Mirrors RemsFormPayloadV1.EffectiveClientName, which is what the server
 * files them under, and the order every REMS list already reads a client in.
 *
 * The particle is not in it. It travels beside the name and is joined on where the name is READ, which is
 * what lets AppNameWithSuffix draw it apart from the name it belongs to.
 */
export function intakeClientName (payload) {
  const joined = [payload.clientLastName, payload.clientFirstName]
    .map((v) => s(v).trim()).filter(Boolean).join(" ");
  return joined || s(payload.clientName);
}

/** Build the outgoing wire payload (dates: "" → null so DateOnly binds; addresses converted). */
export function buildIntakePayload (payload, industryGroup) {
  const key = intakeRoleSetKey(industryGroup);
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
    // The flag travels AND the address is copied. Downstream — the review step, the staff panel, the
    // materialised REMSEntityAddress rows — reads two whole addresses and always has, and a client who
    // says "same as physical" has told us their mailing address rather than declined to give one. The
    // flag comes too so that re-opening the form shows the box the way they left it.
    mailingSameAsPhysical: !!payload.mailingSameAsPhysical,
    mailingAddress: fromAddress(
      payload.mailingSameAsPhysical ? payload.physicalAddress : payload.mailingAddress),
    // Blank rows are dropped rather than sent: adding a block and leaving it empty is somebody changing
    // their mind, not an answer, and it would otherwise become a placeless, nameless billing address on
    // the entity.
    billingAddresses: (payload.billingAddresses || []).filter(addressHasAnyContent).map(fromAddress),
    // The retired billing CONTACT answers, echoed back exactly as they arrived. Nothing on the form
    // writes them, and nothing folds them anywhere either — dropping them here would delete, on the next
    // save, an answer the client actually gave.
    billingContactName: s(payload.billingContactName),
    billingEmail: s(payload.billingEmail),
    additionalBillingContacts: (payload.additionalBillingContacts || []).filter(roleAny).map(outRole),
    spouseName: s(payload.spouseName),
    spousePhone: s(payload.spousePhone),
    spouseEmail: s(payload.spouseEmail),
    // The other people on this return. Blank rows are dropped for the reason blank billing rows are —
    // a block somebody opened and thought better of is a change of mind, not an answer — and the firm's
    // rules are applied once more on the way out, so what is stored can never disagree with them.
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
export function intakeRoleSetKey (industryGroup) {
  return groupKey(industryGroup, isBusinessIndustryGroup(industryGroup));
}

/**
 * Every role this entity type is asked, as [{ key, label, hint, required }]. The billing contact is NOT
 * among them any more — whoever an invoice is addressed to travels on the billing address itself.
 */
export const intakeRoleDefs = (industryGroup) => {
  const key = intakeRoleSetKey(industryGroup);
  return key ? roleDefsFor(key) : [];
};

/**
 * What still has to be filled in. Mirrors RemsFormPayloadValidator on the server; both hosts gate on it
 * — the client's Review button and the Admin's Save — so neither offers an action the API will refuse.
 */
export function intakeIssues (payload, industryGroup) {
  const out = [];
  const individual = industryGroup === "individual";

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
  // Billing is now a whole answer in its own right, not an optional extra: where the invoice goes, and
  // who it is addressed to, in one block. At least one is required — the firm bills somebody, and
  // "whoever the post goes to" was a guess the form was making on the client's behalf.
  //
  // A SECOND block is optional, but a second block somebody has started has to be finished for exactly
  // the same reason the first does: half an invoice address reaches nobody.
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
      // longer be missing it. A payload that carries a pair from before is still checked for SHAPE, so a
      // bad value cannot ride in on an old draft.
      pushIf(out, nameIssue(row.billingFirstName, `${label} billing first name`));
      pushIf(out, nameIssue(row.billingLastName, `${label} billing last name`));
    });
  }

  if (isBusinessIndustryGroup(industryGroup) && !filled(payload.ein)) {
    out.push("EIN is required for a business.");
  }

  // Driven off the same role definitions the cards are rendered from.
  intakeRoleDefs(industryGroup).forEach(({ key, label, required }) => {
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
  //
  // Not asked of an individual, so not checked for one: the card is not on their form, and a draft that
  // carries a row from before it was dropped must not block a client on a box they cannot see. The rows
  // are still echoed back and still materialise.
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

/**
 * A validation failure from either intake endpoint, split into what the FIELDS need and what the banner
 * shows: `{ fields, summary }`. The server sends one string of "path: message" pieces separated by
 * semicolons, and the paths are the payload's own — which is exactly what ClientIntakeFields looks its
 * per-field messages up by.
 */
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
