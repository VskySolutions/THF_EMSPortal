<template>
  <div class="app-field">
    <app-field-label :label="label" :required="required" />
    <q-input
      v-model="display"
      mask="##/##/####"
      placeholder="mm/dd/yyyy"
      :rules="isoRules"
      :error="error"
      :error-message="errorMessage"
      :disable="disable"
      :readonly="readonly"
      :hint="hint"
      :autocomplete="autocomplete"
      :aria-label="ariaLabel"
      outlined
      :dense="dense"
      hide-bottom-space
      class="app-date"
      @blur="onBlur"
    >
      <template #append>
        <!-- The whole point of the icon is that somebody can see it. -->
        <q-icon
          name="o_calendar_month"
          size="21px"
          :class="['app-date__icon', { 'app-date__icon--locked': locked }]"
        >
          <q-tooltip v-if="!locked">Pick a date</q-tooltip>
          <q-popup-proxy
            v-if="!locked"
            ref="popupRef"
            cover
            transition-show="jump-down"
            transition-hide="jump-up"
          >
            <q-date
              v-model="isoModel"
              mask="YYYY-MM-DD"
              today-btn
              minimal
              color="primary"
              class="app-date__calendar"
              @update:model-value="onPicked"
            >
              <div class="row items-center justify-end q-gutter-sm">
                <q-btn v-if="isoModel" flat dense no-caps size="sm" color="grey-8" label="Clear" @click="clear" />
                <q-btn v-close-popup flat dense no-caps size="sm" color="primary" label="Done" />
              </div>
            </q-date>
          </q-popup-proxy>
        </q-icon>
      </template>
    </q-input>
  </div>
</template>

<script setup>
// Standard date field. v-model is an ISO calendar date ("YYYY-MM-DD") — what every endpoint stores and
// what a DateOnly column round-trips — while the box READS MM/DD/YYYY, the app's display format.
import { ref, computed, toRef, watch } from "vue";
import AppFieldLabel from "components/common/AppFieldLabel.vue";
import { useFieldLabel } from "composables/useFieldLabel";
import { formatDateOnly } from "composables/useDateFormat";

const props = defineProps({
  /** ISO calendar date, "YYYY-MM-DD". Never a timestamp — this field has no time and no time zone. */
  modelValue: { type: String, default: "" },
  label: { type: String, default: "" },
  required: { type: Boolean, default: false },
  /** Run against the ISO value, not the displayed string — see isoRules. */
  rules: { type: Array, default: () => [] },
  error: { type: Boolean, default: false },
  errorMessage: { type: String, default: "" },
  disable: { type: Boolean, default: false },
  readonly: { type: Boolean, default: false },
  hint: { type: String, default: "" },
  dense: { type: Boolean, default: true },
  // Browser autofill is disabled by default across the app; pass "on" to opt back in.
  autocomplete: { type: String, default: "off" }
});

const emit = defineEmits(["update:modelValue"]);

const popupRef = ref(null);

// Neither readonly nor disabled opens the calendar: a field being read is being read, not filled in.
const locked = computed(() => props.readonly || props.disable);

// ---- ISO ⇄ MM/DD/YYYY ----
// Reformatted from the STRING, never through a Date object: "2026-12-31" parsed as a date is parsed as UTC
// midnight and read back a day early anywhere west of Greenwich.
const isoToDisplay = (iso) => formatDateOnly(iso, "");

const displayToIso = (text) => {
  const m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(String(text || "").trim());
  if (!m) return null;
  const [, mm, dd, yyyy] = m;
  const month = Number(mm);
  const day = Number(dd);
  if (month < 1 || month > 12 || day < 1) return null;
  // Day 0 of the NEXT month is the last day of this one — the only leap-year rule worth writing.
  const lastDay = new Date(Date.UTC(Number(yyyy), month, 0)).getUTCDate();
  return day > lastDay ? null : `${yyyy}-${mm}-${dd}`;
};

// What the box shows.
const display = ref(isoToDisplay(props.modelValue));

watch(() => props.modelValue, (iso) => {
  const next = isoToDisplay(iso);
  if (next !== display.value) display.value = next;
});

watch(display, (text) => {
  const trimmed = String(text || "").trim();
  if (!trimmed) {
    if (props.modelValue) emit("update:modelValue", "");
    return;
  }
  const iso = displayToIso(trimmed);
  if (iso && iso !== props.modelValue) emit("update:modelValue", iso);
});

// Leaving the field with something that is not a date puts back what IS stored, so the box never sits
// there showing "06/2" — or "02/31/2026" — as though it had been accepted.
const onBlur = () => {
  const trimmed = String(display.value || "").trim();
  if (trimmed && !displayToIso(trimmed)) display.value = isoToDisplay(props.modelValue);
};

// The calendar binds the ISO value directly; no conversion, and no round trip through the box.
const isoModel = computed({
  get: () => props.modelValue || "",
  set: (val) => emit("update:modelValue", val || "")
});

// Picking a day is the whole interaction — the panel closes on it rather than waiting to be dismissed.
const onPicked = () => popupRef.value?.hide();

const clear = () => {
  emit("update:modelValue", "");
  popupRef.value?.hide();
};

// A caller's rules are written against the v-model, which is the ISO value — so they are handed that,
// not the MM/DD/YYYY the control happens to be showing.
const isoRules = computed(() => props.rules.map((rule) => () => rule(props.modelValue)));

const { text: ariaLabel } = useFieldLabel(toRef(props, "label"), toRef(props, "required"));
</script>

<style scoped>
/* Visible, and obviously something to press. */
.app-date__icon {
  color: var(--q-primary);
  cursor: pointer;
}
.app-date__icon--locked {
  color: #9aa5b1;
  cursor: default;
}
.app-date__calendar {
  /* `minimal` drops q-date's title bar, which on a dense form is a block of chrome taller than the field
     it belongs to. What is left is the month and the grid. */
  box-shadow: none;
  border-radius: 10px;
}
</style>
