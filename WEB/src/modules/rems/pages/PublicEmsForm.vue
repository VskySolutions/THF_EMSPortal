<template>
  <div class="pef">
    <!-- Initial load ---------------------------------------------------------------------------------- -->
    <div v-if="loading" class="row flex-center q-pa-xl">
      <q-spinner color="primary" size="42px" />
    </div>

    <!-- Neutral "not available" — Invalid / Unavailable / a failed load. Discloses NOTHING about the app. -->
    <q-card v-else-if="showUnavailable" flat bordered class="pef-card pef-note">
      <q-card-section class="column flex-center text-center q-py-xl">
        <q-icon name="o_link_off" size="52px" color="grey-6" />
        <div class="text-h6 q-mt-md">This form isn't available</div>
        <div class="text-body2 text-grey-7 q-mt-sm" style="max-width: 460px;">
          The link may be invalid, no longer active, or already completed. If you believe this is a mistake,
          please contact the person who sent you this form.
        </div>
      </q-card-section>
    </q-card>

    <!-- Client cancelled — neutral, non-destructive end state (they can return via the emailed link). -->
    <q-card v-else-if="cancelled" flat bordered class="pef-card pef-note">
      <q-card-section class="column flex-center text-center q-py-xl">
        <q-icon name="o_bookmark_border" size="52px" color="primary" />
        <div class="text-h6 q-mt-md">Your progress is saved</div>
        <div class="text-body2 text-grey-7 q-mt-sm" style="max-width: 460px;">
          You can return to this form at any time using the link in your email and pick up right where you
          left off.
        </div>
      </q-card-section>
    </q-card>

    <!-- Submitted — personalized thank-you. No editable fields, no reset / Start-Over (AC-REMS-012.6/7). -->
    <q-card v-else-if="state === 'Submitted'" flat bordered class="pef-card pef-note">
      <q-card-section class="column flex-center text-center q-py-xl">
        <q-icon name="o_check_circle" size="56px" color="positive" />
        <div class="text-h5 q-mt-md">Thank you{{ thankYouName ? `, ${thankYouName}` : "" }}!</div>
        <div class="text-body1 text-grey-8 q-mt-sm" style="max-width: 480px;">
          Your EMS onboarding form has been submitted successfully. Our team has been notified and will be in
          touch with you shortly.
        </div>
        <div class="text-caption text-grey-6 q-mt-md">You can safely close this window.</div>
      </q-card-section>
    </q-card>

    <!-- Editable ------------------------------------------------------------------------------------- -->
    <template v-else-if="state === 'Editable'">
      <!-- Intro -->
      <!-- The entity type is NOT shown. -->
      <div class="pef-head">
        <div class="row items-center q-gutter-sm">
          <div class="text-h5 text-weight-bold col">EMS Onboarding Form</div>
        </div>
        <div class="text-body2 text-grey-7 q-mt-xs">
          Please review and complete the details below. Your progress saves automatically as you go.
        </div>
      </div>

      <!-- ============================ FORM STEP ============================ -->
      <template v-if="step === 'form'">
        <!-- Server-side validation summary (from review/submit) — surfaced field-by-field below too. -->
        <q-banner v-if="serverSummary.length" dense class="pef-card bg-red-1 text-red-9 q-mb-md rounded-borders">
          <template #avatar><q-icon name="o_error" color="red-9" /></template>
          <div class="text-weight-medium">Please fix the following:</div>
          <ul class="q-my-xs q-pl-md">
            <li v-for="(m, i) in serverSummary" :key="i">{{ m }}</li>
          </ul>
        </q-banner>
        <client-intake-fields
          v-model="payload" :entity-type="entityType" :errors="errors"
          :referral-sources="referralSources"
          @confirm-clear-entities="onConfirmClearEntities"
          @confirm-clear-individuals="onConfirmClearIndividuals"
        />

        <!-- Remaining-required checklist (client-side gate for Review). -->
        <q-card v-if="clientIssues.length" flat bordered class="pef-card pef-todo q-mb-md">
          <q-card-section>
            <div class="row items-center q-gutter-xs text-grey-8">
              <q-icon name="o_checklist" color="primary" />
              <span class="text-weight-medium">Complete these to continue to review:</span>
            </div>
            <ul class="q-my-xs q-pl-lg text-grey-8">
              <li v-for="(m, i) in clientIssues" :key="i">{{ m }}</li>
            </ul>
          </q-card-section>
        </q-card>

        <!-- Action bar -->
        <div class="pef-actionbar">
          <q-btn flat no-caps color="grey-8" icon="o_close" label="Cancel" :disable="busy" @click="onCancel" />
          <div class="pef-save" :class="`pef-save--${saveState}`">
            <q-spinner v-if="saveState === 'saving'" size="16px" color="primary" />
            <q-icon v-else :name="saveIcon" size="18px" />
            <span>{{ saveText }}</span>
          </div>
          <q-btn
            unelevated no-caps color="primary" icon-right="o_arrow_forward" label="Review"
            :disable="!canReview || busy" :loading="reviewing" @click="onReview"
          >
            <q-tooltip v-if="!canReview">Complete the required fields to continue.</q-tooltip>
          </q-btn>
        </div>
      </template>

      <!-- ============================ REVIEW STEP ============================ -->
      <template v-else>
        <q-card flat bordered class="pef-card q-mb-md">
          <q-card-section class="pef-card__head">
            Review your details
            <div class="text-caption text-grey-7 text-weight-regular">
              Please confirm everything is correct before submitting. Nothing is submitted until you confirm.
            </div>
          </q-card-section>
          <q-separator />
          <q-card-section>
            <rems-review-summary
              :payload="reviewPayload" :entity-type="entityType" :locked-email="payload.email"
              :referral-sources="referralSources"
            />
          </q-card-section>
        </q-card>

        <div class="pef-actionbar">
          <q-btn
            flat no-caps color="primary" icon="o_arrow_back" label="Go Back and Edit"
            :disable="busy" @click="goBackToForm"
          />
          <q-space />
          <q-btn
            unelevated no-caps color="positive" icon="o_check" label="Confirm & Submit"
            :loading="submitting" :disable="busy" @click="onSubmit"
          />
        </div>
      </template>
    </template>
  </div>
