<template>
  <q-card v-if="rows.length" flat bordered class="app-record-audit q-mb-md">
    <q-card-section class="text-subtitle1 text-weight-medium">{{ title }}</q-card-section>
    <q-separator />
    <q-card-section class="app-record-audit__grid">
      <div v-for="row in rows" :key="row.label" class="app-record-audit__item">
        <div class="app-record-audit__label">{{ row.label }}</div>
        <div class="app-record-audit__value">{{ row.value }}</div>
      </div>
    </q-card-section>
  </q-card>
</template>

<script setup>
// The provenance block every detail page ends with: who made this record and when, who last touched it and
// when — and, once it is deleted.
import { computed } from "vue";
import { useDateFormat } from "composables/useDateFormat";

const props = defineProps({
  // { createdBy, createdOnUtc, updatedBy, updatedOnUtc, deleted, deletedBy, deletedOnUtc }
  audit: { type: Object, default: null },
  title: { type: String, default: "Record information" }
});

const fmt = useDateFormat();

// The server sends "System" for a write the platform made itself, and null only for an actor it can no
// longer name — an account since purged. Both are facts about the record, so neither shows as a blank.
const who = (name) => name || "Unknown";

const rows = computed(() => {
  const audit = props.audit;
  if (!audit) return [];

  const list = [
    { label: "Created By", value: who(audit.createdBy) },
    { label: "Created On", value: fmt.formatDateTime(audit.createdOnUtc) },
    { label: "Updated By", value: who(audit.updatedBy) },
    { label: "Updated On", value: fmt.formatDateTime(audit.updatedOnUtc) }
  ];

  if (audit.deleted) {
    list.push(
      { label: "Deleted By", value: who(audit.deletedBy) },
      { label: "Deleted On", value: fmt.formatDateTime(audit.deletedOnUtc) }
    );
  }

  return list;
});
</script>

<style scoped>
.app-record-audit {
  border-radius: 12px;
}
/* auto-fit rather than a fixed column count: the four sit on one line on a desktop and fold to two and
   then one as the page narrows, with no breakpoint to keep in step per page. */
.app-record-audit__grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px 28px;
}
.app-record-audit__label {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--ink-500);
  margin-bottom: 3px;
}
.app-record-audit__value {
  font-size: 14.5px;
  color: var(--ink-900);
  word-break: break-word;
}
</style>
