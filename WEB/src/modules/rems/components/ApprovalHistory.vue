<template>
  <!-- Nothing at all when a caller excluding its own approval has no OTHER rejection left to show: a
       History card sitting under a list that already carries the rejections is a heading over nothing. -->
  <q-card v-if="!collapsed" flat bordered class="ah">
    <q-card-section class="ah__head row items-center q-py-sm">
      <q-icon name="o_history" size="18px" color="primary" class="q-mr-sm" />
      <div class="text-subtitle2 text-weight-medium">{{ title }}</div>
    </q-card-section>
    <q-separator />

    <div v-if="loading" class="q-pa-md row flex-center"><q-spinner color="primary" size="24px" /></div>
    <div v-else-if="failed" class="q-pa-md text-grey-6">
      This engagement's approval history is not available to you.
    </div>

    <!-- Every round, newest first, with every signature on it: the read-only page's account of the
         approval. -->
    <template v-else-if="allRounds">
      <div v-if="!rounds.length" class="q-pa-md text-grey-6">Not sent for approval yet.</div>
      <div v-for="(r, i) in rounds" :key="r.roundId">
        <q-separator v-if="i > 0" />
        <div class="ah__round-head">
          <span class="text-weight-medium">Round {{ r.roundNumber }}</span>
          <app-option-badge :option="roundStatusOption(r.status, approvedOf(r), r.decisions.length)" />
          <span class="text-caption text-grey-7">{{ sentLine(r) }}</span>
        </div>
        <q-list separator>
          <q-item v-for="d in r.decisions" :key="d.taskId">
            <q-item-section avatar>
              <!-- The icon on the ROLE's own option — the tenant's, like its name. -->
              <q-icon
                :name="approverRoleOption(d.role).icon || 'o_person'"
                :color="awaiting(r, d) ? 'amber-9' : 'primary'"
              />
            </q-item-section>
            <q-item-section>
              <q-item-label class="text-weight-medium">{{ d.approver }}</q-item-label>
              <q-item-label caption>
                {{ approverRoleLabel(d.role) }}
                <template v-if="d.decidedOnUtc"> · {{ fmt.formatDateTime(d.decidedOnUtc) }}</template>
                <template v-if="d.checklistTotal"> · checklist {{ d.checklistCompleted }} of {{ d.checklistTotal }}</template>
              </q-item-label>
              <q-item-label v-if="d.reason" class="ah__reason">{{ d.reason }}</q-item-label>
            </q-item-section>
            <q-item-section side>
              <app-option-badge :option="approvalStatusOption(d.status)" />
            </q-item-section>
          </q-item>
        </q-list>
      </div>
    </template>

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
// What the approvers have objected to on an engagement, newest first: who rejected it, and why — or, in
// "rounds" mode, every round with every decision on it.
import { ref, computed, watch } from "vue";
import { remsApi } from "services/api";
import { useDateFormat } from "composables/useDateFormat";
import { useRemsMeta } from "modules/rems/useRemsMeta";
import AppOptionBadge from "components/common/AppOptionBadge.vue";

const props = defineProps({
  engagementId: { type: String, default: null },
  // An approval the CALLER already renders in full, left out here rather than repeated directly beneath
  // itself.
  excludeRoundId: { type: String, default: null },
  // "rejections" (the default): the objections alone, for the pages that carry the live round themselves.
  // "rounds": every round with every decision on it, for the read-only request page.
  mode: { type: String, default: "rejections" },
  label: { type: String, default: "" }
});

const fmt = useDateFormat();
// The role as the rest of REMS words it. The raw enum name reached this panel alone — "DepartmentDirector"
// beside "Department Director" everywhere else.
const { approverRoleLabel, approverRoleOption, approvalStatusOption, roundStatusOption } = useRemsMeta();
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
  !allRounds.value && !!props.excludeRoundId && !loading.value && !failed.value && rejections.value.length === 0);

// ---- Every round ----
const allRounds = computed(() => props.mode === "rounds");
const title = computed(() => props.label || (allRounds.value ? "Approval Rounds" : "Rejection History"));
const approvedOf = (r) => (r.decisions || []).filter((d) => d.status === "Approved").length;
// A signature the round is still waiting on. A rejection ENDS the round, so the undecided tasks on a
// closed one are not awaiting anything.
const awaiting = (r, d) => r.status === "Pending" && d.status === "Pending";
const sentLine = (r) => {
  const sent = `Sent${r.sentBy ? ` by ${r.sentBy}` : ""} on ${fmt.formatDateTime(r.sentOnUtc)}`;
  return r.completedOnUtc ? `${sent} · closed ${fmt.formatDateTime(r.completedOnUtc)}` : sent;
};

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
/* One round's heading: its number, where it stands, and when it went out. */
.ah__round-head {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 4px 10px;
  padding: 10px 16px 6px;
}
/* The reason is the row's content, not a footnote to the name above it — same size as the name, in the
   red that marks an objection everywhere else in REMS, and wrapped as the approver typed it. */
.ah__reason {
  font-size: 14px;
  color: #8a1c12;
  white-space: pre-wrap;
  margin-top: 2px;
}
</style>
