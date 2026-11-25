<template>
  <!-- How many there are behind a row's icon action; nothing at all when there are none. -->
  <q-badge v-if="count > 0" floating rounded color="primary" class="row-count-badge" :label="count > 99 ? '99+' : count" />
</template>

<script setup>
defineProps({
  count: { type: Number, default: 0 }
});
</script>

<style scoped>
/* Small enough to sit on a dense round button without covering its icon. */
.row-count-badge {
  font-size: 10px;
  padding: 1px 4px;
  min-height: 14px;
  top: 0;
  right: -2px;
  pointer-events: none;
}
</style>
