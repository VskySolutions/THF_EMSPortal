<template>
  <!-- A value from an option set, rendered as its badge: the tenant's label, in the tenant's colours, with
       the tenant's description on the tooltip and their icon in front of it. -->
  <q-badge :style="style" class="app-option-badge">
    <q-icon v-if="option.icon" :name="option.icon" size="13px" class="q-mr-xs" />
    {{ option.label }}
    <!-- Trailing affordance, for the one place a badge is also a CONTROL: the Related Entities list sets a
         status on the badge itself. -->
    <slot />
    <q-tooltip v-if="option.description" max-width="320px" :delay="300">{{ option.description }}</q-tooltip>
  </q-badge>
</template>

<script setup>
// THE badge for an option-set value — a status, a decision, a role, an email event.
import { computed } from "vue";

const props = defineProps({
  // A resolved option: { label, description, backgroundColor, textColor, icon }. Every field but `label` is
  // optional — see useRemsMeta.optionOf, which returns this shape for any code.
  option: { type: Object, required: true }
});

// Neutral grey where the list carries no colour: a list that is not a badge anywhere (a service line, a
// department) has none, and a value somebody added by hand has none until they pick one.
const style = computed(() => ({
  backgroundColor: props.option.backgroundColor || "#9e9e9e",
  color: props.option.textColor || "#ffffff"
}));
</script>

<style scoped>
/* Quasar's own badge metrics; only the colours come from the data. */
.app-option-badge {
  font-weight: 500;
}
</style>
