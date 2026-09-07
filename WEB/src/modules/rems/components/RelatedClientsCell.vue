<template>
  <div class="rcc">
    <!-- ONE PANEL, TWO READINGS, decided by what kind of client the request is for. -->
    <div class="rcc__head">
      <template v-if="isIndividual">
        <q-badge class="rcc__tag rcc__tag--parent">Parent</q-badge>
        <span class="rcc__name">
          <app-name-with-suffix :name="parent.name" :suffix="parent.suffix" />
        </span>
        <!-- A spouse on a JOINT return is not a related client — one return, one client, one invoice —
             so they are named here rather than given a row and a status of their own. -->
        <span v-if="parent.jointWith" class="rcc__joint">
          <q-icon name="o_add" size="12px" class="rcc__joint-plus" />
          <app-name-with-suffix :name="parent.jointWith.name" :suffix="parent.jointWith.suffix" />
          <span v-if="relationLabel(parent.jointWith.relation)" class="rcc__relation">
            ({{ relationLabel(parent.jointWith.relation) }})
          </span>
          <span class="rcc__joint-note">— same client, joint filing</span>
        </span>
      </template>

      <!-- No parent, and no client name repeated: the Client Name column is right beside this one, and a
           heading that only restates it is a heading that says nothing. -->
      <template v-else>
        <q-icon name="o_apartment" size="16px" class="rcc__head-icon" />
        <span class="rcc__head-label">Entities</span>
      </template>
    </div>

    <div
      v-for="(row, i) in rows"
      :key="`${row.kind}:${row.id}`"
      class="rcc__child"
    >
      <!-- "Child" for a person on somebody's return; a NUMBER for a business. -->
      <q-badge class="rcc__tag rcc__tag--child">{{ isIndividual ? "Child" : `Entity-${i + 1}` }}</q-badge>
      <span class="rcc__name">
        <!-- The particle after the name and in bold, as the parent above and the Client column beside it
             draw theirs: a related client is told from their own father by that particle and nothing else. -->
        <app-name-with-suffix :name="row.name" :suffix="row.suffix" />
        <!-- The contact details the client gave for them. -->
        <q-tooltip v-if="contactHint(row)" :delay="300">{{ contactHint(row) }}</q-tooltip>
      </span>
      <span v-if="relationLabel(row.relation)" class="rcc__relation">({{ relationLabel(row.relation) }})</span>

      <q-space />

      <!-- What this related client is referred to by — the request it produced, or the derived
           REMS-1042-C1. -->
      <router-link
        v-if="row.reference && row.createdRemsId"
        class="rcc__ref rcc__ref--link"
        :to="{ name: 'rems_request', params: { id: row.createdRemsId } }"
      >
        {{ row.reference }}
        <q-tooltip>Open the request raised for {{ row.name }}</q-tooltip>
      </router-link>
      <span v-else-if="row.reference" class="rcc__ref">{{ row.reference }}</span>

      <related-status-select
        :model-value="row.status"
        :option="statusOption(row.status)"
        :options="statusOptions"
        :saving="savingKey === `${row.kind}:${row.id}`"
        @update:model-value="$emit('set-status', row, $event)"
      />
    </div>

    <!-- For an individual this is reachable when everybody declared files on the client's own return, which
         the header above has already said. -->
    <div v-if="!rows.length" class="rcc__empty">
      <q-icon name="o_info" size="14px" class="q-mr-xs" />
      {{ isIndividual
        ? "Nothing to set up separately — everyone declared is on this return."
        : "No other entities to set up separately." }}
    </div>
  </div>
</template>

<script setup>
// The nested cell on Related Entities: a client and the clients they brought with them.
import { computed } from "vue";
import AppNameWithSuffix from "components/common/AppNameWithSuffix.vue";
import RelatedStatusSelect from "modules/rems/components/RelatedStatusSelect.vue";
import { INDIVIDUAL_TYPES } from "modules/rems/useRemsIntakeForm";
import { isIndividualEntityType } from "modules/rems/useRemsMeta";

const props = defineProps({
  // The request's entity type (a REMS.EntityType code). It decides which of the two readings above
  // applies, because it is what decided which question the client was asked in the first place.
  entityType: { type: String, default: "" },
  // { name, suffix, jointWith: { name, relation } | null } — the client the request was raised for.
  // Read only in the individual reading; a company's panel does not name its client again.
  parent: { type: Object, required: true },
  // [{ kind, id, name, relation, email, phoneNumber, status, reference, createdRemsId }]
  rows: { type: Array, default: () => [] },
  // The tenant's REMS.RelatedEntityStatus list, and the resolver for one code — both from useRemsMeta, so
  // the badge here says exactly what the same value says everywhere else.
  statusOptions: { type: Array, default: () => [] },
  statusOption: { type: Function, required: true },
  // "kind:id" of the row currently saving, or "" — the page owns the save and says which row it is on.
  savingKey: { type: String, default: "" }
});

