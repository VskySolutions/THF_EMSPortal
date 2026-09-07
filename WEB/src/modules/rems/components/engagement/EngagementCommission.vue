<template>
  <div>
    <!-- One line, and only what the screen cannot show for itself: the rule, and the consequence of being
         on this list. -->
    <div class="text-body2 text-grey-8 q-mb-sm">
      The split must total 100% before the request goes to the client. Each recipient approves the
      engagement.
    </div>

    <!-- Add recipient (searchable CSE-group picker; excludes those already added). -->
    <div v-if="editable" class="row items-center q-col-gutter-md q-mb-md">
      <app-select
        v-model="pick" :options="availableRecipients" label="Add recipient" class="col-12 col-sm"
        use-input :disable="splits.length >= 10" :hint="recipientHint"
        info="Lists users holding the &quot;CSE&quot; role, assigned on a user's page in Administration → Users. Recipients already added are excluded."
        @update:model-value="addRecipient"
      />
      <!-- The cap said as a count, not only as a picker that stops responding. -->
      <div class="col-auto q-pb-sm">
        <div class="rems-commission__count" :class="{ 'rems-commission__count--full': splits.length >= 10 }">
          <span class="rems-commission__count-n">{{ splits.length }}</span>
          <span class="rems-commission__count-of">/ 10</span>
        </div>
      </div>
    </div>

    <div v-if="!splits.length" class="text-grey-6 q-pa-sm">No commission recipients yet.</div>

    <q-list v-else bordered separator class="rounded-borders">
      <q-item v-for="(s, i) in splits" :key="s.employeeId">
        <q-item-section>
          <!-- Truncated rather than allowed to set the row's width: the percentage box and the remove
               button are fixed, so on a narrow screen a long name would push them off the card. -->
          <q-item-label class="text-weight-medium ellipsis">{{ s.name }}</q-item-label>
        </q-item-section>
        <q-item-section side style="min-width: 120px;">
          <app-text-field
            v-model="s.percentage" label="" type="number" :readonly="!editable" :rules="percentRules"
          >
            <template #append><span class="text-grey-7">%</span></template>
          </app-text-field>
        </q-item-section>
        <q-item-section side>
          <q-btn v-if="editable" flat round dense color="negative" icon="o_delete" @click="removeAt(i)">
            <q-tooltip>Remove</q-tooltip>
          </q-btn>
        </q-item-section>
      </q-item>
    </q-list>

    <!-- ONE piece of feedback about the total, not four. -->
    <div class="rems-commission__total q-mt-md" :class="`rems-commission__total--${totalTone}`">
      <q-icon :name="totalIcon" size="16px" class="q-mr-xs" />
      Total allocated: {{ totalPercent }}%<template v-if="totalNote"> — {{ totalNote }}</template>
    </div>
  </div>
</template>

<script setup>
// The engagement commission splits (AC-REMS-016): up to ten recipients holding the CSE role, each with an
// editable percentage (> 0 and ≤ 100), individually removable.
import { ref, computed, watch, nextTick } from "vue";
import { remsApi } from "services/api";
import AppSelect from "components/common/AppSelect.vue";
import AppTextField from "components/common/AppTextField.vue";

const props = defineProps({
  engagement: { type: Object, required: true },
  // Selectable recipients — the holders of the "CSE" role, as [{ label, value }].
  recipientOptions: { type: Array, default: () => [] },
  editable: { type: Boolean, default: true }
});
// The page saves this section for the user, so every change to the splits is announced.
const emit = defineEmits(["change"]);

const buildSplits = (e) => (e.commissionSplits || []).map((s) => ({
  employeeId: s.employee.id,
  name: s.employee.name,
  percentage: s.percentage
}));
const splits = ref(buildSplits(props.engagement));
// Set while the splits are being re-seeded from a fresh engagement view — the server catching this
// component up, not a change to announce.
let syncing = false;
watch(() => props.engagement, (e) => {
  syncing = true;
  splits.value = buildSplits(e);
  nextTick(() => { syncing = false; });
});
// Deep: a percentage is edited in place on an existing row, which is not a new array.
watch(splits, () => { if (!syncing) emit("change"); }, { deep: true });

const pick = ref(null);
const availableRecipients = computed(() =>
  props.recipientOptions.filter((s) => !splits.value.some((x) => x.employeeId === s.value)));

// An empty picker means the group has no members; name it so the fix is obvious rather than the dropdown
// just being blank (mirrors the setup form's executive / billing-manager hints).
const recipientHint = computed(() => (props.recipientOptions.length
  ? ""
  : "Nobody holds the \"CSE\" role — assign it on a user's page in Administration → Users."));

