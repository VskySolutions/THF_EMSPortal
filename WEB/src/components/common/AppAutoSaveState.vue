<template>
  <div v-if="state !== 'idle'" class="row items-center no-wrap q-gutter-xs text-caption">
    <q-spinner v-if="state === 'saving'" size="14px" color="primary" />
    <q-icon v-else-if="state === 'saved'" name="o_check_circle" size="16px" color="positive" />
    <q-icon v-else name="o_error_outline" size="16px" :color="refused ? 'warning' : 'negative'" />
    <span :class="state !== 'error' ? 'text-grey-7' : refused ? 'text-validation' : 'text-negative'">{{ label }}</span>
  </div>
</template>

<script setup>
// What an auto-saving card says about itself.
import { computed } from "vue";
import { isRefusalMessage } from "services/apiRefusals";

const props = defineProps({
  // idle → nothing shown; saving | saved | error.
  state: { type: String, default: "idle" },
  // The failure, in the words the API used. Shown in place of the label when state is "error".
  message: { type: String, default: "" }
});

// A save the API refused (a validation, a rule) is a warning; only one that failed is an error.
const refused = computed(() => props.state === "error" && isRefusalMessage(props.message));

const label = computed(() => {
  if (props.state === "saving") return "Saving…";
  if (props.state === "saved") return "Saved";
  return props.message || "Not saved";
});
</script>
