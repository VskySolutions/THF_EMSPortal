<template>
  <!-- Read-only, grouped presentation of the in-progress payload for the public form's Review step
       (AC-REMS-024.7). -->
  <div>
    <div v-for="g in groups" :key="g.title" class="review-group">
      <div class="review-group__title">
        <q-icon :name="g.icon" size="18px" class="q-mr-xs" />{{ g.title }}
      </div>

      <div v-if="g.kind === 'fields'" class="review-group__body">
        <div v-for="r in g.rows" :key="r.label" class="field-row">
          <div class="rems-label">{{ r.label }}</div>
          <div class="rems-value">
            {{ r.value }}
            <!-- The option item's own description, where the value came from a list that has one. -->
            <template v-if="r.hint">
              <q-icon name="o_info" size="14px" class="rems-value__info" />
              <q-tooltip anchor="top middle" self="bottom middle" max-width="320px" :delay="300">
                {{ r.hint }}
              </q-tooltip>
            </template>
          </div>
        </div>
      </div>

      <div v-else-if="g.kind === 'contacts'">
        <div v-if="g.rows.length" class="column q-gutter-sm">
          <div v-for="r in g.rows" :key="r.role" class="role-row">
            <div class="rems-label">{{ r.role }}<span v-if="r.isRequired" class="text-red-6"> *</span></div>
            <div class="rems-value"><app-name-with-suffix :name="r.name" :suffix="r.suffix" /></div>
            <div class="text-caption text-grey-7">{{ r.email || "no email" }} · {{ r.phone || "no phone" }}</div>
          </div>
        </div>
        <div v-else class="text-grey-6">No additional contacts provided.</div>
      </div>

      <!-- The other people on this return, several to a row. -->
      <div v-else-if="g.kind === 'individuals'" class="review-people__grid">
        <div v-for="p in g.rows" :key="p.key" class="review-person">
          <div class="person-head">
            <span class="rems-value text-weight-medium person-head__name">{{ p.name }}</span>
            <q-badge
              v-if="p.type" :label="p.type" color="blue-grey-1" text-color="blue-grey-8"
              class="person-head__type"
            />
          </div>
          <div class="text-caption text-grey-7 ellipsis">
            {{ p.email || "no email" }}<template v-if="p.phone"> · {{ p.phone }}</template>
          </div>
          <div class="text-caption text-grey-8">
            {{ p.filing }}<template v-if="p.minor"> · {{ p.minor }}</template> · {{ p.billing }}
          </div>
          <div v-if="p.billedTo" class="text-caption text-grey-7 ellipsis">Billed to {{ p.billedTo }}</div>
        </div>
      </div>

      <div v-else-if="g.kind === 'entities'">
        <div v-if="g.rows.length" class="column q-gutter-sm">
          <q-card v-for="(e, i) in g.rows" :key="e.key || i" flat bordered class="q-pa-sm entity-card">
            <div class="rems-value text-weight-medium">{{ e.name || "Unnamed entity" }}</div>
            <div v-for="r in e.rows" :key="r.label" class="field-row field-row--dense">
              <div class="rems-label">{{ r.label }}</div>
              <div class="rems-value">{{ r.value }}</div>
            </div>
          </q-card>
        </div>
        <div v-else class="text-grey-6">{{ g.emptyText || "No other entities provided." }}</div>
      </div>

      <!-- People belonging to a FIELDS group — the addressees, under the addresses they are the other
           half of. -->
      <div v-if="g.people && g.people.length" class="review-people">
        <div class="review-people__title">{{ g.peopleTitle }}</div>
        <div class="review-people__grid">
          <div v-for="(person, i) in g.people" :key="i" class="review-person">
            <div class="rems-label">{{ person.role }}</div>
            <div class="rems-value"><app-name-with-suffix :name="person.name" :suffix="person.suffix" /></div>
            <div class="text-caption text-grey-7">
              {{ person.email || "no email" }}<template v-if="person.phone !== null"> · {{ person.phone || "no phone" }}</template>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from "vue";
import { addressText, billingAddressList, addresseeParts } from "modules/rems/remsAddress";
import { formatDateOnly } from "composables/useDateFormat";
import { isBusinessEntityType } from "modules/rems/useRemsMeta";
import {
  answeredRoleKeys, groupKey, normalizeRoles, roleDefsFor, roleHasAny, roleNameParts
} from "modules/rems/remsContactRoles";
import { additionalIndividualHasData, individualSummary } from "modules/rems/useRemsIntakeForm";
import AppNameWithSuffix from "components/common/AppNameWithSuffix.vue";

