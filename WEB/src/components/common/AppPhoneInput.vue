<template>
  <!-- The dial code and the number are ONE answer, so they read as one field: a single label, and the two
       controls flush beneath it with the seam between them collapsed to a single line. -->
  <div class="app-field app-phone">
    <app-field-label :label="label" :required="required" />
    <div class="app-phone__row">
      <app-select
        v-model="iso"
        :options="dialCodeOptions"
        :aria-label="countryLabel"
        use-input
        :clearable="false"
        :dense="dense"
        :disable="disable"
        :readonly="readonly"
        class="app-phone__code"
      />
      <q-input
        :model-value="display"
        :placeholder="exampleNational"
        :error="!!localError"
        :error-message="localError"
        :disable="disable"
        :readonly="readonly"
        :aria-label="ariaLabel"
        autocomplete="off"
        outlined
        :dense="dense"
        hide-bottom-space
        class="app-phone__number"
        @update:model-value="onInput"
        @blur="onBlur"
      />
    </div>
  </div>
</template>

<script setup>
// Reusable phone field: a country dial-code dropdown + number input.
import { ref, computed, toRef, watch } from "vue";
import { AsYouType, isValidPhoneNumber, parsePhoneNumber, getExampleNumber } from "libphonenumber-js";
import examples from "libphonenumber-js/mobile/examples";
import { orderedCountries, dialCodeOption, isoFromDial, dialFromIso, DEFAULT_COUNTRY_ISO } from "composables/useCountries";
import { useFieldLabel } from "composables/useFieldLabel";
import AppSelect from "components/common/AppSelect.vue";
import AppFieldLabel from "components/common/AppFieldLabel.vue";

const props = defineProps({
  // The phone number (stored value; normalised to E.164 when valid).
  modelValue: { type: String, default: "" },
  // Dial code for the number, e.g. "+91" (matches Person.CountryCode storage). May be null.
  country: { type: String, default: null },
  label: { type: String, default: "Phone Number" },
  // Shows the mandatory marker, exactly as AppTextField does. Without it a phone the surrounding form
  // treats as required looks optional next to starred fields, and the form blocks with nothing to point at.
  required: { type: Boolean, default: false },
  countryLabel: { type: String, default: "Country" },
  dense: { type: Boolean, default: true },
  disable: { type: Boolean, default: false },
  readonly: { type: Boolean, default: false },
  // Default ISO country used when none is supplied (uppercase ISO-2).
  defaultCountry: { type: String, default: DEFAULT_COUNTRY_ISO }
});

const emit = defineEmits(["update:modelValue", "update:country", "blur", "update:valid"]);

// The pair's one visible label names the number input too — the dial code is named separately for a
// screen reader, since nothing on screen says what that box is.
const { text: ariaLabel } = useFieldLabel(toRef(props, "label"), toRef(props, "required"));

// US + India are pinned on top (see useCountries); US is the default selection. AppSelect narrows the
// list as the user types, so the full set is handed over as-is.
const dialCodeOptions = orderedCountries.map(dialCodeOption);

// When no dial code is supplied separately (e.g. the value is a bare E.164 string), infer the
// country from the number itself so the dropdown + as-you-type pattern match the stored value.
const isoFromNumber = (val) => {
  if (!val) return null;
  try { return parsePhoneNumber(String(val))?.country || null; } catch { return null; }
};

const iso = ref(isoFromDial(props.country) || isoFromNumber(props.modelValue) || props.defaultCountry);
const display = ref(""); // the formatted national number shown in the input
const localError = ref("");
let lastEmitted = props.modelValue || "";

// An example mobile number for the selected country, in national format (e.g. US → "(201) 555-0123").
// Drives the input placeholder and the "expected format" hint in the validation message.
const exampleNational = computed(() => {
  try { return getExampleNumber(iso.value, examples)?.formatNational() || ""; } catch { return ""; }
});

