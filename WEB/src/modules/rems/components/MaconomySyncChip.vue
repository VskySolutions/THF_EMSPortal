<template>
  <!-- Where posting the request's client to Maconomy has got to, as one small badge. The same badge in
       the request header, the lists and the settings page's unsynced table, so a status reads the same
       everywhere. -->
  <q-badge :color="look.color" :text-color="look.textColor" class="maconomy-sync-chip">
    <q-spinner v-if="status === 'syncing'" size="11px" class="q-mr-xs" />
    <q-icon v-else :name="look.icon" size="13px" class="q-mr-xs" />
    {{ look.label }}
    <q-tooltip v-if="tooltip" max-width="360px" :delay="300">{{ tooltip }}</q-tooltip>
  </q-badge>
</template>

<script setup>
import { computed } from "vue";
import { useDateFormat } from "composables/useDateFormat";
import { MACONOMY_SYNC_LOOKS as LOOKS } from "modules/rems/maconomySync";

const props = defineProps({
  // The server's RemsMaconomySyncView: { status, customerNumber, syncedOnUtc, failureReason, … }. The
  // lists hand the bare status word instead, so both shapes are read.
  sync: { type: [Object, String], default: null }
});

const fmt = useDateFormat();

const status = computed(() => (typeof props.sync === "string" ? props.sync : props.sync?.status) || "notSynced");
const view = computed(() => (typeof props.sync === "object" && props.sync ? props.sync : {}));

const look = computed(() => {
  const base = LOOKS[status.value] || LOOKS.notSynced;
  // A synced client reads as its number: that is what somebody will type into Maconomy.
  if ((status.value === "synced" || status.value === "linked") && view.value.customerNumber) {
    return { ...base, label: `Maconomy ${view.value.customerNumber}` };
  }
  return base;
});

const tooltip = computed(() => {
  const v = view.value;
  switch (status.value) {
    case "synced":
      return v.syncedOnUtc ? `Created in Maconomy ${fmt.formatDateTime(v.syncedOnUtc)}.` : "Created in Maconomy.";
    case "linked":
      return v.syncedOnUtc
        ? `A Maconomy customer with this client's email already existed; linked ${fmt.formatDateTime(v.syncedOnUtc)}.`
        : "Linked to an existing Maconomy customer with this client's email.";
    case "failed":
      return v.failureReason || "The last attempt failed.";
    case "pending":
      return "Queued for posting to Maconomy.";
    case "syncing":
      return "An attempt is under way.";
    default:
      return v.syncEnabled === false
        ? "Not posted to Maconomy: the customer sync was off when the request was approved."
        : "Not posted to Maconomy yet.";
  }
});
</script>

<style scoped>
.maconomy-sync-chip {
  font-weight: 500;
  white-space: nowrap;
}
</style>