const addRecipient = (value) => {
  if (!value || splits.value.length >= 10) { pick.value = null; return; }
  const option = props.recipientOptions.find((s) => s.value === value);
  if (option && !splits.value.some((x) => x.employeeId === value)) {
    splits.value.push({ employeeId: value, name: option.label, percentage: "" });
  }
  pick.value = null;
};

const removeAt = (i) => { splits.value.splice(i, 1); };

const percentRules = [
  (v) => (v !== "" && v !== null && Number(v) > 0 && Number(v) <= 100) || "Enter 0–100"
];

// Rounded to 2dp before comparing: three 33.33/33.34 splits sum to 100.00000000000001 in binary floating
// point, which would otherwise report a perfectly valid 100% allocation as over the limit.
const round2 = (n) => Math.round(n * 100) / 100;
const totalPercent = computed(() =>
  round2(splits.value.reduce((sum, s) => sum + (Number(s.percentage) || 0), 0)));
const totalOver = computed(() => totalPercent.value > 100);

// The whole of the feedback: how far off the split is, and which way. The number is already on screen, so
// the note says only what the number does not — how much is missing, or how much too much.
const totalNote = computed(() => {
  if (totalOver.value) return `${round2(totalPercent.value - 100)}% over`;
  if (totalPercent.value < 100) return `${round2(100 - totalPercent.value)}% unallocated`;
  return "";
});
const totalTone = computed(() => {
  if (totalOver.value) return "bad";
  return totalPercent.value === 100 ? "ok" : "warn";
});
const totalIcon = computed(() => ({
  ok: "o_check_circle", warn: "o_pending", bad: "o_error"
}[totalTone.value]));

// Called by the page's Save. Sent whenever the engagement has splits or had them a moment ago — an empty
// list is how a recipient is removed, so "nothing to send" is only true when there was nothing before.
const saveCommission = async (engagementId) => {
  const had = (props.engagement.commissionSplits || []).length > 0;
  if (!splits.value.length && !had) return null;

  // Every recipient must carry a valid percentage (> 0 and ≤ 100).
  const invalid = splits.value.some((s) => {
    const n = Number(s.percentage);
    return s.percentage === "" || s.percentage === null || !(n > 0 && n <= 100);
  });
  if (invalid) {
    throw new Error("Every commission recipient needs a percentage between 0 and 100.");
  }
  // The splits divide one commission, so they can never add up to more than the whole of it. The API
  // enforces the same ceiling — this is the readable version of that rejection.
  if (totalOver.value) {
    throw new Error(
      `Commission totals ${totalPercent.value}% — ${round2(totalPercent.value - 100)}% over. ` +
      "Reduce the splits to 100% or less.");
  }

  return remsApi.updateCommission(
    engagementId,
    splits.value.map((s) => ({ employeeId: s.employeeId, percentage: Number(s.percentage) }))
  );
};

defineExpose({ saveCommission });
</script>

<style scoped>
/* The recipient count, beside the picker. Bordered and set in the ink colour because it is the only thing
   on the row that says there is a ceiling at all — as grey caption text it read as decoration, and the
   picker greying out at ten arrived as a bug report instead of as the rule being met. */
.rems-commission__count {
  display: inline-flex;
  align-items: baseline;
  gap: 3px;
  padding: 4px 10px;
  border: 1px solid var(--line);
  border-radius: 999px;
  line-height: 1.2;
  background: #fff;
}
.rems-commission__count-n {
  font-size: 15px;
  font-weight: 700;
  color: var(--ink-900);
}
.rems-commission__count-of {
  font-size: 12px;
  color: var(--ink-500);
}
/* Full. The picker beside it is disabled from here on, and this is what explains it. */
.rems-commission__count--full {
  border-color: #ffb300;
  background: #fff8e1;
}
.rems-commission__count--full .rems-commission__count-n,
.rems-commission__count--full .rems-commission__count-of {
  color: #8a5a00;
}

/* The single line of feedback about the split. Colour carries the state — green once it adds up, amber
   while it is short, red when it is over — which is what lets this one line replace the caption, the
   banner and the toast that all used to say the same number. Amber and not red for short: a split can
   legitimately sit part-allocated while it is still being agreed. */
.rems-commission__total {
  display: inline-flex;
  align-items: center;
  padding: 6px 12px;
  border-radius: var(--radius);
  font-size: 13px;
  font-weight: 500;
}
.rems-commission__total--ok {
  background: #e8f5e9;
  color: #1b5e20;
}
.rems-commission__total--warn {
  background: #fff8e1;
  color: #8a5a00;
}
.rems-commission__total--bad {
  background: #ffebee;
  color: #b71c1c;
}
</style>