// Render a stored value (E.164 or partial) into the country's national pattern for display.
const formatNational = (val, region) => {
  if (!val) return "";
  try {
    if (isValidPhoneNumber(val, region)) return parsePhoneNumber(val, region).formatNational();
  } catch { /* fall through to as-you-type */ }
  return new AsYouType(region).input(String(val));
};

display.value = formatNational(props.modelValue, iso.value);

// External value changes (e.g. a record loads) — reformat, unless it is our own echo.
watch(() => props.modelValue, (v) => {
  if ((v || "") === (lastEmitted || "")) return;
  // With no separately-stored dial code, sync the dropdown to the loaded number's country.
  if (!props.country) {
    const derived = isoFromNumber(v);
    if (derived && derived !== iso.value) iso.value = derived;
  }
  display.value = formatNational(v, iso.value);
  // The guard above asks "is this value already on screen?", so it has to follow what the parent wrote just
  // as much as what we emitted.
  lastEmitted = v || "";
});

// A dial code that already matches the current pick names no new country, so it is left alone: +1 is the US
// and Canada both.
watch(() => props.country, (v) => {
  if (!v || v === dialFromIso(iso.value)) return;
  const next = isoFromDial(v);
  if (next && next !== iso.value) iso.value = next;
});

watch(iso, (region) => {
  emit("update:country", dialFromIso(region));
  // Reformat the current input under the new country's pattern.
  display.value = new AsYouType(region).input(display.value);
  emitValue();
  if (display.value) validate();
}, { immediate: false });

// Emit the stored value: E.164 when valid, otherwise the typed (formatted) value.
const emitValue = () => {
  let stored = display.value;
  try {
    if (isValidPhoneNumber(display.value, iso.value)) {
      stored = parsePhoneNumber(display.value, iso.value).number;
    }
  } catch { /* keep the raw value */ }
  lastEmitted = stored;
  emit("update:modelValue", stored);
};

const onInput = (val) => {
  // Format as the user types using the selected country's pattern.
  display.value = new AsYouType(iso.value).input(val || "");
  emitValue();
  validate();
};

const validate = () => {
  localError.value = "";
  if (display.value && iso.value && !isValidPhoneNumber(display.value, iso.value)) {
    localError.value = exampleNational.value
      ? `Enter a valid phone number for the selected country (e.g. ${exampleNational.value}).`
      : "Enter a valid phone number for the selected country.";
  }
  const valid = !localError.value;
  emit("update:valid", valid);
  return valid;
};

const onBlur = (e) => {
  // Keep the national display, but ensure the stored value is E.164 when valid.
  if (validate()) emitValue();
  emit("blur", e);
};

// Allow parents to force validation at submit time.
defineExpose({ validate });
</script>

<style scoped>
.app-phone__row {
  display: flex;
  align-items: flex-start;
}
/* The dial code takes a fixed share and the number the rest. min-width: 0 on both so a long country name
   cannot push the number field out of the column the pair was given. */
.app-phone__code {
  flex: 0 0 42%;
  max-width: 210px;
  min-width: 0;
}
.app-phone__number {
  flex: 1 1 auto;
  min-width: 0;
  /* Collapses the two adjoining borders into one line. */
  margin-left: -1px;
}
/* One control, not two: only the outer corners stay rounded. An outlined Quasar field draws its border on
   the control's ::before, which is what has to be squared off — the root element carries no border. */
.app-phone__code :deep(.q-field__control:before),
.app-phone__code :deep(.q-field__control:after) {
  border-top-right-radius: 0;
  border-bottom-right-radius: 0;
}
.app-phone__number :deep(.q-field__control:before),
.app-phone__number :deep(.q-field__control:after) {
  border-top-left-radius: 0;
  border-bottom-left-radius: 0;
}
/* Whichever half has focus draws its own edge over the other's. */
.app-phone__code :deep(.q-field--focused),
.app-phone__number:focus-within {
  position: relative;
  z-index: 1;
}
</style>