const props = defineProps({
  payload: { type: Object, required: true },
  entityType: { type: String, default: "" },
  // The locked request email, shown on the Contact row (the payload email is a courtesy echo only).
  lockedEmail: { type: String, default: "" },
  // The referral-source list, passed down rather than resolved here.
  referralSources: { type: Array, default: () => [] }
});

const isIndividual = computed(() => props.entityType === "individual");
const isBusiness = computed(() => isBusinessEntityType(props.entityType));
const isGovernment = computed(() => props.entityType === "government");

const referralOption = (v) => props.referralSources.find((o) => o.value === v);
// Falls back to the raw value: drafts saved before this was a picker hold free text the client typed.
const referralSourceLabel = (v) => (v ? (referralOption(v)?.label || v) : "—");
const referralSourceHint = (v) => (v ? (referralOption(v)?.description || "") : "");

const val = (v) => (v == null || String(v).trim() === "" ? "—" : v);

// Calendar dates read MM/DD/YYYY and are never timezone-shifted — see formatDateOnly.
const dateOnly = formatDateOnly;

// The role labels, order and required set come from modules/rems/remsContactRoles — the same definition
// the form above is rendered from, so review shows the questions that were actually asked.

const groups = computed(() => {
  const p = props.payload || {};
  const result = [];

  // Contact. An individual's name is reviewed as the two parts they typed, so an error in either one is
  // visible where it was made; every other entity type has the one name it gave.
  const contact = isIndividual.value
    ? [
      { label: "First Name", value: val(p.clientFirstName) },
      { label: "Last Name", value: val(p.clientLastName) },
      { label: "Suffix", value: val(p.clientSuffix) }
    ]
    : [{ label: "Client/Entity Name", value: val(p.clientName) }];
  contact.push(
    { label: "Email (locked)", value: val(props.lockedEmail || p.email) },
    { label: "Phone Number", value: val(p.mobileNumber) },
    { label: "Referral Source", value: referralSourceLabel(p.referralSource), hint: referralSourceHint(p.referralSource) },
    { label: "Referral Details", value: val(p.referralSourceDetail) }
  );
  if (isIndividual.value) {
    // The courtesy title the name box used to ask for, before it asked for a generational suffix.
    if (p.clientPrefix) contact.push({ label: "Prefix", value: p.clientPrefix });
    // The spouse is asked for once, in the Spouse & More Individuals card, and is reviewed in its own group
    // below.
    if (p.spouseName) contact.push({ label: "Spouse Name", value: p.spouseName });
    if (p.spouseEmail) contact.push({ label: "Spouse Email Address", value: p.spouseEmail });
    if (p.spousePhone) contact.push({ label: "Spouse Phone", value: p.spousePhone });
  }
  if (isBusiness.value) contact.push({ label: "EIN", value: val(p.ein) });
  result.push({ title: "Contact", icon: "o_person", kind: "fields", rows: contact });

  // Addresses.
  const roles = normalizeRoles(p.roles);
  const addressRows = [{ label: "Physical Address", value: addressText(p.physicalAddress) }];
  addressRows.push({
    label: "Mailing Address",
    value: p.mailingSameAsPhysical ? "Same as physical address" : addressText(p.mailingAddress)
  });
  result.push({ title: "Physical & Mailing Addresses", icon: "o_place", kind: "fields", rows: addressRows });

  // Billing — a card of its own, because it is a whole answer of its own: who each invoice is for, and
  // where it goes.
  const billing = billingAddressList(p);
  // Numbered only where there is more than one — a "1" over a lone block answers a question nobody asked.
  const billingLabel = (i) => (billing.length > 1 ? `Billing Information ${i + 1}` : "Billing Information");
  const billingPeople = billing
    .map((b, i) => ({ role: billingLabel(i), ...addresseeParts(b), email: b?.email, phone: b?.phone }))
    .filter((person) => person.name || person.email || person.phone);
  result.push({
    title: "Billing Information",
    icon: "o_receipt_long",
    kind: "fields",
    rows: billing.length
      ? billing.map((b, i) => ({ label: billingLabel(i), value: addressText(b) }))
      : [{ label: "Billing Information", value: "—" }],
    people: billingPeople,
    peopleTitle: "Invoices addressed to"
  });

  // Spouse & more individuals — everyone else on this return. Shown for an individual, and only where
  // they named somebody: a card saying "nobody" is a card about a question they answered No to.
  const individuals = (p.additionalIndividuals || []).filter(additionalIndividualHasData);
  if (isIndividual.value && individuals.length) {
    result.push({
      title: "Spouse & More Individuals",
      icon: "o_family_restroom",
      kind: "individuals",
      rows: individuals.map(individualSummary)
    });
  }

  // Contract Details (Government)
  if (isGovernment.value) {
    result.push({
      title: "Contract Details",
      icon: "o_gavel",
      kind: "fields",
      rows: [
        { label: "Contract Start Date", value: dateOnly(p.contractStartDate) },
        { label: "Contract End Date", value: dateOnly(p.contractEndDate) },
        { label: "Original Term", value: val(p.originalTerm) },
        { label: "Renewal Terms", value: val(p.renewalTerms) },
        { label: "PO Start Date", value: dateOnly(p.poStartDate) },
        { label: "PO End Date", value: dateOnly(p.poEndDate) }
      ]
    });
  }

  // Additional Contacts (role contacts, in group order).
  const key = groupKey(props.entityType, isBusinessEntityType(props.entityType));
  const contactRows = roleDefsFor(key, answeredRoleKeys(roles))
    .filter((def) => roleHasAny(roles[def.key]))
    .map((def) => ({
      role: def.label,
      isRequired: def.required,
      ...roles[def.key],
      ...roleNameParts(roles[def.key])
    }));
  // Absent entirely where the group is asked for none — an individual, whose own details are the first
  // card and whose family is a card of its own.
  if (contactRows.length || roleDefsFor(key).length) {
    result.push({ title: "Additional Contacts", icon: "o_groups", kind: "contacts", rows: contactRows });
  }

  // Other Entities — a contact each, not a second set of business details.
  const entities = (p.relatedEntities || [])
    .filter((e) => [e.fullName, e.emailAddress, e.phoneNumber].some((x) => x && String(x).trim()))
    .map((e, i) => {
      const rows = [];
      if (e.emailAddress) rows.push({ label: "Email Address", value: e.emailAddress });
      if (e.phoneNumber) rows.push({ label: "Phone Number", value: e.phoneNumber });
      return { key: e.sourceKey || `entity-${i}`, name: e.fullName, rows };
    });
  if (!isIndividual.value || entities.length) {
    result.push({ title: "Other Entities", icon: "o_apartment", kind: "entities", rows: entities });
  }

  // The billing answers a payload gave under questions the form no longer asks: the two plain boxes that
  // preceded the billing-contact block.
  const retiredBilling = (p.additionalBillingContacts || []).filter(roleHasAny);
  if (p.billingContactName || p.billingEmail || retiredBilling.length) {
    result.push({
      title: "Billing (as previously given)",
      icon: "o_receipt_long",
      kind: "fields",
      rows: [
        { label: "Billing Contact", value: val(p.billingContactName) },
        { label: "Billing Email", value: val(p.billingEmail) }
      ],
      people: retiredBilling.map((role, i) => ({
        role: `Billing Contact ${i + 2}`,
        ...roleNameParts(role),
        email: role.email,
        phone: role.phone
      })),
      peopleTitle: "Also invoiced"
    });
  }

  return result;
});
</script>

