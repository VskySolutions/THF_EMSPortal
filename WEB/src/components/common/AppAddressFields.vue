<template>
  <div class="row" :class="`q-col-gutter-${gutter}`">
    <!-- The standard address block, in the standard order: Country → State → City → Address Line 1
         → Address Line 2 → Zip. -->
    <app-select
      v-model="address.countryCode" :options="countryOptions" :label="label('Country')" :required="required"
      use-input :class="col('country')" :dense="dense" :disable="disable" :readonly="readonly"
      :rules="rulesFor('Country is required')"
      :error="!!errorFor('countryCode')" :error-message="errorFor('countryCode')"
      @update:model-value="onCountryChange"
    />

    <!-- State / City come from the dataset when it has them and degrade to a free-text input when it
         doesn't (a country with no states, a state with no cities), so nobody is blocked by a data gap. -->
    <app-select
      v-if="stateMode !== 'text'" v-model="address.stateCode" :options="stateOptions"
      :label="label('State / Province')" :required="required" use-input :class="col('state')"
      :dense="dense" :readonly="readonly" :disable="disable || stateMode === 'pending'"
      :rules="rulesFor('State / Province is required')"
      :error="!!errorFor('stateName')" :error-message="errorFor('stateName')"
      @update:model-value="onStateChange"
    />
    <app-text-field
      v-else v-model="address.stateName" :label="label('State / Province')" :required="required"
      :class="col('state')" :dense="dense" :disable="disable" :readonly="readonly"
      :rules="rulesFor('State / Province is required')"
      :error="!!errorFor('stateName')" :error-message="errorFor('stateName')"
    />

    <app-select
      v-if="cityMode !== 'text'" v-model="address.cityName" :options="cityOptions"
      :label="label('City')" :required="required" use-input :class="col('city')"
      :dense="dense" :readonly="readonly" :disable="disable || cityMode === 'pending'"
      :rules="rulesFor('City is required')"
      :error="!!errorFor('cityName')" :error-message="errorFor('cityName')"
    />
    <app-text-field
      v-else v-model="address.cityName" :label="label('City')" :required="required"
      :class="col('city')" :dense="dense" :disable="disable" :readonly="readonly"
      :rules="rulesFor('City is required')"
      :error="!!errorFor('cityName')" :error-message="errorFor('cityName')"
    />

    <app-text-field
      v-model="address.addressLine1" :label="label('Address Line 1')" :required="required"
      :class="col('addressLine1')" :dense="dense" :disable="disable" :readonly="readonly"
      :rules="rulesFor('Address Line 1 is required')"
      :error="!!errorFor('addressLine1')" :error-message="errorFor('addressLine1')"
    />
    <app-text-field
      v-model="address.addressLine2" label="Address Line 2" :class="col('addressLine2')"
      :dense="dense" :disable="disable" :readonly="readonly"
      :error="!!errorFor('addressLine2')" :error-message="errorFor('addressLine2')"
    />

    <app-text-field
      v-model="address.postalCode" :label="label('Zip Code')" :required="required" :class="col('postalCode')"
      :dense="dense" :disable="disable" :readonly="readonly"
      :rules="rulesFor('Zip Code is required')"
      :error="!!postalMessage" :error-message="postalMessage" @blur="validatePostal"
    />

    <!-- Who the post is addressed to. -->
    <template v-if="contact">
      <!-- Only where the host names the block. -->
      <div v-if="contactLabel" class="col-12 section-subhead" :class="contactOrder">{{ contactLabel }}</div>
      <app-text-field
        v-model="address.firstName" :label="contactLabelFor('First Name')" :required="contactRequired"
        :class="[col('firstName'), contactOrder]"
        :dense="dense" :disable="disable" :readonly="readonly"
        :rules="nameRules('First Name', { required: contactRequired })"
        :error="!!errorFor('firstName')" :error-message="errorFor('firstName')"
      />
      <app-text-field
        v-model="address.lastName" :label="contactLabelFor('Last Name')" :required="contactRequired"
        :class="[col('lastName'), contactOrder]"
        :dense="dense" :disable="disable" :readonly="readonly"
        :rules="nameRules('Last Name', { required: contactRequired })"
        :error="!!errorFor('lastName')" :error-message="errorFor('lastName')"
      />
      <app-text-field
        v-model="address.email" :label="contactLabelFor('Email Address')" type="email"
        :required="contactRequired" :class="[col('email'), contactOrder]"
        :dense="dense" :disable="disable" :readonly="readonly"
        :rules="contactRequired ? [requiredRule('Email Address is required')] : []"
        :error="!!errorFor('email')" :error-message="errorFor('email')"
      />
    </template>

    <!-- Opt-in extras kept for the records that already capture them (profile / person). -->
    <template v-if="extended">
      <div class="col-12 section-subhead">Additional details</div>
      <app-text-field v-model="address.landmark" label="Landmark" class="col-12 col-sm-6" :dense="dense" :disable="disable" :readonly="readonly" />
      <app-text-field v-model="address.buildingName" label="Building / Complex" class="col-12 col-sm-6" :dense="dense" :disable="disable" :readonly="readonly" />
      <app-text-field v-model="address.floorNumber" label="Floor" class="col-12 col-sm-6" :dense="dense" :disable="disable" :readonly="readonly" />
      <app-text-field v-model="address.unitNumber" label="Unit / Suite" class="col-12 col-sm-6" :dense="dense" :disable="disable" :readonly="readonly" />
    </template>
  </div>
