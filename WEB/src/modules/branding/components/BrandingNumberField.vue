<template>
  <div class="app-field">
    <app-field-label :label="label" :info="info" />
    <q-input
      :model-value="modelValue ?? ''"
      type="number" :min="min" :max="max" :step="step"
      :placeholder="fallback === null ? '' : String(fallback)"
      :suffix="suffix" :disable="disable" :aria-label="label"
      outlined dense hide-bottom-space clearable
      @update:model-value="onInput"
      @blur="clamp"
    />
  </div>
</template>

<script setup>
// One measurement of the theme. Empty is "use the default", shown as the placeholder; a number outside
// the range the API accepts is pulled back into it on the way out of the box.
import AppFieldLabel from "components/common/AppFieldLabel.vue";

const props = defineProps({
  modelValue: { type: Number, default: null },
  label: { type: String, default: "" },
  info: { type: String, default: "" },
  fallback: { type: Number, default: null },
  min: { type: Number, required: true },
  max: { type: Number, required: true },
  step: { type: Number, default: 1 },
  suffix: { type: String, default: "" },
  disable: { type: Boolean, default: false }
});
const emit = defineEmits(["update:modelValue"]);

const onInput = (value) => {
  const n = value === null || value === "" ? null : Number(value);
  emit("update:modelValue", Number.isFinite(n) ? n : null);
};

const clamp = () => {
  if (props.modelValue === null) return;
  const stepped = props.step >= 1 ? Math.round(props.modelValue / props.step) * props.step : props.modelValue;
  const bounded = Math.min(props.max, Math.max(props.min, stepped));
  if (bounded !== props.modelValue) emit("update:modelValue", bounded);
};
</script>
