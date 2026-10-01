<template>
  <div class="app-field">
    <app-field-label :label="label" :info="info" />
    <q-input
      :model-value="text"
      :placeholder="fallback || 'Not set'"
      :error="invalid"
      error-message="Use a hex colour, such as #1f6478"
      :disable="disable"
      :aria-label="label"
      outlined dense hide-bottom-space maxlength="7"
      @update:model-value="onType"
      @blur="onBlur"
    >
      <template #prepend>
        <!-- The swatch shows what is in effect: the tenant's colour, or the stock one it is standing in for. -->
        <button
          type="button" class="brand-swatch" :class="{ 'brand-swatch--stock': !modelValue }"
          :style="{ background: modelValue || fallback || 'transparent' }" :disabled="disable"
          :aria-label="`Pick ${label}`"
        >
          <q-popup-proxy cover transition-show="scale" transition-hide="scale">
            <q-color
              :model-value="modelValue || fallback || '#ffffff'" format-model="hex" no-header-tabs default-view="palette"
              @update:model-value="pick"
            />
          </q-popup-proxy>
        </button>
      </template>
      <template v-if="modelValue && !disable" #append>
        <q-icon name="o_restart_alt" class="cursor-pointer" size="18px" @click="clear">
          <q-tooltip>Back to the default</q-tooltip>
        </q-icon>
      </template>
    </q-input>
  </div>
</template>

<script setup>
// One colour of the theme: a hex box with a picker on its swatch. An empty box is "use the default", and
// the default is shown as the placeholder so an untouched setting still says what colour it is.
import { ref, computed, watch } from "vue";
import AppFieldLabel from "components/common/AppFieldLabel.vue";
import { isHex } from "services/branding";

const props = defineProps({
  modelValue: { type: String, default: null },
  label: { type: String, default: "" },
  info: { type: String, default: "" },
  // What is used while this is unset.
  fallback: { type: String, default: null },
  disable: { type: Boolean, default: false }
});
const emit = defineEmits(["update:modelValue"]);

// What is in the box, which is not always a colour yet ("#1f6" on the way to "#1f6478").
const text = ref(props.modelValue || "");
watch(() => props.modelValue, (value) => { if ((value || "") !== normalise(text.value)) text.value = value || ""; });

const normalise = (value) => {
  const raw = String(value || "").trim().toLowerCase();
  if (!raw) return "";
  const hex = raw.startsWith("#") ? raw : `#${raw}`;
  // #abc is shorthand for #aabbcc.
  return /^#[0-9a-f]{3}$/.test(hex) ? `#${[...hex.slice(1)].map((ch) => ch + ch).join("")}` : hex;
};

const invalid = computed(() => !!text.value.trim() && !isHex(normalise(text.value)));

const onType = (value) => {
  text.value = value || "";
  const hex = normalise(text.value);
  if (!hex) emit("update:modelValue", null);
  else if (isHex(hex)) emit("update:modelValue", hex);
};

// Leaving the box with half a colour in it puts back the last good one.
const onBlur = () => { text.value = invalid.value ? props.modelValue || "" : normalise(text.value); };

const pick = (hex) => {
  text.value = hex;
  emit("update:modelValue", hex.toLowerCase());
};

const clear = () => {
  text.value = "";
  emit("update:modelValue", null);
};
</script>

<style scoped>
.brand-swatch {
  width: 22px;
  height: 22px;
  padding: 0;
  border: 1px solid rgba(0, 0, 0, 0.18);
  border-radius: 5px;
  cursor: pointer;
}
/* A stock colour standing in for an unset one reads as a suggestion, not a choice. */
.brand-swatch--stock {
  opacity: 0.55;
  border-style: dashed;
}
</style>
