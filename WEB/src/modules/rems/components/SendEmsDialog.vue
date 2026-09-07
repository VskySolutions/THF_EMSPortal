<template>
  <q-dialog v-model="open" persistent>
    <q-card class="send-dialog">
      <q-card-section class="row items-center no-wrap">
        <div>
          <div class="text-h6">{{ phase === "sent" ? copy.sentTitle : copy.title }}</div>
          <div v-if="subtitle" class="text-caption text-grey-7">{{ subtitle }}</div>
        </div>
        <q-space />
        <q-btn flat round dense icon="o_close" @click="open = false" />
      </q-card-section>
      <q-separator />

      <!-- Loading the preview -->
      <q-card-section v-if="phase === 'loading'" class="row flex-center q-pa-xl">
        <q-spinner color="primary" size="32px" />
      </q-card-section>

      <!-- The email itself, pre-populated from the tenant's template and editable before it goes
           (AC-REMS-008.1). -->
      <q-card-section v-else-if="phase === 'preview' || phase === 'sending'" class="send-body">
        <app-text-field :model-value="preview.destinationEmail || ''" label="To" readonly>
          <template #append><q-icon name="o_lock" size="18px" color="grey-6" /></template>
        </app-text-field>

        <!-- No client email → sending is blocked (AC-REMS-008.2). -->
        <q-banner v-if="!preview.destinationEmail" dense class="bg-orange-1 text-orange-9 rounded-borders">
          <template #avatar><q-icon name="o_warning" color="orange-9" /></template>
          This client has no email address on file, so nothing can be sent. Add a customer email on the
          request first.
        </q-banner>

        <app-text-field v-model="subject" label="Subject" />

        <!-- Rich text, not a raw-HTML box: the template body IS html, and the admin should be editing the
             email as the client will read it rather than around its markup. -->
        <app-rich-text-field v-model="body" label="Message" />

        <div class="text-caption text-grey-6">
          Pre-filled from your tenant's email template. Edit anything above and the client receives exactly
          that — the template itself is unchanged.
        </div>
      </q-card-section>

      <!-- Error resolving the preview (e.g. form not built yet) -->
      <q-card-section v-else-if="phase === 'error'">
        <q-banner dense class="bg-red-1 text-red-9 rounded-borders">
          <template #avatar><q-icon name="o_error" color="red-9" /></template>
          {{ errorMsg }}
        </q-banner>
      </q-card-section>

      <!-- Sent success state -->
      <q-card-section v-else-if="phase === 'sent'">
        <div class="column flex-center q-py-md text-center">
          <q-icon name="o_mark_email_read" color="positive" size="48px" class="q-mb-sm" />
          <div class="text-subtitle1 q-mb-xs">{{ copy.sentHeadline }}</div>
          <div class="text-grey-7">{{ sentEmail }}</div>
          <div class="text-caption text-grey-6 q-mt-sm">{{ copy.sentNote }}</div>
        </div>
      </q-card-section>

      <!-- The client's own form link — shown once it is THEIRS, never before. -->
      <q-card-section v-if="showLink" class="q-pt-none">
        <div class="send-label">Form link</div>
        <div class="row items-center no-wrap q-gutter-xs">
          <div class="send-value send-link col">{{ formLink }}</div>
          <q-btn flat round dense icon="o_content_copy" color="primary" @click="copyLink">
            <q-tooltip>Copy link</q-tooltip>
          </q-btn>
        </div>
      </q-card-section>

      <q-separator />
      <q-card-actions align="right">
        <template v-if="phase === 'sent'">
          <q-btn unelevated no-caps color="primary" label="Done" @click="open = false" />
        </template>
        <template v-else>
          <q-btn flat no-caps color="grey-8" label="Cancel" @click="open = false" />
          <q-btn
            unelevated no-caps color="primary" :label="copy.action" icon="o_send"
            :loading="phase === 'sending'"
            :disable="!canConfirm"
            @click="confirmSend"
          />
        </template>
      </q-card-actions>
    </q-card>
  </q-dialog>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { copyToClipboard } from "quasar";
import { remsApi, getApiErrorMessage, webUrl } from "services/api";
import { useNotify } from "composables/useNotify";

import AppTextField from "components/common/AppTextField.vue";
import AppRichTextField from "components/common/AppRichTextField.vue";

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  remsId: { type: String, default: null },
  subtitle: { type: String, default: "" },
  // "send" is the first delivery of the form link; "reminder" nudges a client who already has it but has
  // not submitted. Same dialog and same shapes — a different template, endpoint and wording.
  mode: { type: String, default: "send" }
});
// The "View Email Log" hand-off went with the dialog it opened: the email log had no screen left once the
// EMS Inbox and the engagement workspace were replaced by the request page.
const emit = defineEmits(["update:modelValue", "sent"]);

const notify = useNotify();

const open = computed({
  get: () => props.modelValue,
  set: (val) => emit("update:modelValue", val)
});

