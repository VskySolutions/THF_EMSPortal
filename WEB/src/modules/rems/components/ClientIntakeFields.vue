<template>
  <div>
    <!-- 1 · Confirm Your Contact Details ------------------------------------------------------------
         "Confirm", not "Enter". -->
    <q-card flat bordered class="cif-card q-mb-sm">
      <q-card-section class="cif-card__head">
        Confirm Your Contact Details
        <div class="text-caption text-grey-7 text-weight-regular">
          Review the information THF has on file. You can correct your name or mobile number below — your
          email can't be changed here.
        </div>
      </q-card-section>
      <q-separator />
      <q-card-section>
        <div class="row q-col-gutter-sm">
          <!-- An individual is a person, so the name is asked as two boxes and stays two: the record we
               file them under has a given name and a family name. -->
          <template v-if="isIndividual">
            <app-text-field
              v-model="payload.clientFirstName" label="First Name" required class="col-12 col-sm-6"
              :rules="nameRules('First Name')"
              :error="!!errors.clientFirstName" :error-message="errors.clientFirstName"
            />
            <app-text-field
              v-model="payload.clientLastName" label="Last Name" required class="col-8 col-sm-4"
              :rules="nameRules('Last Name')"
              :error="!!errors.clientLastName" :error-message="errors.clientLastName"
            />
            <!-- The generational particle on their name — Jr., Sr., III. Optional, and its own box rather
                 than something typed into the last name: the name is what we file them under. -->
            <app-name-suffix-field v-model="payload.clientSuffix" class="col-4 col-sm-2" />
          </template>
          <app-text-field
            v-else
            v-model="payload.clientName" label="Client/Entity Name" required class="col-12 col-sm-6"
            :error="!!errors.clientName" :error-message="errors.clientName"
          />
          <!-- Locked wherever this form is shown: it is the address the invite went to, so a request that
               named somebody else would be a record of a conversation that never happened. -->
          <app-text-field
            v-model="payload.email" label="Email" readonly class="col-12 col-sm-6"
            :hint="emailHint"
          >
            <template #append><q-icon name="o_lock" size="18px" color="grey-6" /></template>
          </app-text-field>
          <div class="col-12 col-sm-6">
            <app-phone-input v-model="payload.mobileNumber" label="Phone Number" />
          </div>
          <!-- Each option's description is its own tooltip, maintained by staff in Administration →
               Option Sets. -->
          <app-select
            v-model="payload.referralSource" :options="referralSources" label="Referral Source"
            class="col-12 col-sm-6" clearable
            hint="How did you hear about us?"
          >
            <template #option="scope">
              <q-item v-bind="scope.itemProps">
                <q-item-section>
                  <q-item-label>{{ scope.opt.label }}</q-item-label>
                  <q-item-label v-if="scope.opt.description" caption>{{ scope.opt.description }}</q-item-label>
                </q-item-section>
              </q-item>
            </template>
          </app-select>
          <app-text-field
            v-if="payload.referralSource"
            v-model="payload.referralSourceDetail" label="Tell us more" class="col-12 col-sm-6"
            :placeholder="referralDetailPlaceholder"
          />

          <!-- No spouse fields here. -->

          <!-- Business (and Trust and Estate, which is asked the same things): EIN -->
          <app-text-field
            v-if="isBusiness" v-model="payload.ein" label="EIN" required class="col-12 col-sm-6"
            :error="!!errors.ein" :error-message="errors.ein"
          />
        </div>
      </q-card-section>
    </q-card>

    <!-- 2 · Address --------------------------------------------------------------------------------- ONE
         card and, for almost every client, one address. -->
    <q-card flat bordered class="cif-card q-mb-sm">
      <q-card-section class="cif-card__head">
        Physical &amp; Mailing Addresses
        <div class="text-caption text-grey-7 text-weight-regular">
          Where you live or operate. Tell us below if your post goes somewhere else.
        </div>
      </q-card-section>
      <q-separator />
      <q-card-section>
        <div class="cif-addr-head">
          <div class="cif-subhead">
            Physical Address
            <q-icon name="o_info" size="15px" color="grey-6" class="cif-subhead__info">
              <q-tooltip anchor="top middle" self="bottom middle" max-width="300px" :delay="200">
                {{ ADDRESS_HINTS.physical }}
              </q-tooltip>
            </q-icon>
          </div>
        </div>
        <app-address-fields
          v-model="payload.physicalAddress" required gutter="sm" :cols="ADDRESS_COLS"
          :errors="addressErrors(errors, 'physicalAddress')"
        />

        <!-- The box that decides whether there is a second address, at the END of the first one: it is a
             question about the address just typed, and above it there was nothing yet to answer it about. -->
        <q-checkbox
          v-model="payload.mailingSameAsPhysical" dense color="primary" class="cif-same-as q-mt-sm"
          label="Mailing address is the same as physical address"
        />

        <template v-if="!payload.mailingSameAsPhysical">
          <q-separator class="cif-rule" />

          <div class="cif-addr-head">
            <div class="cif-subhead">
              Mailing Address
              <q-icon name="o_info" size="15px" color="grey-6" class="cif-subhead__info">
                <q-tooltip anchor="top middle" self="bottom middle" max-width="300px" :delay="200">
                  {{ ADDRESS_HINTS.mailing }}
                </q-tooltip>
              </q-icon>
            </div>
            <!-- No "Copy from physical" here. -->
          </div>
          <app-address-fields
            v-model="payload.mailingAddress" required gutter="sm" :cols="ADDRESS_COLS"
            :errors="addressErrors(errors, 'mailingAddress')"
          />
        </template>
      </q-card-section>
    </q-card>

    <!-- 3 · Billing Information --------------------------------------------------------------------- Who
         the invoice is for and where it goes, in one block and in that order. -->
    <q-card flat bordered class="cif-card q-mb-sm">
      <q-card-section class="cif-card__head">
        Billing Information
        <div class="text-caption text-grey-7 text-weight-regular">
          {{ ADDRESS_HINTS.billing }}
        </div>
      </q-card-section>
      <q-separator />
      <q-card-section>
        <div class="column q-gutter-sm">
          <!-- The BOX is only drawn where there is more than one: it exists to show a reader where one
               block ends and the next begins. -->
          <div v-for="(row, i) in billingAddresses" :key="row.key" :class="{ 'cif-billing': severalBilling }">
            <div class="cif-addr-head cif-billing__head">
              <!-- Numbered, and shown only once there is more than one. -->
              <div v-if="severalBilling" class="cif-subhead">Billing Information {{ i + 1 }}</div>
              <!-- BOTH sources, because either can be the right one: a client whose post goes to a PO box
                   is often invoiced at the office they actually work. -->
              <div class="cif-addr-copy">
                <q-btn
                  flat dense no-caps size="sm" color="primary" icon="o_content_copy"
                  label="Copy from physical" :disable="!addressHasAny(payload.physicalAddress)"
                  @click="copyIntakeAddress(payload, 'physicalAddress', row)"
                />
                <q-btn
                  v-if="!payload.mailingSameAsPhysical"
                  flat dense no-caps size="sm" color="primary" icon="o_content_copy"
                  label="Copy from mailing" :disable="!addressHasAny(payload.mailingAddress)"
                  @click="copyIntakeAddress(payload, 'mailingAddress', row)"
                />
                <!-- Only from the second block onwards: billing is required, so the last one is not
                     somebody's to remove. -->
                <q-btn
                  v-if="severalBilling"
                  flat round dense color="negative" icon="o_delete" size="sm"
                  :aria-label="`Remove billing information ${i + 1}`" @click="removeBillingAddress(i)"
                >
                  <q-tooltip>Remove this billing block</q-tooltip>
                </q-btn>
              </div>
            </div>
            <!-- Bound through the payload rather than through the `billingAddresses` computed above: the
                 computed is for reading. -->
            <app-address-fields
              v-model="payload.billingAddresses[i]" required
              contact contact-first contact-required contact-label="" gutter="sm" :cols="BILLING_COLS"
              :errors="addressErrors(errors, `billingAddresses[${i}]`)"
            />
          </div>
        </div>

        <div class="q-mt-sm">
          <q-btn
            outline no-caps color="primary" icon="o_add" label="Add another billing block"
            :disable="!canAddBillingAddress" @click="addBillingAddress"
          >
            <q-tooltip v-if="!canAddBillingAddress">
              You can give up to {{ MAX_BILLING_ADDRESSES }} billing blocks.
            </q-tooltip>
          </q-btn>
        </div>
      </q-card-section>
    </q-card>

    <!-- 4 · Spouse & More Individuals (individual only) ---------------------------------------------
         Everyone else on this client's return. -->
    <q-card v-if="isIndividual" flat bordered class="cif-card q-mb-sm">
      <q-card-section class="cif-card__head">
        Spouse &amp; More Individuals
        <div class="text-caption text-grey-7 text-weight-regular">
          Add a spouse or child if we'll be preparing their return too.
        </div>
      </q-card-section>
      <q-separator />
      <q-card-section>
        <!-- The client's own surname prefills each person added below: a spouse and children nearly always
             share it, and it is theirs to type over where they do not. -->
        <additional-individuals-fields
          v-model="payload.additionalIndividuals" :errors="errors"
          :default-last-name="payload.clientLastName"
          @confirm-clear="(done) => emit('confirm-clear-individuals', done)"
        />
      </q-card-section>
    </q-card>

    <!-- Contract Details (Government) -->
    <q-card v-if="isGovernment" flat bordered class="cif-card q-mb-sm">
      <q-card-section class="cif-card__head">Contract Details</q-card-section>
      <q-separator />
      <q-card-section>
        <div class="row q-col-gutter-sm">
          <app-date-field v-model="payload.contractStartDate" label="Contract Start Date" class="col-12 col-sm-6" />
          <app-date-field v-model="payload.contractEndDate" label="Contract End Date" class="col-12 col-sm-6" />
          <app-text-field v-model="payload.originalTerm" label="Original Term" class="col-12 col-sm-6" />
          <app-text-field v-model="payload.renewalTerms" label="Renewal Terms" class="col-12 col-sm-6" />
          <app-date-field v-model="payload.poStartDate" label="Purchase Order Start Date" class="col-12 col-sm-6" />
          <app-date-field v-model="payload.poEndDate" label="Purchase Order End Date" class="col-12 col-sm-6" />
        </div>
      </q-card-section>
    </q-card>

    <!-- Contacts (roles). -->
    <q-card v-if="contactRoleDefs.length" flat bordered class="cif-card q-mb-sm">
      <q-card-section class="cif-card__head">
        Contacts
        <div class="text-caption text-grey-7 text-weight-regular">
          Required contacts need a first name, a last name and an email. Phone is optional.
        </div>
      </q-card-section>
      <q-separator />
      <q-card-section class="column q-gutter-sm">
        <role-contact-fields
          v-for="def in contactRoleDefs" :key="def.key"
          v-model="payload.roles[def.key]"
          :label="def.label" :hint="def.hint" :required="def.required"
          :prefix="`roles.${def.key}`" :errors="errors"
        />
      </q-card-section>
    </q-card>

    <!-- Other entities: who to speak to, not a second set of business details. -->
    <q-card v-if="!isIndividual" flat bordered class="cif-card q-mb-sm">
      <q-card-section class="cif-card__head">
        Other Entities
        <div class="text-caption text-grey-7 text-weight-regular">
          We will set each one up separately and get in touch about it.
        </div>
      </q-card-section>
      <q-separator />
      <q-card-section>
        <q-toggle
          :model-value="hasRelatedEntities"
          label="Are there more entities?"
          color="primary"
          @update:model-value="onToggleRelated"
        />

        <div v-if="hasRelatedEntities" class="column q-gutter-sm q-mt-sm">
          <q-card
            v-for="(entity, i) in payload.relatedEntities" :key="entity.sourceKey"
            flat bordered class="cif-entity"
          >
            <q-card-section class="row items-center no-wrap q-pb-none">
              <div class="text-subtitle2 text-weight-medium col">Entity #{{ i + 1 }}</div>
              <q-btn flat round dense color="negative" icon="o_delete" @click="removeEntity(i)">
                <q-tooltip>Remove</q-tooltip>
              </q-btn>
            </q-card-section>
            <q-card-section>
              <div class="row q-col-gutter-sm">
                <!-- Two across on a tablet and three only from md: an email address in a third of a 600px
                     card is an email address nobody can read back to check it. -->
                <app-text-field
                  v-model="entity.fullName" label="Client/Entity Name" required
                  class="col-12 col-sm-6 col-md-4"
                  :error="!!entityErr(i, 'fullName')" :error-message="entityErr(i, 'fullName')"
                />
                <!-- Required, not "email or phone": each of these becomes its own EMS request, and that
                     request is opened by emailing an intake form to this address. -->
                <app-text-field
                  v-model="entity.emailAddress" label="Email Address" type="email" required
                  class="col-12 col-sm-6 col-md-4"
                  :error="!!entityErr(i, 'emailAddress')" :error-message="entityErr(i, 'emailAddress')"
                />
                <!-- The same dial-code + number control the client's own phone above uses. -->
                <div class="col-12 col-sm-6 col-md-4">
                  <app-phone-input v-model="entity.phoneNumber" label="Phone Number" />
                </div>
              </div>
            </q-card-section>
          </q-card>

          <div>
            <q-btn outline no-caps color="primary" icon="o_add" label="Add another entity" @click="addEntity" />
          </div>
        </div>
      </q-card-section>
    </q-card>
  </div>
