<template>
  <div>
    <div class="row items-center q-mb-md">
      <div class="text-body2 text-grey-8 col">
        <!-- STATIC-APPROVAL-POLICY: the fixed rules read differently from the platform list. -->
        <template v-if="staticRouting">
          Who this engagement routes to, all at the same time: every commission recipient, the CSE, the
          Department Director, everyone holding the Shareholder role, plus anyone you add below. The
          Department Director and the Shareholders are skipped by the tax rules. Sending for approval locks
          the list.
        </template>
        <template v-else>
          Who this engagement routes to: the firm's shareholders, the Department Director and the CSE from
          the setup, and every commission recipient — all automatically, and none of them removable — plus
          anyone you add below. Sending for approval locks the list.
        </template>
      </div>
      <app-option-badge :option="statusMeta" class="q-pa-sm text-body2" />
    </div>

    <!-- Post-send / decided states. -->
    <q-banner v-if="status === 'PendingApproval'" dense class="bg-orange-1 text-orange-9 rounded-borders q-mb-md">
      <template #avatar><q-icon name="o_hourglass_top" color="orange-9" /></template>
      Sent for approval — awaiting decisions. The approver list is locked.
    </q-banner>
    <q-banner v-else-if="status === 'Approved'" dense class="bg-green-1 text-green-9 rounded-borders q-mb-md">
      <template #avatar><q-icon name="o_verified" color="green-9" /></template>
      This engagement is fully approved.
    </q-banner>
    <q-banner v-else-if="status === 'Rejected'" class="bg-red-1 text-red-9 rounded-borders q-mb-md">
      <template #avatar><q-icon name="o_cancel" color="red-9" /></template>
      <div class="text-weight-medium">This engagement was rejected and returned for rework.</div>
      <div v-if="rejectionReason" class="q-mt-xs" style="white-space: pre-wrap;">Reason: {{ rejectionReason }}</div>
      <div class="q-mt-xs">Update the setup as needed, then resubmit for approval.</div>
    </q-banner>

    <div v-if="loading" class="row flex-center q-pa-lg"><q-spinner color="primary" size="28px" /></div>

    <q-banner v-else-if="errorMsg" dense class="bg-red-1 text-red-9 rounded-borders">
      <template #avatar><q-icon name="o_error" color="red-9" /></template>
      {{ errorMsg }}
    </q-banner>

    <template v-else>
      <!-- Extra approvers only: the automatic ones already route and are shown in the list below. -->
      <div v-if="canPick" class="q-mb-md">
        <app-select
          v-model="picked" :options="approverOptions" label="Add approvers" multiple use-input
          :loading="loadingOptions || savingPicks"
          info="Lists every user in this tenant with the role they hold here: an engagement can need a signature from anyone. They are ADDED to the ones below — the shareholders, the Department Director, the CSE and the commission recipients — who approve regardless and cannot be removed."
          @update:model-value="savePicks"
        />
      </div>

      <q-list v-if="approvers.length" bordered separator class="rounded-borders">
        <q-item v-for="(a, i) in approvers" :key="i">
          <q-item-section avatar>
            <q-icon :name="roleOption(a.role).icon || 'o_person'" color="primary" />
          </q-item-section>
          <q-item-section>
            <q-item-label class="text-weight-medium">{{ a.user.name || "Unassigned" }}</q-item-label>
            <q-item-label caption>{{ roleOption(a.role).label }}</q-item-label>
          </q-item-section>
        </q-item>
      </q-list>
      <div v-else class="text-grey-6 q-pa-sm">
        No approvers yet. The automatic ones come from the firm and from the engagement itself — give
        somebody the Shareholder role, name a CSE, pick a department that has a director, or add
        commission recipients. You can also add approvers above.
      </div>

      <!-- Why the round cannot go out yet, where the button that would send it is. -->
      <q-banner
        v-if="commissionProblem && (canShowSend || canShowResubmit)" dense
        class="rems-approval__warn q-mt-md rounded-borders"
      >
        <template #avatar><q-icon name="o_warning" color="orange-9" /></template>
        {{ commissionProblem }}
      </q-banner>

      <div v-if="canShowSend" class="row justify-end q-mt-md">
        <q-btn
          unelevated no-caps color="primary" icon="o_send" label="Send for Approval"
          :loading="sending" :disable="!canRoute" @click="send"
        >
          <q-tooltip v-if="blockedReason">{{ blockedReason }}</q-tooltip>
        </q-btn>
      </div>

      <!-- Staff resubmission (AC-REMS-020.3): after a rejection, re-route the (regenerated) approver list. -->
      <div v-if="canShowResubmit" class="row justify-end q-mt-md">
        <q-btn
          unelevated no-caps color="primary" icon="o_restart_alt" label="Resubmit for Approval"
          :loading="resubmitting" :disable="!canRoute" @click="resubmit"
        >
          <q-tooltip v-if="blockedReason">{{ blockedReason }}</q-tooltip>
        </q-btn>
      </div>
    </template>
  </div>