</template>

<script setup>
// THE address field-set.
import { ref, reactive, computed, watch } from "vue";
import { State, City } from "country-state-city";
import validator from "validator";
import { orderedCountries, countryOption, countryNameFromIso } from "composables/useCountries";
import { nameRules } from "utils/personName";
import AppSelect from "components/common/AppSelect.vue";
import AppTextField from "components/common/AppTextField.vue";

const address = defineModel({ type: Object, required: true });

const props = defineProps({
  disable: { type: Boolean, default: false },
  readonly: { type: Boolean, default: false },
  dense: { type: Boolean, default: true },
  // When true, Country, State, City, Address Line 1 and Zip Code are mandatory (Address Line 2 never is).
  // Whether an address block is required at all is the surrounding form's call.
  required: { type: Boolean, default: false },
  // Also capture Landmark / Building / Floor / Unit.
  extended: { type: Boolean, default: false },
  // Also capture the addressee — first name, last name, email. Off by default: an address is a place, and
  // only a form that genuinely asks "and who is it addressed to?" wants these.
  contact: { type: Boolean, default: false },
  // The heading over that block.
  contactLabel: { type: String, default: "Addressed to" },
  // Ask the addressee BEFORE the place. For a form whose question is "who is this invoice for, and where
  // does it go?" rather than "where is this address, and who is at it?".
  contactFirst: { type: Boolean, default: false },
  // The addressee's three boxes are mandatory.
  contactRequired: { type: Boolean, default: false },
  // Grid widths, per field, keyed by the canonical field names above — the one thing a host may change
  // about this field-set's LAYOUT. Anything not named keeps the default below.
  cols: { type: Object, default: () => ({}) },
  // The space between the boxes — a Quasar gutter size (xs / sm / md / lg / xl).
  gutter: { type: String, default: "md" },
  // Server-side messages for THIS address, keyed by the canonical field names above. A host whose API
  // reports them under other names re-keys first (see modules/rems/remsAddress).
  errors: { type: Object, default: () => ({}) }
});

// What each box is worth on the grid when the host says nothing.
const DEFAULT_COLS = {
  country: "col-12 col-sm-4",
  state: "col-12 col-sm-4",
  city: "col-12 col-sm-4",
  addressLine1: "col-12 col-sm-8",
  addressLine2: "col-12 col-sm-4",
  postalCode: "col-12 col-sm-4",
  firstName: "col-12 col-sm-6",
  lastName: "col-12 col-sm-6",
  email: "col-12 col-sm-6"
};

const col = (field) => props.cols[field] || DEFAULT_COLS[field];

// The full lists for each level. AppSelect narrows them as the user types, so these stay complete —
// which also means "no options" always genuinely means the dataset has none, never a filtering hiccup.
const countryOptions = orderedCountries.map(countryOption);
const stateOptions = ref([]);
const cityOptions = ref([]);

// How each dependent level renders: "pending" — an empty disabled dropdown until the level above it is
// chosen; "select" — the dataset's list; "text" — a free-text input where the dataset has no list at all.
const stateMode = computed(() => {
  if (!address.value.countryCode) return "pending";
  return stateOptions.value.length ? "select" : "text";
});
const cityMode = computed(() => {
  if (stateMode.value === "pending" || (stateMode.value === "select" && !address.value.stateCode)) return "pending";
  return cityOptions.value.length ? "select" : "text";
});

// ---- Labels, rules and messages ----
const label = (text) => (props.required ? `${text} *` : text);
const contactLabelFor = (text) => (props.contactRequired ? `${text} *` : text);

// The addressee's own boxes, moved ahead of the postal ones by the flex row rather than by a second copy of
// the markup.
const contactOrder = computed(() => (props.contactFirst ? "app-address__contact-first" : ""));