</template>

<script setup>
// THE client intake field set — the cards a client fills in, and the cards an Admin corrects afterwards.
import { computed } from "vue";
import { isBusinessEntityType } from "modules/rems/useRemsMeta";
import { addressErrors, addressHasAny } from "modules/rems/remsAddress";
import {
  copyIntakeAddress, intakeRoleDefs, newBillingAddress, newRelatedEntity, relatedEntityHasData,
  MAX_BILLING_ADDRESSES
} from "modules/rems/useRemsIntakeForm";

import { nameRules } from "utils/personName";
import AppTextField from "components/common/AppTextField.vue";
import AppNameSuffixField from "components/common/AppNameSuffixField.vue";
import AppSelect from "components/common/AppSelect.vue";
import AppPhoneInput from "components/common/AppPhoneInput.vue";
import AppDateField from "components/common/AppDateField.vue";
import AppAddressFields from "components/common/AppAddressFields.vue";
import RoleContactFields from "modules/rems/components/RoleContactFields.vue";
import AdditionalIndividualsFields from "modules/rems/components/AdditionalIndividualsFields.vue";

// The payload the host owns; this component writes through it rather than round-tripping a v-model, which
// for a form this size would be a copy of the whole thing on every keystroke.
const payload = defineModel({ type: Object, required: true });

