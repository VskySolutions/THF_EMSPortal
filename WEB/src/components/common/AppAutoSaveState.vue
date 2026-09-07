<template>
  <div v-if="state !== 'idle'" class="row items-center no-wrap q-gutter-xs text-caption">
    <q-spinner v-if="state === 'saving'" size="14px" color="primary" />
    <q-icon v-else-if="state === 'saved'" name="o_check_circle" size="16px" color="positive" />
    <q-icon v-else name="o_error_outline" size="16px" color="negative" />
    <span :class="state === 'error' ? 'text-negative' : 'text-grey-7'">{{ label }}</span>
  </div>
</template>

<script setup>
// What an auto-saving card says about itself.
import { computed } from "vue";

const props = defineProps({
  // idle → nothing shown; saving | saved | error.
  state: { type: String, default: "idle" },
  // The failure, in the words the API used. Shown in place of the label when state is "error".
  message: { type: String, default: "" }
});

const label = computed(() => {
  if (props.state === "saving") return "Saving…";
  if (props.state === "saved") return "Saved";
  return props.message || "Not saved";
});
</script>