</template>

<script setup>
// The Approval tab (AC-REMS-018): shows the live suggested approver list from the engagement, and — for a
// Draft engagement whose holder may send — the Send-for-Approval action.
import { ref, computed, watch, onMounted } from "vue";
import { remsApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useRemsMeta } from "modules/rems/useRemsMeta";
import AppSelect from "components/common/AppSelect.vue";
import AppOptionBadge from "components/common/AppOptionBadge.vue";

const props = defineProps({
  engagement: { type: Object, required: true },
  // Whether the caller holds rems.approvals.send.
  canSend: { type: Boolean, default: false },
  // Whether marketing has been saved (the tab is only reachable then; belt-and-braces here too).
  marketingSaved: { type: Boolean, default: false }
});
const emit = defineEmits(["status-changed"]);

const notify = useNotify();
const { confirm } = useConfirm();
const { engagementStatusOption, approverRoleOption } = useRemsMeta();

// What each approver IS to this engagement, from the REMS.ApproverRole list — the name and the icon are
// the tenant's, maintained in Administration → Option Sets.
const roleOption = (r) => approverRoleOption(r);

const status = computed(() => props.engagement.status);
const statusMeta = computed(() => engagementStatusOption(status.value));
// The rejection reason is shown to staff/CSE when the engagement carries it (AC-REMS-020.2).
const rejectionReason = computed(() => props.engagement.rejectionReason);

// The Send action only appears for a Draft engagement, when the caller may send and marketing is saved.
const canShowSend = computed(() => status.value === "Draft" && props.canSend && props.marketingSaved);
// Resubmission is offered on a Rejected engagement to a caller holding rems.approvals.send (AC-REMS-020.3).
const canShowResubmit = computed(() => status.value === "Rejected" && props.canSend);

const approvers = ref([]);
const loading = ref(false);
const errorMsg = ref("");

// ---- STATIC-APPROVAL-POLICY ----
// Whether the fixed rules apply, who the seats reserve (kept out of the picker), and why the server says
// the round cannot go out yet.
const staticRouting = ref(false);
const reservedIds = ref([]);
const serverBlockedReason = ref("");

// ---- Whether the round can actually go out ----
// The commission splits divide ONE commission, so a set of them that comes to 90% leaves a tenth of it
// allocated to nobody — and every recipient is a required approver.
const round2 = (n) => Math.round(n * 100) / 100;
const commissionTotal = computed(() => round2(
  (props.engagement.commissionSplits || []).reduce((sum, s) => sum + (Number(s.percentage) || 0), 0)));
const commissionProblem = computed(() => {
  // Commission is optional; only a split somebody started has to come to 100%.
  if (!(props.engagement.commissionSplits || []).length) return "";
  if (commissionTotal.value === 100) return "";
  return `Commission totals ${commissionTotal.value}% — the recipients on the Commission tab must add up ` +
    "to 100% before this engagement can be sent for approval.";
});