</template>

<script setup>
// Public, anonymous REMS client EMS form (WO-116, Part B).
import { ref, computed, watch, onMounted, nextTick } from "vue";
import { useRoute } from "vue-router";
import { debounce } from "quasar";
import { remsPublicApi, getApiErrorMessage, getApiErrorCode, ApiErrorCodes } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import {
  blankIntakePayload, buildIntakePayload, intakeClientName, intakeIssues, parseIntakeFieldErrors,
  seedIntakePayload
} from "modules/rems/useRemsIntakeForm";

import ClientIntakeFields from "modules/rems/components/ClientIntakeFields.vue";
import RemsReviewSummary from "modules/rems/components/RemsReviewSummary.vue";

const route = useRoute();
const notify = useNotify();
const { confirm } = useConfirm();
const inviteCode = route.params.inviteCode;

// The referral-source list, as the SERVER sends it with the form — the tenant's own wording, in their own
// order, with the descriptions that become each option's caption.
const referralSources = ref([]);

// ---- Screen state ----
const loading = ref(true);
const loadFailed = ref(false);
const state = ref("");          // "Invalid" | "Unavailable" | "Submitted" | "Editable"
const cancelled = ref(false);
const thankYouName = ref("");
const entityType = ref("");  // lowercase entity-type code, e.g. individual | commercial | government
const step = ref("form");       // "form" | "review"

// ---- Save / validation state ----
const ready = ref(false);       // becomes true once the initial seed completes (gates autosave)
const reviewing = ref(false);
const submitting = ref(false);
const errors = ref({});         // per-field server messages, keyed by payload path (e.g. "roles.self.email")
const serverSummary = ref([]);  // flat list of server messages for the top banner
const saveState = ref("idle");  // "idle" | "saving" | "saved" | "error"