const props = defineProps({
  // The client's entity type (lowercase code). Decides which questions appear — it is THF's own
  // classification, never something the client is asked to confirm, so it is a prop and not a field.
  entityType: { type: String, default: "" },
  // Per-field server messages, keyed by payload path (e.g. "roles.self.email").
  errors: { type: Object, default: () => ({}) },
  // The tenant's REMS.ReferralSource list, resolved by whoever is hosting this: the public page is
  // anonymous and is handed the list with the form, the Admin dialog reads the shared catalogue.
  referralSources: { type: Array, default: () => [] },
  // What the locked email field says about itself. The client is told it is locked to their invitation;
  // an admin is told it is the request's.
  emailHint: { type: String, default: "Locked to your invitation" }
});

// Asked when the client is confirming a change they may not have intended. Two questions, because they
// throw away two different things and the hosts word them differently.
const emit = defineEmits(["confirm-clear-entities", "confirm-clear-individuals"]);

// What each kind of address is, said where it belongs — on the heading for the two that have one, and in
// the card's own subtitle for billing, which is a whole card now.
const ADDRESS_HINTS = {
  physical: "Where the business actually operates, or where the client lives — the address we would visit. Not a PO box.",
  mailing: "Where post should reach them. Use this for a PO box, or if their post goes somewhere other than the physical address.",
  billing: "Who each invoice is for, and where it should be sent. Add another block for every further " +
    "place you are invoiced at."
};

