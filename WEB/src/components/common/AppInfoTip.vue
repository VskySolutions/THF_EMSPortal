<template>
  <q-icon :name="icon" :size="size" :color="color" class="app-info-tip">
    <q-tooltip anchor="top middle" self="bottom middle" :max-width="maxWidth" class="text-body2">
      <slot>{{ text }}</slot>
    </q-tooltip>
  </q-icon>
</template>

<script setup>
// The standard place to put a note that explains rather than instructs: an info icon beside a heading, with
// the note on its tooltip.
defineProps({
  // The note. Multi-paragraph or marked-up notes can go in the default slot instead.
  text: { type: String, default: "" },
  icon: { type: String, default: "o_info" },
  // A shade bigger than the 14px on a field label — this one sits beside a section title.
  size: { type: String, default: "16px" },
  color: { type: String, default: "grey-6" },
  maxWidth: { type: String, default: "320px" }
});
</script>

<style scoped>
.app-info-tip {
  margin-left: 4px;
  cursor: help;
  vertical-align: text-bottom;
}
</style>
