<template>
  <div>
    <!-- Yes / No, and No to start with. Two buttons rather than a switch because it is a QUESTION with
         two answers, not a setting: a client who has nobody else to declare should be able to say so and
         move on, and an unticked switch looks the same as a question nobody read. -->
    <div class="ai-ask">
      <div class="ai-ask__label">Will we be preparing a return for anyone else?</div>
      <q-btn-toggle
        :model-value="enabled" :options="YES_NO"
        no-caps unelevated dense toggle-color="primary" color="grey-3" text-color="grey-8"
        @update:model-value="onAsk"
      />
    </div>

    <div v-if="enabled" class="column q-gutter-sm q-mt-sm">
      <q-card v-for="(row, i) in rows" :key="row.sourceKey" flat bordered class="ai-block">
        <q-card-section class="row items-center no-wrap q-pb-none">
          <!-- Named by who they are as soon as they are named, and by their position until then: a card
               headed "Individual 2" is one a reader has to count to find. Truncated rather than allowed
               to set the row's width — the remove button beside it is fixed, and a long name would
               otherwise push it off a phone-width card. -->
          <div class="text-subtitle2 text-weight-medium col ellipsis">{{ individualLabel(row, i) }}</div>
          <q-btn
            flat round dense color="negative" icon="o_delete"
            :aria-label="`Remove ${individualLabel(row, i)}`" @click="remove(i)"
          >
            <q-tooltip>Remove</q-tooltip>
          </q-btn>
        </q-card-section>
        <q-card-section>
          <div class="row q-col-gutter-sm">
            <!-- What they are to the client. It decides the two rules below it, so it is asked first.
                 Two across on a tablet and three only from md: a "Joint | Individual" pair of buttons in
                 a third of a 600px card has no room for the word "Individual". -->
            <app-select
              v-model="row.type" :options="INDIVIDUAL_TYPES" label="Type" required :clearable="false"
              class="col-12 col-sm-6 col-md-4"
              :error="!!err(i, 'type')" :error-message="err(i, 'type')"
              @update:model-value="onRuleChange(row)"
            />

            <!-- Locked to Individual for a child: a child files their own return, and offering "Joint"
                 beside a child's name is offering an answer the firm cannot act on. The disabled button
                 stays visible rather than disappearing, so the client can see the choice was made and
                 why. -->
            <div class="col-12 col-sm-6 col-md-4">
              <app-field-label label="Filing Type" required />
              <!-- The rules are re-applied on change, because this box decides one of them: a spouse
                   moving off a joint return opens the billing choice, and moving back onto one closes it
                   again and puts the answer back to the primary client. -->
              <q-btn-toggle
                v-model="row.filingType" :options="filingOptions(row)" spread
                no-caps unelevated dense toggle-color="primary" color="grey-3" text-color="grey-8"
                @update:model-value="onRuleChange(row)"
              />
              <div v-if="individualFilingLocked(row)" class="ai-note">A child files individually.</div>
            </div>

            <!-- Asked of a child and nobody else, because the answer changes who pays. A line of its own
                 until md, where it completes the row of three — half-width it would have shared a line
                 with First Name, which reads as though the question were about the name beside it. -->
            <div v-if="individualAsksMinor(row)" class="col-12 col-md-4">
              <app-field-label
                label="Is this child a minor?"
                info="A minor is under 18 years old on the last day of the tax year. It decides who is invoiced: a minor child's return is billed to the primary client."
              />
              <q-btn-toggle
                :model-value="row.isMinor" :options="YES_NO" spread
                no-caps unelevated dense toggle-color="primary" color="grey-3" text-color="grey-8"
                @update:model-value="onMinorChange(row, $event)"
              />
            </div>

            <app-text-field
              v-model="row.firstName" label="First Name" required class="col-12 col-sm-6"
              :rules="nameRules('First Name')"
              :error="!!err(i, 'firstName')" :error-message="err(i, 'firstName')"
            />
            <!-- Last Name and Suffix share a line the way they do on the client's own block: the particle
                 goes after the family name it belongs to, and it gets a particle's width rather than a
                 half-row of its own. On a phone the pair still shares its line — split apart they read as
                 two questions instead of one answer. -->
            <app-text-field
              v-model="row.lastName" label="Last Name" required class="col-8 col-sm-4"
              :rules="nameRules('Last Name')"
              :error="!!err(i, 'lastName')" :error-message="err(i, 'lastName')"
            />
            <!-- Optional, and asked of everybody on the card rather than only of a child: the particle is
                 what tells a father from a son, and either of them may be the one on this return. -->
            <app-text-field
              v-model="row.suffix" label="Suffix" class="col-4 col-sm-2" placeholder="Jr."
              :error="!!err(i, 'suffix') || suffixTooLong(row)"
              :error-message="err(i, 'suffix') || 'A suffix is at most 16 characters.'"
            >
              <template #append>
                <q-btn
                  flat dense round size="sm" icon="o_arrow_drop_down" color="grey-7"
                  aria-label="Suffix suggestions"
                >
                  <q-menu anchor="bottom end" self="top end" auto-close>
                    <q-list dense style="min-width: 150px;">
                      <q-item
                        v-for="opt in SUFFIX_OPTIONS" :key="opt.value"
                        clickable :active="row.suffix === opt.value"
                        active-class="bg-grey-2 text-primary"
                        @click="row.suffix = opt.value"
                      >
                        <q-item-section>
                          <q-item-label>{{ opt.label }}</q-item-label>
                          <q-item-label caption>{{ opt.caption }}</q-item-label>
                        </q-item-section>
                      </q-item>
                      <q-separator />
                      <q-item clickable :disable="!row.suffix" @click="row.suffix = ''">
                        <q-item-section class="text-grey-7">No suffix</q-item-section>
                      </q-item>
                    </q-list>
                  </q-menu>
                </q-btn>
              </template>
            </app-text-field>
            <!-- The email is required; the phone is not. Everyone the firm prepares a return for needs an
                 address it can be reached at, and where a person has none of their own — a young child —
                 the client gives the one the firm should use for them, which is the answer the firm needs
                 either way. -->
            <app-text-field
              v-model="row.email" label="Email Address" type="email" required class="col-12 col-sm-6"
              :error="!!err(i, 'email')" :error-message="err(i, 'email')"
            />
            <div class="col-12 col-sm-6">
              <app-phone-input v-model="row.phone" label="Phone Number" />
            </div>

            <!-- Who is invoiced for this person's return. Decided for them where the firm's rules decide
                 it — a spouse on a joint return and a minor child are billed to the primary client — and
                 open otherwise, which now includes a spouse who files individually. -->
            <!-- A line to itself until md, where the two billing name boxes join it: 6 + 3 + 3. Below
                 that they take a line of their own, which keeps "who pays" and "who it is addressed to"
                 from being cut across the middle by a wrap. -->
            <div class="col-12 col-md-6">
              <app-field-label label="Billing Preference" required />
              <q-btn-toggle
                v-model="row.billingPreference" :options="billingOptions(row)" spread
                no-caps unelevated dense toggle-color="primary" color="grey-3" text-color="grey-8"
                @update:model-value="onRuleChange(row)"
              />
              <div v-if="billingNote(row)" class="ai-note">{{ billingNote(row) }}</div>
            </div>

            <!-- "Bill Separately" used to open two more boxes here — Billing First Name and Billing Last
                 Name — asking who the separate invoice was addressed to. They are not asked any more:
                 the answer is the person the row is already about, and asking a client to type a second
                 name for their own child's invoice was asking them to repeat themselves.
                 The COLUMNS stay (see REMSAdditionalIndividual), and a submission that carries an answer
                 still shows it wherever that submission is read back — this stops the question being put,
                 it does not unsay what anybody has already told us. -->
          </div>
        </q-card-section>
      </q-card>

      <div>
        <q-btn
          outline no-caps color="primary" icon="o_add" label="Add another person"
          :disable="!canAdd" @click="add"
        >
          <q-tooltip v-if="!canAdd">
            You can add up to {{ MAX_ADDITIONAL_INDIVIDUALS }} people here.
          </q-tooltip>
        </q-btn>
      </div>
    </div>
  </div>
