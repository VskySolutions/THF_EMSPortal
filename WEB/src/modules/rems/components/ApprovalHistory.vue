<template>
  <!-- Nothing at all when a caller excluding its own approval has no OTHER rejection left to show: a
       History card sitting under a list that already carries the rejections is a heading over nothing. -->
  <q-card v-if="!collapsed" flat bordered class="ah">
    <q-card-section class="ah__head row items-center q-py-sm">
      <q-icon name="o_history" size="18px" color="primary" class="q-mr-sm" />
      <div class="text-subtitle2 text-weight-medium">{{ label }}</div>
    </q-card-section>
    <q-separator />

    <div v-if="loading" class="q-pa-md row flex-center"><q-spinner color="primary" size="24px" /></div>
    <div v-else-if="failed" class="q-pa-md text-grey-6">
      This engagement's approval history is not available to you.
    </div>
    <div v-else-if="!rejections.length" class="q-pa-md text-grey-6">
      No rejections recorded.
    </div>
    <q-list v-else separator>
      <q-item v-for="d in rejections" :key="d.taskId">
        <q-item-section>
          <q-item-label class="text-weight-medium">
            {{ d.approver }}
            <span class="text-grey-6">— {{ approverRoleLabel(d.role) }}</span>
          </q-item-label>
          <!-- A rejection has required a reason since AC-REMS-020.1, but an older one may carry none, and a
               row that names an objector and then says nothing is worse than one that admits the gap. -->
          <q-item-label class="ah__reason">{{ d.reason || "No reason recorded." }}</q-item-label>
        </q-item-section>
        <q-item-section v-if="d.decidedOnUtc" side top>
          <span class="text-caption text-grey-6">{{ fmt.formatDateTime(d.decidedOnUtc) }}</span>
        </q-item-section>
      </q-item>
    </q-list>
  </q-card>
</template>

<script setup>
// What the approvers have objected to on an engagement, newest first: who rejected it, and why.
import { ref, computed, watch } from "vue";
import { remsApi } from "services/api";
import { useDateFormat } from "composables/useDateFormat";
import { useRemsMeta } from "modules/rems/useRemsMeta";

const props = defineProps({
  engagementId: { type: String, default: null },
  // An approval the CALLER already renders in full, left out here rather than repeated directly beneath
  // itself.
  excludeRoundId: { type: String, default: null },
  label: { type: String, default: "Rejection History" }
});

const fmt = useDateFormat();
// The role as the rest of REMS words it. The raw enum name reached this panel alone — "DepartmentDirector"
// beside "Department Director" everywhere else.
const { approverRoleLabel } = useRemsMeta();
const rounds = ref([]);
const loading = ref(false);
// Told apart from an empty history on purpose.
const failed = ref(false);

// Every rejection on the engagement, in the server's order — which leads with the most recent sending,
// so the objections still waiting to be answered are the ones at the top.
const rejections = computed(() => {
  const shown = props.excludeRoundId
    ? rounds.value.filter((r) => r.roundId !== props.excludeRoundId)
    : rounds.value;
  return shown.flatMap((r) => (r.decisions || []).filter((d) => d.status === "Rejected"));
});

// A card with an exclusion and nothing left over has nothing to say — the objections it would have listed
// are the ones already on screen above it.
const collapsed = computed(() =>
  !!props.excludeRoundId && !loading.value && !failed.value && rejections.value.length === 0);

const load = async () => {
  failed.value = false;
  if (!props.engagementId) {
    rounds.value = [];
    return;
  }
  loading.value = true;
  try {
    rounds.value = (await remsApi.approvalHistory(props.engagementId)) || [];
  } catch {
    // A history that will not load is not worth an error banner over the page it decorates; the card
    // says so in its own body instead.
    rounds.value = [];
    failed.value = true;
  } finally {
    loading.value = false;
  }
};

watch(() => props.engagementId, load, { immediate: true });
</script>

<style scoped>
.ah { border-radius: 10px; }
.ah__head { background: var(--teal-050); }
/* The reason is the row's content, not a footnote to the name above it — same size as the name, in the
   red that marks an objection everywhere else in REMS, and wrapped as the approver typed it. */
.ah__reason {
  font-size: 14px;
  color: #8a1c12;
  white-space: pre-wrap;
  margin-top: 2px;
}
</style>