// The editable payload the field set writes through. `ref` rather than the bare reactive object because
// ClientIntakeFields takes it as a defineModel.
const payload = ref(blankIntakePayload());

// ---- Derived ----
const showUnavailable = computed(() => loadFailed.value || state.value === "Invalid" || state.value === "Unavailable");
const busy = computed(() => reviewing.value || submitting.value);

// ---- Save indicator ----
const saveIcon = computed(() => ({
  saved: "o_cloud_done", error: "o_cloud_off", idle: "o_cloud_queue"
}[saveState.value] || "o_cloud_queue"));
const saveText = computed(() => ({
  saving: "Saving…",
  saved: "Saved just now",
  error: "Couldn't save — we'll retry as you edit"
}[saveState.value] || "Your progress saves automatically"));

// What still has to be filled in before Review. Mirrors RemsFormPayloadValidator — see useRemsIntakeForm.
const clientIssues = computed(() => intakeIssues(payload.value, entityType.value));
const canReview = computed(() => clientIssues.value.length === 0);

const buildPayload = () => buildIntakePayload(payload.value, entityType.value);

// The review step shows the payload exactly as it will be submitted (wire shape, addresses converted).
const reviewPayload = computed(() => buildPayload());

// ---- Seeding (prefill + draft) ----
function seed (prefill, draft) {
  ready.value = false;
  seedIntakePayload(payload.value, draft, prefill);
  // Let the reactive writes flush before re-arming autosave, so seeding never triggers a save.
  nextTick(() => { ready.value = true; });
}

// Turning the Other Entities toggle off throws away what has been typed, so the field set hands the
// decision back here and applies it only if the client says yes.
async function onConfirmClearEntities (applyClear) {
  const ok = await confirm({
    title: "Remove other entities?",
    message: "This will remove every other entity you've added to this form.",
    confirmLabel: "Remove",
    type: "danger"
  });
  if (ok) applyClear();
}

// The same bargain for the spouse / children card: answering "No" throws away everyone added to it.
async function onConfirmClearIndividuals (applyClear) {
  const ok = await confirm({
    title: "Remove these people?",
    message: "This will remove everyone you've added to this form.",
    confirmLabel: "Remove",
    type: "danger"
  });
  if (ok) applyClear();
}

// ---- Load ----
function applySubmitted (res) {
  state.value = "Submitted";
  thankYouName.value = res?.clientName || intakeClientName(payload.value) || "";
  step.value = "form";
}

async function load () {
  loading.value = true;
  loadFailed.value = false;
  try {
    const res = await remsPublicApi.load(inviteCode);
    state.value = res?.state || "Invalid";
    if (res?.state === "Submitted") {
      thankYouName.value = res.clientName || "";
    } else if (res?.state === "Editable") {
      entityType.value = String(res.entityType || "").toLowerCase();
      // The tenant's own REMS.ReferralSource list, resolved server-side because this page has no
      // session to resolve it with. Absent (an emptied or missing list) leaves the built-in copy.
      if (res.referralSources?.length) referralSources.value = res.referralSources;
      seed(res.prefill, res.draftPayload);
    }
  } catch {
    loadFailed.value = true;
  } finally {
    loading.value = false;
  }
}

// ---- Autosave (debounced) ----
async function doSave () {
  if (!ready.value || state.value !== "Editable") return;
  saveState.value = "saving";
  try {
    const res = await remsPublicApi.saveDraft(inviteCode, buildPayload());
    saveState.value = "saved";
    return res;
  } catch {
    saveState.value = "error";
  }
}
const scheduleSave = debounce(doSave, 1000);

watch(payload, () => {
  // Any edit clears stale server highlights…
  if (Object.keys(errors.value).length || serverSummary.value.length) {
    errors.value = {};
    serverSummary.value = [];
  }
  // …and (once seeded, while editable) schedules a durable autosave.
  if (ready.value && state.value === "Editable") {
    saveState.value = "saving";
    scheduleSave();
  }
}, { deep: true });

// ---- Server validation → per-field + summary ----
function applyServerValidation (err) {
  const parsed = parseIntakeFieldErrors(err);
  errors.value = parsed.fields;
  serverSummary.value = parsed.summary;
}