// Rules are what a q-form validates; the same requirement is enforced for hosts without one by validate().
const requiredRule = (message) => (v) => (!!v && String(v).trim().length > 0) || message;
const rulesFor = (message) => (props.required ? [requiredRule(message)] : []);

// Messages raised by our own validate() for hosts that are not inside a q-form, and the postal-format
// message, which is checked on blur as well.
const localErrors = reactive({});
const postalError = ref("");

const errorFor = (field) => props.errors[field] || localErrors[field] || "";
// The postal field carries the server message, the required message and the format message in one slot.
const postalMessage = computed(() => errorFor("postalCode") || postalError.value);

// ---- The cascade ----
// Each level reloads the level below it from the dataset and re-derives the display names the caller
// persists (countryName / stateName) from the selected ISO codes.
const syncCountry = () => {
  const a = address.value;
  a.countryName = countryNameFromIso(a.countryCode);
  stateOptions.value = a.countryCode
    ? State.getStatesOfCountry(a.countryCode).map((s) => ({ label: s.name, value: s.isoCode }))
    : [];
};

const syncState = () => {
  const a = address.value;
  // A record saved as free text (or from before this cascade existed) carries a state name with no ISO
  // code — recover the code so the dropdown shows what is already stored.
  if (!a.stateCode && a.stateName && stateOptions.value.length) {
    const stored = String(a.stateName).trim().toLowerCase();
    a.stateCode = stateOptions.value.find((o) => o.label.toLowerCase() === stored || o.value.toLowerCase() === stored)?.value || null;
  }
  if (a.stateCode) {
    a.stateName = State.getStateByCodeAndCountry(a.stateCode, a.countryCode)?.name || a.stateName;
  }
  cityOptions.value = a.countryCode && a.stateCode
    ? City.getCitiesOfState(a.countryCode, a.stateCode).map((c) => ({ label: c.name, value: c.name }))
    : [];
};

// Also runs when the bound object itself is swapped (a record loads, a parent resets) and immediately on
// mount: the codes can carry the same values on the fresh object, so value-watchers alone would miss it.
watch(
  () => [address.value, address.value.countryCode, address.value.stateCode],
  () => { syncCountry(); syncState(); },
  { immediate: true }
);

// A user-driven pick invalidates everything below it — clear the now-stale selections (the watcher above
// then reloads the dependent lists).
const onCountryChange = () => {
  const a = address.value;
  a.stateCode = null;
  a.stateName = "";
  a.cityName = "";
};
const onStateChange = () => { address.value.cityName = ""; };

// ---- Locale-aware postal validation ----
const validatePostal = () => {
  postalError.value = "";
  const { postalCode, countryCode } = address.value;
  if (!postalCode || !countryCode) return true;
  const locale = validator.isPostalCodeLocales.includes(countryCode) ? countryCode : "any";
  if (!validator.isPostalCode(postalCode, locale)) {
    postalError.value = "Invalid postal code for the selected country.";
    return false;
  }
  return true;
};

// Clear our own messages as the user fixes things (server messages are the host's to clear).
const clearLocalErrors = () => {
  Object.keys(localErrors).forEach((k) => delete localErrors[k]);
  postalError.value = "";
};
watch(address, clearLocalErrors, { deep: true });

// Hosts that are NOT a q-form call this at submit time; a q-form host gets the same coverage from rules.
const REQUIRED_FIELDS = [
  ["countryCode", "Country is required"],
  ["stateName", "State / Province is required"],
  ["cityName", "City is required"],
  ["addressLine1", "Address Line 1 is required"],
  ["postalCode", "Zip Code is required"]
];

// The addressee's three, checked the same way when the host says they are mandatory.
const REQUIRED_CONTACT_FIELDS = [
  ["firstName", "First Name is required"],
  ["lastName", "Last Name is required"],
  ["email", "Email Address is required"]
];

const validate = () => {
  clearLocalErrors();
  if (props.required) {
    REQUIRED_FIELDS.forEach(([field, message]) => {
      if (!String(address.value[field] ?? "").trim()) localErrors[field] = message;
    });
  }
  if (props.contact && props.contactRequired) {
    REQUIRED_CONTACT_FIELDS.forEach(([field, message]) => {
      if (!String(address.value[field] ?? "").trim()) localErrors[field] = message;
    });
  }
  const postalOk = validatePostal();
  return postalOk && Object.keys(localErrors).length === 0;
};

defineExpose({ validate });
</script>

<style scoped>
/* The addressee ahead of the place. `order` on flex children rather than a second copy of the block:
   every field in this component is a direct child of one wrapping flex row, so one class moves the whole
   addressee group and both orders keep reading from a single set of markup. */
.app-address__contact-first {
  order: -1;
}
</style>