</template>

<script setup>
// "Spouse & More Individuals" — the other people on an individual client's return.
//
// It replaced the Self and Spouse contact roles this form used to ask an individual for. Those asked for
// a name, an email and a phone: "Self" was the client re-typing what the first card had just asked them,
// and "Spouse" said nothing about the two things the firm actually needs to know about a second person
// on a return — how it is filed, and who pays for it. One spouse also fitted, and children did not.
//
// The RULES live in useRemsIntakeForm, not here, because three things enforce them: this component
// (which disables the controls), the completeness gate the Review button reads, and the server. A
// disabled button is a courtesy; the rule is the shared predicate.
import { computed } from "vue";
import { nameRules } from "utils/personName";
import {
  INDIVIDUAL_TYPES, INDIVIDUAL_FILING_TYPES, INDIVIDUAL_BILLING_PREFERENCES,
  MAX_ADDITIONAL_INDIVIDUALS, additionalIndividualHasData, applyIndividualRules,
  individualAsksMinor, individualBillingLocked, individualFilingLocked, individualLabel,
  newAdditionalIndividual
} from "modules/rems/useRemsIntakeForm";
import { CLIENT_NAME_SUFFIXES } from "modules/rems/remsContactRoles";
import AppSelect from "components/common/AppSelect.vue";
import AppTextField from "components/common/AppTextField.vue";
import AppFieldLabel from "components/common/AppFieldLabel.vue";
import AppPhoneInput from "components/common/AppPhoneInput.vue";

