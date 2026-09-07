<template>
  <q-dialog v-model="open" persistent :maximized="maximized" @show="load">
    <q-card class="esf">
      <!-- Head: what is being corrected, and the way out. -->
      <q-card-section class="esf__head row items-center no-wrap q-gutter-sm">
        <q-icon name="o_edit_document" size="22px" color="primary" />
        <div class="col">
          <div class="text-subtitle1 text-weight-medium">Edit Client Form</div>
          <div class="text-caption text-grey-7">{{ subtitle }}</div>
        </div>
        <q-btn flat round dense icon="o_close" color="grey-8" aria-label="Close" @click="onClose" />
      </q-card-section>
      <q-separator />

      <q-card-section class="esf__body">
        <div v-if="loading" class="row flex-center q-pa-xl"><q-spinner color="primary" size="36px" /></div>

        <q-banner v-else-if="loadError" dense class="bg-red-1 text-red-9 rounded-borders">
          <template #avatar><q-icon name="o_error" color="red-9" /></template>
          {{ loadError }}
        </q-banner>

        <template v-else>
          <!-- What this dialog IS, said once. -->
          <q-banner dense class="esf__note q-mb-md rounded-borders">
            <template #avatar><q-icon name="o_info" color="primary" /></template>
            These are the client's own answers. Corrections replace them and are recorded against your
            name — the client is not asked again and is not notified.
          </q-banner>

          <!-- Server-side validation summary — surfaced field-by-field below too. -->
          <q-banner v-if="serverSummary.length" dense class="bg-red-1 text-red-9 q-mb-md rounded-borders">
            <template #avatar><q-icon name="o_error" color="red-9" /></template>
            <div class="text-weight-medium">Please fix the following:</div>
            <ul class="q-my-xs q-pl-md">
              <li v-for="(m, i) in serverSummary" :key="i">{{ m }}</li>
            </ul>
          </q-banner>

          <client-intake-fields
            v-model="payload" :entity-type="entityType" :errors="errors"
            :referral-sources="referralSourceOptions"
            email-hint="Locked — the intake form was sent to this address"
            @confirm-clear-entities="onConfirmClearEntities"
            @confirm-clear-individuals="onConfirmClearIndividuals"
          />

          <!-- The same completeness gate the client's own Review button uses. -->
          <q-card v-if="issues.length" flat bordered class="esf__todo q-mb-md">
            <q-card-section>
              <div class="row items-center q-gutter-xs text-grey-8">
                <q-icon name="o_checklist" color="primary" />
                <span class="text-weight-medium">Complete these before saving:</span>
              </div>
              <ul class="q-my-xs q-pl-lg text-grey-8">
                <li v-for="(m, i) in issues" :key="i">{{ m }}</li>
              </ul>
            </q-card-section>
          </q-card>
        </template>
      </q-card-section>

      <q-separator />
      <q-card-actions align="right" class="esf__actions">
        <q-btn flat no-caps color="grey-8" label="Cancel" :disable="saving" @click="onClose" />
        <q-btn
          unelevated no-caps color="primary" icon="o_save" label="Save corrections"
          :disable="!!loadError || loading || !!issues.length" :loading="saving" @click="save"
        >
          <q-tooltip v-if="issues.length">Complete the required fields first.</q-tooltip>
        </q-btn>
      </q-card-actions>
    </q-card>
  </q-dialog>
</template>

<script setup>
// The Admin's correction of a client's submitted intake form (Phase 16).
import { ref, computed } from "vue";
import { useQuasar } from "quasar";
import { remsApi, getApiErrorMessage, getApiErrorCode, ApiErrorCodes } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useRemsMeta } from "modules/rems/useRemsMeta";
import {
  blankIntakePayload, buildIntakePayload, intakeIssues, parseIntakeFieldErrors, seedIntakePayload
} from "modules/rems/useRemsIntakeForm";
import ClientIntakeFields from "modules/rems/components/ClientIntakeFields.vue";

const open = defineModel({ type: Boolean, default: false });

const props = defineProps({
  remsId: { type: String, required: true },
  // "REMS-000123 — Acme Holdings", for the head. The page has it already.
  subtitle: { type: String, default: "" }
});

// `saved` carries the refreshed view, so the page can reload the panel behind this one without asking
// the server a second time for what it has just been handed.
const emit = defineEmits(["saved"]);

// Below md an inset dialog leaves too little room for the address blocks, so it takes the whole screen.
const quasar = useQuasar();
const maximized = computed(() => quasar.screen.lt.md);

