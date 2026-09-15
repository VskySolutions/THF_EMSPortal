<template>
  <!-- The list's counted shortcuts, and the one control that undoes all of them. Left to right: the
       groups, each a labelled row of buttons; then, held to the far right, how much narrowing is on and
       the button that clears it — the same clear the filter drawer offers, so a shortcut and a drawer
       pick are undone together. -->
  <div class="qf-bar">
    <div class="qf-bar__groups">
      <slot />
    </div>
    <div class="qf-bar__end">
      <span class="qf-bar__state" :class="{ 'qf-bar__state--on': activeCount > 0 }">{{ stateText }}</span>
      <q-btn
        flat dense no-caps color="primary" icon="o_filter_alt_off" label="Clear all"
        :disable="!activeCount" @click="$emit('clear')"
      />
    </div>
  </div>
</template>

<script setup>
import { computed } from "vue";

const props = defineProps({
  // How many filters the list is under right now — the drawer's chip count, so the two agree.
  activeCount: { type: Number, default: 0 }
});
defineEmits(["clear"]);

const stateText = computed(() => {
  if (!props.activeCount) return "No filters applied";
  return `${props.activeCount} ${props.activeCount === 1 ? "filter" : "filters"} applied`;
});
</script>

<style scoped>
/* A bar of its own under the table's top bar: the groups read as one strip rather than as buttons
   scattered along the title row, and the clear sits where every list keeps its way out — the far right. */
.qf-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 10px 24px;
  width: 100%;
  padding: 5px 3px;
  border: 1px solid var(--line);
  background: #f7f9fb;
}
.qf-bar__groups {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px 20px;
  flex: 1 1 auto;
  min-width: 0;
}
/* A rule between one group and the next, so three rows of buttons read as three questions. */
.qf-bar__groups > * + * {
  padding-left: 20px;
  border-left: 2px solid #c9d1da;
}
.qf-bar__end {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-left: auto;
  white-space: nowrap;
}
.qf-bar__state {
  font-size: 12px;
  color: var(--ink-500);
}
.qf-bar__state--on {
  color: var(--ink-900);
  font-weight: 600;
}
</style>
