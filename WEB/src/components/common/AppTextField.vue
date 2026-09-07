<template>
  <div class="app-field">
    <app-field-label :label="label" :required="required" />
    <q-input
      v-model="model"
      :type="type"
      :placeholder="placeholder"
      :rules="rules"
      :error="error"
      :error-message="errorMessage"
      :disable="disable"
      :readonly="readonly"
      :hint="hint"
      :autogrow="autogrow"
      :mask="mask"
      :maxlength="maxlength"
      :autofocus="autofocus"
      :clearable="clearable"
      :autocomplete="autocomplete"
      :aria-label="ariaLabel"
      outlined
      :dense="dense"
      hide-bottom-space
      @blur="$emit('blur', $event)"
    >
      <template v-if="$slots.prepend" #prepend>
        <slot name="prepend" />
      </template>
      <template v-if="$slots.append" #append>
        <slot name="append" />
      </template>
    </q-input>
  </div>
</template>

<script setup>
// Standard single-line text/email/number field.
import { computed, toRef } from "vue";
import AppFieldLabel from "components/common/AppFieldLabel.vue";
import { useFieldLabel } from "composables/useFieldLabel";

const props = defineProps({
  modelValue: { type: [String, Number], default: "" },
  label: { type: String, default: "" },
  // Force the required marker; a trailing "*" in the label also marks it required.
  required: { type: Boolean, default: false },
  type: { type: String, default: "text" },
  placeholder: { type: String, default: undefined },
  rules: { type: Array, default: () => [] },
  error: { type: Boolean, default: false },
  errorMessage: { type: String, default: "" },
  disable: { type: Boolean, default: false },
  readonly: { type: Boolean, default: false },
  hint: { type: String, default: "" },
  autogrow: { type: Boolean, default: false },
  mask: { type: String, default: undefined },
  // Declared rather than left to fall through: an undeclared attribute lands on the wrapper div, where
  // maxlength does nothing at all and the field silently accepts more than the column holds.
  maxlength: { type: [String, Number], default: undefined },
  autofocus: { type: Boolean, default: false },
  clearable: { type: Boolean, default: false },
  dense: { type: Boolean, default: true },
  // Browser autofill is disabled by default across the app; pass a value (e.g. "on") to opt back in.
  autocomplete: { type: String, default: "off" }
});

const emit = defineEmits(["update:modelValue", "blur"]);

const model = computed({
  get: () => props.modelValue,
  set: (val) => emit("update:modelValue", val)
});

// Keep the control accessible now that the visible label is external.
const { text: ariaLabel } = useFieldLabel(toRef(props, "label"), toRef(props, "required"));
</script>