<style scoped>
.review-group {
  margin-bottom: 18px;
}
.review-group__title {
  display: flex;
  align-items: center;
  font-size: 13px;
  font-weight: 600;
  color: var(--q-primary);
  text-transform: uppercase;
  letter-spacing: 0.03em;
  border-bottom: 1px solid #e0e6ed;
  padding-bottom: 4px;
  margin-bottom: 10px;
}
.review-group__body {
  display: grid;
  /* auto-fit rather than a hard pair: two columns of a phone-width dialog leave each answer about a
     hundred and thirty pixels wide, which wraps an email address over three lines. Under the minimum it
     drops to one column and every label keeps its value beside it. */
  grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));
  gap: 10px 24px;
}
.field-row--dense {
  margin-top: 4px;
}

/* The addressees sit under the addresses they belong to, but outside their grid: a person is not a
   field, and putting several of them in it was what made the section unreadable. */
.review-people { margin-top: 14px; }
.review-people__title {
  font-size: 12px;
  font-weight: 600;
  color: var(--ink-500, #5a6675);
  margin-bottom: 8px;
}
.review-people__grid {
  display: grid;
  /* The same auto-fit rule as the field grid, at a wider minimum: a card carries a whole email address on
     one line, and below the minimum it drops to a single column rather than wrapping every one of them. */
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 10px;
}
.review-person {
  border: 1px solid #e0e6ed;
  border-radius: 8px;
  padding: 8px 10px;
  background: #fbfcfd;
}
/* The name and what the person IS, on one line. The name takes the room and truncates; the badge keeps
   its width, because "Spouse" cut to "Spo…" is the one word on the card that cannot be guessed from the
   rest of it. */
.person-head {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
}
/* Truncated here rather than with the `ellipsis` utility: .rems-value sets white-space: pre-wrap at the
   same specificity, so whichever stylesheet loaded last would decide whether a long name wraps the card
   to two lines. Said explicitly, it cannot. */
.person-head__name {
  min-width: 0;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}
.person-head__type {
  flex: 0 0 auto;
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.02em;
}
</style>