// Non-validation failures (409 not-editable / 404 / network) → resync the load state so the client sees
// the real terminal state (e.g. the admin cancelled, or it was already submitted in another tab).
async function handleActionError (err, fallback) {
  if (getApiErrorCode(err) === ApiErrorCodes.ValidationFailed) {
    applyServerValidation(err);
    step.value = "form";
    notify.error("Please fix the highlighted fields.");
    scrollTop();
    return;
  }
  notify.error(getApiErrorMessage(err, fallback));
  await load();
}

const scrollTop = () => { try { window.scrollTo({ top: 0, behavior: "smooth" }); } catch { /* noop */ } };

// ---- Review / Submit / Cancel ----
async function onReview () {
  if (!canReview.value) return;
  reviewing.value = true;
  errors.value = {};
  serverSummary.value = [];
  try {
    // Persist the latest before validating server-side (belt-and-suspenders with autosave).
    await doSave();
    await remsPublicApi.review(inviteCode, buildPayload());
    step.value = "review";
    scrollTop();
  } catch (err) {
    await handleActionError(err, "We couldn't validate your form. Please try again.");
  } finally {
    reviewing.value = false;
  }
}

function goBackToForm () {
  step.value = "form";
  scrollTop();
}

async function onSubmit () {
  submitting.value = true;
  errors.value = {};
  serverSummary.value = [];
  try {
    const res = await remsPublicApi.submit(inviteCode, buildPayload());
    applySubmitted(res);
    scrollTop();
  } catch (err) {
    await handleActionError(err, "We couldn't submit your form. Please try again.");
  } finally {
    submitting.value = false;
  }
}

async function onCancel () {
  const ok = await confirm({
    title: "Leave this form?",
    message: "Your progress is saved automatically. You can return anytime using the link in your email.",
    confirmLabel: "Leave form",
    cancelLabel: "Keep editing"
  });
  if (!ok) return;
  try { await remsPublicApi.cancel(inviteCode); } catch { /* non-destructive — ignore */ }
  cancelled.value = true;
  scrollTop();
}

onMounted(load);
</script>

<style scoped>
.pef {
  padding-bottom: 12px;
}
/* Trimmed with the cards below it: the intro is two lines, and 16px under it on a form this long is a
   gap the client scrolls past rather than reads. */
.pef-head {
  margin-bottom: 10px;
}
.pef-card {
  border-radius: 12px;
}
.pef-card__head {
  font-size: 15px;
  font-weight: 600;
  color: var(--q-primary);
}
.pef-note {
  margin-top: 32px;
}
/* The sub-heading, address-heading, copy-button and entity-card rules moved to ClientIntakeFields with
   the markup they style — scoped styles do not reach into a child component, and leaving them here would
   have been rules for elements this file no longer renders. */
.pef-todo {
  background: #f7f9fc;
}
.pef-actionbar {
  position: sticky;
  bottom: 0;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  padding: 12px 4px;
  margin-top: 8px;
  background: #f4f6fb;
  border-top: 1px solid #e0e6ed;
}
.pef-save {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12.5px;
  color: #7a8699;
  margin-left: auto;
  margin-right: auto;
}
.pef-save--saved {
  color: var(--q-positive);
}
.pef-save--error {
  color: var(--q-negative);
}

/* The client fills this in on a phone as often as not. Below sm the three items across the bar do not
   fit on one line, so the save state takes the line above and the two buttons split the one below,
   each wide enough to be a thumb target rather than a 60px stub. */
@media (max-width: 599px) {
  .pef-actionbar {
    gap: 8px 10px;
    padding: 10px 0;
  }
  .pef-save {
    order: -1;
    width: 100%;
    margin: 0;
    justify-content: center;
  }
  /* Splits the row evenly between them; the spacer that separates the pair on a desktop would
     otherwise claim a third of the line for itself. */
  .pef-actionbar > .q-btn {
    flex: 1 1 0;
  }
  .pef-actionbar > .q-space {
    display: none;
  }
}
</style>