const blockedReason = computed(() => {
  if (serverBlockedReason.value) return serverBlockedReason.value;
  if (!approvers.value.length) {
    return "There is nobody to route this to yet — name a CSE, pick a department with a director, or add " +
      "approvers above.";
  }
  return commissionProblem.value;
});
const canRoute = computed(() => !blockedReason.value);

// ---- Add approvers ----
// Editable while the engagement is unsent; once routed, the API locks the list too.
const canPick = computed(() => ["Draft", "Rejected"].includes(status.value));
const approverOptions = ref([]);
const loadingOptions = ref(false);
// ONLY the added approvers.
const picked = ref([]);

// Adopt a returned approver list as both the display list and the picker's current state.
const adopt = (list) => {
  approvers.value = list?.approvers || [];
  picked.value = [...(list?.selectedApproverIds || [])];
  staticRouting.value = !!list?.staticRouting;
  reservedIds.value = list?.reservedApproverIds || [];
  serverBlockedReason.value = list?.blockedReason || "";
};

const load = async () => {
  loading.value = true;
  errorMsg.value = "";
  try {
    adopt(await remsApi.approvers(props.engagement.id));
  } catch (err) {
    errorMsg.value = getApiErrorMessage(err);
  } finally {
    loading.value = false;
  }
};

const loadOptions = async () => {
  if (!canPick.value) return;
  loadingOptions.value = true;
  try {
    const rows = await remsApi.approverOptions(props.engagement.id);
    // "Full Name — Role", falling back to the email and then to the name alone.
    approverOptions.value = (rows || [])
      // STATIC-APPROVAL-POLICY: the seats already approve.
      .filter((r) => !reservedIds.value.includes(r.userId))
      .map((r) => {
        const qualifier = (r.roles || []).join(", ") || r.email;
        return { label: qualifier ? `${r.name} — ${qualifier}` : r.name, value: r.userId };
      });
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loadingOptions.value = false;
  }
};

// Saved on selection rather than behind a Save button, so the picked person drops straight into the list
// below and the only action left is Send for Approval.
const savingPicks = ref(false);
const savePicks = async () => {
  savingPicks.value = true;
  try {
    adopt(await remsApi.setApprovers(props.engagement.id, picked.value));
  } catch (err) {
    notify.error(getApiErrorMessage(err));
    await load(); // put the picker back to what actually persisted
  } finally {
    savingPicks.value = false;
  }
};

// The list first: the picker's options are filtered by the seats the list reports.
const reload = async () => { await load(); await loadOptions(); };

onMounted(reload);
watch(() => props.engagement.id, reload);
// Commission recipients are automatic approvers, so editing them changes the list — re-read it. Picks are
// saved on selection, so there is never unsaved state to clobber here.
watch(() => props.engagement.commissionSplits, reload, { deep: true });

const sending = ref(false);
const send = async () => {
  const ok = await confirm({
    title: "Send for approval",
    message: "This routes the engagement to every listed approver and locks the approver list. Continue?",
    confirmLabel: "Send"
  });
  if (!ok) return;
  sending.value = true;
  try {
    const list = await remsApi.sendApproval(props.engagement.id);
    adopt(list);
    emit("status-changed", list?.engagementStatus || "PendingApproval");
    notify.success("Engagement sent for approval.");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    sending.value = false;
  }
};

const resubmitting = ref(false);
const resubmit = async () => {
  const ok = await confirm({
    title: "Resubmit for approval",
    message: "This sends the engagement to the regenerated approver list again and re-notifies every approver. Continue?",
    confirmLabel: "Resubmit"
  });
  if (!ok) return;
  resubmitting.value = true;
  try {
    const list = await remsApi.resubmitApproval(props.engagement.id);
    adopt(list);
    emit("status-changed", list?.engagementStatus || "PendingApproval");
    notify.success("Engagement resubmitted for approval.");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    resubmitting.value = false;
  }
};
</script>

<style scoped>
/* Amber, not red: a round that cannot go out yet is something to finish, not something that has failed.
   The same shade the Commission tab's allocation warning uses, because it is usually the same fact. */
.rems-approval__warn {
  background: #fff8e1;
  color: #8a5a00;
}
</style>