defineEmits(["set-status"]);

const isIndividual = computed(() => isIndividualEntityType(props.entityType));

// What they are to the client, read back as the word the client chose.
const relationLabel = (relation) => {
  if (!relation) return "";
  return INDIVIDUAL_TYPES.find((o) => o.value === relation)?.label || relation;
};

const contactHint = (row) => [row.email, row.phoneNumber].filter(Boolean).join(" · ");
</script>

<style scoped>
/* A panel rather than loose lines: the cell holds a table, and without a border its rows run into the
   columns either side of it. */
.rcc {
  border: 1px solid #dfe6ee;
  border-radius: 10px;
  overflow: hidden;
  min-width: 360px;
  background: #fff;
}
.rcc__head,
.rcc__child {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 10px;
  font-size: 13px;
}
/* The brand rule along the top is what makes a group read as one block down a long list — the eye finds
   the head of each one without reading a word of it. */
.rcc__head {
  background: linear-gradient(180deg, #f3f7fa 0%, #eef3f8 100%);
  border-bottom: 1px solid #dfe6ee;
  box-shadow: inset 0 3px 0 -1px var(--q-primary);
}
.rcc__child + .rcc__child {
  border-top: 1px solid #eef2f6;
}
.rcc__child:hover {
  background: #f8fafc;
}
/* The tag sits in the same place on every row, so the shape of a group is readable down the column
   without reading any of the names — which means it needs a fixed width, or "ENTITY-1" and "ENTITY-10"
   would shunt the names beside them out of line. */
.rcc__tag {
  font-size: 9.5px;
  font-weight: 700;
  letter-spacing: 0.6px;
  text-transform: uppercase;
  padding: 3px 6px;
  border-radius: 4px;
  flex: 0 0 auto;
  min-width: 66px;
  text-align: center;
}
.rcc__tag--parent {
  background: var(--q-primary);
  color: #fff;
}
.rcc__tag--child {
  background: #eaf0f6;
  color: #4a5b6b;
  border: 1px solid #dbe4ec;
}
/* The entities heading: the same weight as a parent's name, since it is what stands in for one. */
.rcc__head-icon {
  color: var(--q-primary);
  flex: 0 0 auto;
}
.rcc__head-label {
  font-weight: 600;
  font-size: 12px;
  letter-spacing: 0.4px;
  text-transform: uppercase;
  color: #35485c;
}
.rcc__name {
  font-weight: 500;
  color: #1f2933;
}
.rcc__head .rcc__name {
  font-weight: 600;
}
.rcc__relation {
  color: #7b8794;
  font-size: 11.5px;
}
/* The joint filer reads as part of the parent, in the brand colour so it is plainly the same client
   rather than another row that has drifted up into the header. */
.rcc__joint {
  color: var(--q-primary);
  font-size: 12px;
  font-weight: 500;
  display: inline-flex;
  align-items: center;
  gap: 3px;
  min-width: 0;
}
.rcc__joint-plus {
  opacity: 0.7;
}
.rcc__joint-note {
  color: #8895a4;
  font-weight: 400;
}
/* Right of the name and left of the status, set as a monospaced tag: it is an identifier, and identifiers
   are compared character by character. */
.rcc__ref {
  font-family: ui-monospace, "SFMono-Regular", "Consolas", monospace;
  font-size: 10.5px;
  letter-spacing: -0.2px;
  color: #6b7885;
  background: #f1f4f8;
  border: 1px solid #e3e9f0;
  border-radius: 4px;
  padding: 2px 6px;
  white-space: nowrap;
}
.rcc__ref--link {
  color: var(--q-primary);
  background: rgba(31, 100, 120, 0.07);
  border-color: rgba(31, 100, 120, 0.2);
  text-decoration: none;
  font-weight: 600;
}
.rcc__ref--link:hover {
  background: rgba(31, 100, 120, 0.14);
}
.rcc__empty {
  display: flex;
  align-items: center;
  padding: 9px 10px;
  font-size: 12px;
  color: #7b8794;
}
</style>