// loading → preview → sending → sent | error
const phase = ref("loading");
const preview = ref({ destinationEmail: null, formLink: "" });
// The email as the admin will send it — seeded from the rendered template, theirs to change.
const subject = ref("");
const body = ref("");
const errorMsg = ref("");
const sentEmail = ref("");

// Confirm is active only once the preview resolved with a real destination email (AC-REMS-008.2).
const canConfirm = computed(() =>
  phase.value === "preview" && !!preview.value.destinationEmail && !!body.value.trim() && !!subject.value.trim());

// Show the link exactly as it should be opened — absolute, even if the API's App:BaseUrl is unset.
const formLink = computed(() => webUrl(preview.value.formLink));

const isReminder = computed(() => props.mode === "reminder");

// When the link may be looked at: after this send has gone out, or on a reminder, where it went out long
// ago.
const showLink = computed(() => {
  if (!formLink.value) return false;
  if (phase.value === "sent") return true;
  return isReminder.value && (phase.value === "preview" || phase.value === "sending");
});

// Wording that has to differ between the two flows, in one place rather than five ternaries in the
// template. The reminder is repeatable and changes nothing, so its copy never mentions locking.
const copy = computed(() => (isReminder.value
  ? {
    title: "Send Reminder",
    sentTitle: "Reminder sent",
    action: "Send Reminder",
    sentHeadline: "The reminder was emailed to the client.",
    sentNote: "Nothing else changed — the form link and its status are exactly as they were. You can " +
      "send another reminder whenever you need to."
  }
  : {
    title: "Send EMS Form",
    sentTitle: "Form sent",
    action: "Confirm & Send",
    sentHeadline: "The EMS Form link was emailed to the client.",
    sentNote: "The industry group and link are now locked. Delivery and open status will appear in the " +
      "Email Log as the provider reports them."
  }));

const loadPreview = async () => {
  phase.value = "loading";
  errorMsg.value = "";
  try {
    const p = isReminder.value
      ? await remsApi.previewReminder(props.remsId)
      : await remsApi.previewForm(props.remsId);
    preview.value = { destinationEmail: p?.destinationEmail || null, formLink: p?.formLink || "" };
    // Seeded from the template rendered server-side with this request's values. Empty when the tenant
    // has no effective template — the same reason the send itself would deliver nothing.
    subject.value = p?.subject || "";
    body.value = p?.body || "";
    phase.value = "preview";
  } catch (err) {
    errorMsg.value = getApiErrorMessage(err);
    phase.value = "error";
  }
};

const confirmSend = async () => {
  if (!canConfirm.value) return;
  phase.value = "sending";
  try {
    // What the admin is looking at is what gets sent — edited or not. The server still resolves the
    // template (it is what decides whether there is anything to send at all); these just win over it.
    const payload = { subject: subject.value, body: body.value };
    if (isReminder.value) await remsApi.sendReminder(props.remsId, payload);
    else await remsApi.sendForm(props.remsId, payload);
    sentEmail.value = preview.value.destinationEmail;
    phase.value = "sent";
    notify.success(isReminder.value ? "Reminder sent to the client." : "EMS Form link sent to the client.");
    emit("sent");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
    phase.value = "preview";
  }
};

const copyLink = () => {
  if (!formLink.value) return;
  copyToClipboard(formLink.value)
    .then(() => notify.success("Link copied."))
    .catch(() => notify.error("Could not copy the link."));
};

watch(() => props.modelValue, (isOpen) => {
  if (isOpen && props.remsId) loadPreview();
});
</script>

<style scoped>
/* This dialog is not a confirmation — it is the email itself, edited before it goes, and the body is a
   rich-text editor holding the tenant's template. At the 620px a dialog usually gets, that template
   wrapped into a narrow ribbon that read nothing like what the client receives. So it takes 70% of the
   window, which needs `max-width` as well as `width`: Quasar caps a minimized dialog at 560px, and a
   width alone loses to it. Below a laptop the percentage stops helping and it goes near-full-width. */
.send-dialog {
  width: 70vw;
  max-width: 70vw;
}
@media (max-width: 1023px) {
  .send-dialog {
    width: 92vw;
    max-width: 92vw;
  }
}

/* The email fields stack with even spacing; the rich-text editor needs the room, and a long template
   body should scroll inside the dialog rather than pushing the actions off-screen. */
.send-body {
  display: flex;
  flex-direction: column;
  gap: 14px;
  max-height: 68vh;
  overflow-y: auto;
}
.send-label {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.03em;
  text-transform: uppercase;
  color: var(--q-primary);
  margin-bottom: 2px;
}
.send-value {
  font-size: 14px;
  color: #2c3540;
  word-break: break-word;
}
.send-link {
  font-family: monospace;
  background: #f5f7fa;
  border: 1px solid #e0e6ed;
  border-radius: 6px;
  padding: 6px 8px;
}
</style>