// The grid every address block on this form uses, so a client reads the same shape whether they are telling
// us where they live or where the invoice goes.
const ADDRESS_COLS = {
  country: "col-12 col-sm-4",
  state: "col-12 col-sm-4",
  city: "col-12 col-sm-4",
  addressLine1: "col-12 col-sm-6",
  addressLine2: "col-12 col-sm-6",
  postalCode: "col-12 col-sm-4 col-md-3"
};

// The billing block adds the three boxes saying who the invoice is for, and they LEAD it.
const BILLING_COLS = {
  ...ADDRESS_COLS,
  firstName: "col-12 col-sm-6 col-md-3",
  lastName: "col-12 col-sm-6 col-md-3",
  email: "col-12 col-md-6"
};

const isIndividual = computed(() => props.entityType === "individual");
const isBusiness = computed(() => isBusinessEntityType(props.entityType));
const isGovernment = computed(() => props.entityType === "government");

const referralDetailPlaceholder = computed(() => {
  const chosen = props.referralSources.find((o) => o.value === payload.value.referralSource);
  // The option's own description is the best prompt for the follow-up — "Friend, Family, or Colleague"
  // says what to type far better than a generic "Please provide details".
  return chosen?.description || "Please provide details";
});

// Every role this entity type is asked. Empty for an individual, which is what hides the whole card.
const contactRoleDefs = computed(() => intakeRoleDefs(props.entityType));

// Where the client is invoiced. Read defensively: a payload seeded from a draft saved before this list
// existed simply has none, and a missing key must render an empty section rather than throw on the way in.
const billingAddresses = computed(() => payload.value.billingAddresses || []);

const canAddBillingAddress = computed(() => billingAddresses.value.length < MAX_BILLING_ADDRESSES);

// Whether the client is invoiced in more than one place — which is what decides the block's own chrome.
const severalBilling = computed(() => billingAddresses.value.length > 1);