const notify = useNotify();
const { confirm } = useConfirm();
// The tenant's own Referral Source wording. Resolvable here, unlike on the client's anonymous page,
// where the list has to travel with the form.
const { referralSourceOptions } = useRemsMeta();

const loading = ref(false);
const saving = ref(false);
const loadError = ref("");
const entityType = ref("");
const errors = ref({});
const serverSummary = ref([]);
const payload = ref(blankIntakePayload());
// What was loaded, so Cancel can tell "nothing typed" from "typed and then thought better of it".
let baseline = "";

const issues = computed(() => (loading.value || loadError.value
  ? []
  : intakeIssues(payload.value, entityType.value)));

const snapshot = () => JSON.stringify(buildIntakePayload(payload.value, entityType.value));
const dirty = () => snapshot() !== baseline;

async function load () {
  loading.value = true;
  loadError.value = "";
  errors.value = {};
  serverSummary.value = [];
  try {
    const view = await remsApi.submission(props.remsId);
    entityType.value = String(view?.entityType || "").toLowerCase();
    payload.value = blankIntakePayload();
    // The locked email is the request's, not the payload's echo of it — the same rule the server applies
    // on submit and again on save.
    seedIntakePayload(payload.value, view?.payload, { email: view?.lockedEmail });
    baseline = snapshot();
  } catch (err) {
    loadError.value = getApiErrorMessage(err, "We couldn't open this client's form.");
  } finally {
    loading.value = false;
  }
}

async function onClose () {
  if (saving.value) return;
  if (dirty() && !(await confirm({
    title: "Discard these corrections?",
    message: "The changes you have made to the client's answers will not be saved.",
    confirmLabel: "Discard",
    cancelLabel: "Keep editing",
    type: "danger"
  }))) {
    return;
  }
  open.value = false;
}

// Turning the Other Entities toggle off throws away what has been typed, so the field set hands the
// decision back here and applies it only on a yes.
async function onConfirmClearEntities (applyClear) {
  const ok = await confirm({
    title: "Remove other entities?",
    message: "This will remove every other entity recorded on this form.",
    confirmLabel: "Remove",
    type: "danger"
  });
  if (ok) applyClear();
}

// The same bargain for the spouse / children card: answering "No" throws away everyone recorded on it.
async function onConfirmClearIndividuals (applyClear) {
  const ok = await confirm({
    title: "Remove these people?",
    message: "This will remove every additional individual recorded on this form.",
    confirmLabel: "Remove",
    type: "danger"
  });
  if (ok) applyClear();
}

async function save () {
  if (issues.value.length) return;
  saving.value = true;
  errors.value = {};
  serverSummary.value = [];
  try {
    const view = await remsApi.updateSubmission(
      props.remsId, buildIntakePayload(payload.value, entityType.value));
    notify.success("The client's form has been updated.");
    emit("saved", view);
    open.value = false;
  } catch (err) {
    if (getApiErrorCode(err) === ApiErrorCodes.ValidationFailed) {
      const parsed = parseIntakeFieldErrors(err);
      errors.value = parsed.fields;
      serverSummary.value = parsed.summary;
      notify.error("Please fix the highlighted fields.");
    } else {
      // A 409 (the round is open) or a 403 (not an admin any more) — the wording is the server's, and
      // the dialog stays open so nothing typed is lost.
      notify.error(getApiErrorMessage(err, "We couldn't save these corrections."));
    }
  } finally {
    saving.value = false;
  }
}
</script>

<style scoped>
/* Effectively full-screen, but centred and inset, so the form has the width its four-across rows want
   without losing the page behind it entirely. Below md the dialog maximizes instead — see `maximized`. */
.esf {
  width: 96vw;
  max-width: 1100px;
  height: 92vh;
  display: flex;
  flex-direction: column;
}
.esf__head {
  padding: 12px 16px;
}
/* The one part that scrolls. The head and the actions stay put, so Save is never a scroll away on a form
   this long. */
.esf__body {
  flex: 1 1 auto;
  overflow-y: auto;
  background: #f4f6fb;
  /* Less than a card section's default, to match the density of the field-set inside it — this pane is
     the same long form the client fills in, and it scrolls inside a fixed-height dialog. */
  padding: 12px;
}
.esf__note {
  background: #eef3fb;
  color: #2c3540;
}
.esf__todo {
  background: #fff;
  border-radius: 12px;
}
.esf__actions {
  padding: 10px 16px;
}
</style>