// The list itself, written through rather than round-tripped — the same bargain ClientIntakeFields makes
// with the payload it hosts.
const rows = defineModel({ type: Array, required: true });

const props = defineProps({
  // The client's own surname, used to prefill each new person's. A spouse and children nearly always
  // share it, and the alternative is asking a client to type their own family name once per child.
  // Read at the moment a block is added and never again — see newAdditionalIndividual.
  defaultLastName: { type: String, default: "" },
  // Per-field server messages, keyed by payload path ("additionalIndividuals[0].firstName").
  errors: { type: Object, default: () => ({}) }
});

// Raised when turning the answer back to No would throw away something the client typed. The HOST owns
// the dialog, exactly as it does for the Other Entities toggle.
const emit = defineEmits(["confirm-clear"]);

const YES_NO = [
  { label: "Yes", value: true },
  { label: "No", value: false }
];

// The same shortlist the client's own Suffix box offers, so the two boxes suggest one vocabulary.
const SUFFIX_OPTIONS = CLIENT_NAME_SUFFIXES;

// Mirrors the client's own box and the column behind it. Checked here rather than left to the server so a
// client who pastes a title into it is told before they reach Review.
const suffixTooLong = (row) => (row?.suffix?.trim().length || 0) > 16;

// The answer follows the LIST rather than holding a state of its own, so a payload that already carries
// people opens on Yes and clearing the last card returns it to No.
const enabled = computed(() => rows.value.length > 0);

const canAdd = computed(() => rows.value.length < MAX_ADDITIONAL_INDIVIDUALS);

const err = (i, field) => props.errors[`additionalIndividuals[${i}].${field}`] || "";

// A locked choice is DISABLED, not removed: the client can see what was decided for them, which is the
// difference between a rule and a missing feature.
const filingOptions = (row) => INDIVIDUAL_FILING_TYPES.map((o) => ({
  ...o,
  disable: individualFilingLocked(row) && o.value !== "individual"
}));

const billingOptions = (row) => INDIVIDUAL_BILLING_PREFERENCES.map((o) => ({
  ...o,
  disable: individualBillingLocked(row) && o.value !== "primary"
}));

// Why the choice was made for them, where it was. Says which ANSWER did it, so the client can see that
// changing that answer opens the choice back up — a spouse moved off the joint return can be billed
// separately, and the note is the only thing that says so.
const billingNote = (row) => {
  if (individualBillingLocked(row)) {
    return row.type === "spouse"
      ? "A spouse on a joint return is billed to the primary client."
      : "A minor child is billed to the primary client.";
  }
  return "";
};

function add () {
  if (canAdd.value) rows.value.push(newAdditionalIndividual(props.defaultLastName));
}

// No confirmation on ONE card: unlike the Yes/No answer, which clears them all at once, the cards around
// it are still on screen to make the mistake obvious.
function remove (i) {
  rows.value.splice(i, 1);
}

// The firm's rules are re-applied the moment the answer they depend on changes, so a client who picks
// "Bill Separately" and then changes the Type to Spouse does not leave a separate-billing answer sitting
// behind a control that has just been disabled.
function onRuleChange (row) {
  applyIndividualRules(row);
}

function onMinorChange (row, value) {
  row.isMinor = value;
  applyIndividualRules(row);
}

// Yes opens one card; No clears them, and asks first where there is anything to lose.
function onAsk (value) {
  if (value) {
    if (!rows.value.length) add();
    return;
  }
  if (!rows.value.some(additionalIndividualHasData)) {
    rows.value = [];
    return;
  }
  emit("confirm-clear", () => { rows.value = []; });
}
</script>

<style scoped>
/* The question and its two buttons on one line, wrapping together on a phone. */
.ai-ask {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px 14px;
}
.ai-ask__label {
  font-size: 13px;
  font-weight: 500;
  color: #423939;
}
/* One person: a boxed card, so a family of four reads as four people rather than as one long grid — and
   trimmed like every other block on this form, because a family of four pays the padding four times. */
.ai-block {
  border-radius: 10px;
  background: #fbfcfe;
}
.ai-block .q-card__section {
  padding: 8px 12px;
}
/* Why a control beside it is disabled. Under the control it explains, in the caption weight the rest of
   the form uses for a note about one field. */
.ai-note {
  font-size: 11px;
  color: #5a6675;
  margin-top: 4px;
}
</style>