function addBillingAddress () {
  if (!payload.value.billingAddresses) payload.value.billingAddresses = [];
  if (canAddBillingAddress.value) payload.value.billingAddresses.push(newBillingAddress());
}

// No confirmation.
function removeBillingAddress (i) {
  payload.value.billingAddresses.splice(i, 1);
}

const entityErr = (i, field) => props.errors[`relatedEntities[${i}].${field}`] || "";

// ---- Other entities ----
// The toggle follows the list rather than holding a state of its own, so a payload that already carries
// entities opens with it on and clearing the last row turns it off.
const hasRelatedEntities = computed(() => payload.value.relatedEntities.length > 0);

function addEntity () {
  payload.value.relatedEntities.push(newRelatedEntity(payload.value.relatedEntities.length));
}

function removeEntity (i) {
  payload.value.relatedEntities.splice(i, 1);
}

// Turning the toggle OFF throws away whatever has been typed, so where there is anything to lose the host
// is asked to confirm it — the host owns the dialog, and the two hosts word it differently.
function onToggleRelated (val) {
  if (val) {
    if (!payload.value.relatedEntities.length) addEntity();
    return;
  }
  if (!payload.value.relatedEntities.some(relatedEntityHasData)) {
    payload.value.relatedEntities = [];
    return;
  }
  emit("confirm-clear-entities", () => { payload.value.relatedEntities = []; });
}
</script>

<style scoped>
.cif-card {
  border-radius: 12px;
}
/* Six cards of questions is a long page, and on a phone the client is scrolling all of it. Every card
   gives back some of Quasar's default 16px section padding: it buys nothing on a form whose job is to be
   got through, and four pixels a side across six cards and three nested blocks is most of a screen. The
   gutters between the boxes come down with it (q-col-gutter-sm), so the density is consistent rather than
   tight in one dimension and loose in the other. */
.cif-card .q-card__section {
  padding: 10px 12px;
}
.cif-card .cif-card__head {
  padding: 9px 12px;
  font-size: 15px;
  font-weight: 600;
  color: var(--q-primary);
}
.cif-subhead {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--q-primary);
  margin-bottom: 6px;
}
/* The rule between two groups of fields inside one card. Addresses stacked one under another are answers
   to different questions, and whitespace alone left them reading as one long block — which is how a
   mailing address ends up typed into the physical one. The line does the separating and carries the
   spacing with it, so the headings below it sit where the old q-mt-lg put them. */
.cif-rule {
  margin: 14px 0 12px;
  background: var(--line, #e0e6ed);
}
/* Heading and its copy button on one baseline. The button sits with the label it fills in, so it reads as
   "this address, copied from that one" rather than as a stray action above the fields. */
.cif-addr-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}
.cif-addr-head .cif-subhead {
  margin-bottom: 0;
}
/* The billing block has TWO sources to copy from. They wrap together rather than one of them dropping to
   a line of its own, so the pair still reads as one choice.

   Pushed right by `margin-left: auto` rather than by the row's space-between, because on a lone block
   there is no heading beside them for space-between to push against — one child in a space-between row
   sits at the start. The margin puts them on the right either way, which is where an action on the block
   below belongs: the client reads the fields down the left edge, not the buttons. */
.cif-addr-copy {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 4px 8px;
  margin-left: auto;
}
/* What this address IS, a hover away on the heading it belongs to. Not uppercased with the rest of the
   heading — it is an icon, and the tooltip carries the words. */
.cif-subhead__info {
  margin-left: 5px;
  cursor: help;
  vertical-align: text-bottom;
}
/* The box that decides whether a second address exists. Given the label weight of a field rather than of
   a caption: it is the answer to a question, and at caption weight it read as a note about the block
   above it. */
.cif-same-as :deep(.q-checkbox__label) {
  font-size: 13px;
  color: #423939;
}
.cif-entity {
  border-radius: 10px;
  background: #fbfcfe;
}
/* One place the client is invoiced at: a boxed block, so where several run down the card a reader can see
   where one ends and the next begins. Without the border they were address grids of identical shape
   separated by whitespace, and the addressee of the second read as the tail of the first. */
.cif-billing {
  border: 1px solid #e0e6ed;
  border-radius: 10px;
  padding: 10px 12px;
  background: #fff;
}
/* The block's own heading sits inside it, so it needs the gap the card-level headings get from .cif-rule.
   Small, because on a lone block this row carries the copy buttons and nothing else — there is no heading
   above the fields for it to hold off. */
.cif-billing__head {
  margin-bottom: 4px;
}
</style>
