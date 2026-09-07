<template>
  <!-- The badge IS the control — the same AppOptionBadge every other REMS status is drawn with, not a
       button dressed up as one. -->
  <q-btn flat dense no-caps padding="0" class="rss" :disable="saving" :aria-label="`Status: ${current.label}`">
    <app-option-badge :option="current" class="rss__badge">
      <q-spinner v-if="saving" size="13px" class="q-ml-xs" />
      <q-icon v-else name="o_expand_more" size="15px" class="q-ml-xs rss__chevron" />
    </app-option-badge>

    <q-menu v-model="open" anchor="bottom right" self="top right" :offset="[0, 4]">
      <q-list dense class="rss__list">
        <q-item-label header class="rss__header">Set status</q-item-label>
        <q-item
          v-for="choice in options"
          :key="choice.value"
          v-close-popup
          clickable
          :active="choice.value === modelValue"
          active-class="rss__item--active"
          @click="pick(choice.value)"
        >
          <!-- Each choice drawn as the badge it will become, so picking one is picking what the row will
               look like rather than reading a word and hoping. -->
          <q-item-section>
            <app-option-badge :option="choice" />
          </q-item-section>
          <q-item-section side>
            <q-icon v-if="choice.value === modelValue" name="o_check" size="16px" color="primary" />
          </q-item-section>
        </q-item>
        <q-item v-if="!options.length">
          <q-item-section class="text-grey-6">No statuses configured</q-item-section>
        </q-item>
      </q-list>
    </q-menu>
  </q-btn>
</template>

<script setup>
// One related client's hand-set progress, read and written on the same badge.
import { ref, computed } from "vue";
import AppOptionBadge from "components/common/AppOptionBadge.vue";

const props = defineProps({
  // A REMS.RelatedEntityStatus code.
  modelValue: { type: String, default: "" },
  // The resolved option list — the tenant's own, so a firm that has added a fifth position offers it here.
  options: { type: Array, default: () => [] },
  // The full option for the current value, resolved by the caller (useRemsMeta) so an unknown code renders
  // as itself rather than as a blank badge.
  option: { type: Object, default: null },
  // The page's own row is in flight. It disables the control rather than swapping the badge for a spinner:
  // the value is what the reader is looking at, and it should not vanish while it is being saved.
  saving: { type: Boolean, default: false }
});

const emit = defineEmits(["update:modelValue"]);

const open = ref(false);

const current = computed(() =>
  props.option || props.options.find((o) => o.value === props.modelValue) || { label: props.modelValue || "—" });

// Picking the value it already holds is not a change; letting it through would spend a request and an
// audit entry saying nothing happened.
const pick = (value) => {
  if (value !== props.modelValue) emit("update:modelValue", value);
};
</script>

<style scoped>
/* Nothing of the button may show — no min-height, no background, no letter-spacing of its own. What is on
   screen is the badge inside it. */
.rss {
  min-height: 0;
  border-radius: 5px;
}
.rss :deep(.q-btn__content) {
  flex-wrap: nowrap;
}
.rss :deep(.q-focus-helper) {
  border-radius: 5px;
}
/* Wide enough that a column of these lines up whatever word is in them, and left-aligned inside that
   width so the labels start together — the chevron floats out to the right edge. */
.rss__badge {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-width: 148px;
  padding: 4px 8px;
  font-size: 11.5px;
  letter-spacing: 0.1px;
}
/* Slightly held back from the label: it is the affordance, not the value. */
.rss__chevron {
  opacity: 0.85;
}
.rss:hover .rss__chevron {
  opacity: 1;
}
.rss__list {
  min-width: 220px;
  padding-bottom: 4px;
}
.rss__header {
  padding: 6px 16px 2px;
  font-size: 11px;
  line-height: 16px;
  color: #7b8794;
  letter-spacing: 0.3px;
  text-transform: uppercase;
}
.rss__item--active {
  background: rgba(31, 100, 120, 0.08);
}
</style>
