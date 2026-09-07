<template>
  <q-dialog v-model="open">
    <q-card style="width: 560px; max-width: 92vw;">
      <q-card-section class="row items-center no-wrap">
        <div>
          <div class="text-h6">Email Log</div>
          <div v-if="subtitle" class="text-caption text-grey-7">{{ subtitle }}</div>
        </div>
        <q-space />
        <q-btn flat round dense icon="o_refresh" :loading="loading" @click="load">
          <q-tooltip>Refresh</q-tooltip>
        </q-btn>
        <q-btn flat round dense icon="o_close" @click="open = false" />
      </q-card-section>
      <q-separator />

      <q-card-section style="max-height: 62vh; overflow: auto;">
        <div v-if="loading" class="row flex-center q-pa-lg"><q-spinner color="primary" size="32px" /></div>

        <!-- What actually happened to each email: what we sent (Sent / Reminder), and what the provider
             reported back about it (Delivered / Opened / Failed). -->
        <q-list v-else-if="events.length" separator>
          <q-item v-for="ev in events" :key="ev.id">
            <q-item-section avatar>
              <q-icon
                :name="emailEventOption(ev.eventType).icon || 'o_mail'"
                :style="{ color: emailEventOption(ev.eventType).backgroundColor || '#9e9e9e' }"
                size="24px"
              />
            </q-item-section>
            <q-item-section>
              <q-item-label>
                <app-option-badge :option="emailEventOption(ev.eventType)" />
                <span class="q-ml-sm text-grey-8">{{ ev.recipientEmail || "—" }}</span>
              </q-item-label>
              <q-item-label caption>
                {{ fmt.formatDateTime(ev.occurredOnUtc) }}
                <!-- Who chased the client, on the rows where somebody did. -->
                <template v-if="ev.sentBy"> · by {{ ev.sentBy }}</template>
              </q-item-label>
              <!-- The subject line stood in for by a transport id here before. -->
              <q-item-label v-if="ev.subject" caption class="event-subject">
                {{ ev.subject }}
              </q-item-label>
              <!-- Why a Failed event failed. -->
              <q-item-label v-if="ev.detail" caption class="text-negative event-detail">
                {{ ev.detail }}
              </q-item-label>
            </q-item-section>

            <!-- Only the rows that ARE a message carry one to read. -->
            <q-item-section v-if="ev.body" side>
              <q-btn flat round dense color="primary" icon="o_visibility" @click="openPreview(ev)">
                <q-tooltip>Preview email</q-tooltip>
              </q-btn>
            </q-item-section>
          </q-item>
        </q-list>

        <div v-else class="column flex-center q-pa-xl text-grey-6">
          <q-icon name="o_mark_email_unread" size="36px" class="q-mb-sm" />
          <div class="text-subtitle1 q-mb-xs">No email activity yet</div>
          <div class="text-center">
            Every send, reminder and delivery update for this client's intake form appears here.
          </div>
        </div>
      </q-card-section>

      <q-separator />
      <q-card-actions align="right">
        <!-- Why the client cannot be chased from here, when that is worth saying: they have already
             answered, the form was never sent, the request is with somebody else. -->
        <div v-if="!canRemind && remindBlockedReason" class="col text-caption text-grey-7 remind-note">
          {{ remindBlockedReason }}
        </div>
        <!-- The client's own form link, for chasing them by any means other than this dialog — a phone
             call, a message from someone's own mailbox. -->
        <q-btn
          v-if="clientFormLink" outline no-caps color="primary" icon="o_content_copy"
          label="Client Form" @click="copyClientFormLink"
        >
          <q-tooltip>Copy the client's intake form link</q-tooltip>
        </q-btn>
        <q-btn
          v-if="canRemind" unelevated no-caps color="amber-8" icon="o_notifications_active"
          label="Send reminder" @click="reminderOpen = true"
        />
        <q-btn flat no-caps color="grey-8" label="Close" @click="open = false" />
      </q-card-actions>
    </q-card>
  </q-dialog>

  <!-- The message as the client received it, read from what was stored at send rather than re-rendered from
       the template: the sender may rewrite any of it in the compose dialog. -->
  <q-dialog v-model="previewOpen">
    <q-card class="preview-card">
      <q-card-section class="row items-center no-wrap">
        <div class="col">
          <div class="text-subtitle1 text-primary">{{ emailEventOption(previewing.eventType).label }} email</div>
          <div class="text-caption text-grey-7">
            To {{ previewing.recipientEmail || "—" }} · {{ fmt.formatDateTime(previewing.occurredOnUtc) }}
          </div>
        </div>
        <q-btn flat round dense icon="o_close" color="grey-7" @click="previewOpen = false" />
      </q-card-section>
      <q-separator />
      <q-card-section>
        <div class="preview-subject-label">Subject</div>
        <div class="preview-subject">{{ previewing.subject || "—" }}</div>
      </q-card-section>
      <q-separator />
      <!-- The body IS html — it is what the template produces and what the rich-text editor wrote. -->
      <q-card-section class="preview-body">
        <!-- eslint-disable-next-line vue/no-v-html -->
        <div v-html="previewing.body" />
      </q-card-section>
      <q-separator />
      <q-card-actions align="right">
        <q-btn flat no-caps color="grey-8" label="Close" @click="previewOpen = false" />
      </q-card-actions>
    </q-card>
  </q-dialog>

  <!-- The same compose-and-send dialog the request page uses, so a reminder reads and behaves identically
       wherever it is sent from. -->
  <send-ems-dialog v-model="reminderOpen" mode="reminder" :rems-id="remsId" :subtitle="subtitle" @sent="onSent" />
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { remsApi, getApiErrorMessage, webUrl } from "services/api";
import { useNotify } from "composables/useNotify";
import { useDateFormat } from "composables/useDateFormat";
import { useRemsMeta } from "modules/rems/useRemsMeta";

import SendEmsDialog from "modules/rems/components/SendEmsDialog.vue";

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  remsId: { type: String, default: null },
  subtitle: { type: String, default: "" }
});
// `sent` fires after a reminder goes out, so a list that shows the last email event can refresh itself.
const emit = defineEmits(["update:modelValue", "sent"]);

const notify = useNotify();
const fmt = useDateFormat();
const { emailEventOption } = useRemsMeta();

const open = computed({
  get: () => props.modelValue,
  set: (val) => emit("update:modelValue", val)
});

const events = ref([]);
const loading = ref(false);
const reminderOpen = ref(false);
const previewOpen = ref(false);
// The row being read. An object rather than an id so the preview keeps its content while the dialog
// animates shut, instead of emptying out under the reader.
const previewing = ref({});
// The client's intake link, or empty where the server withholds it — before the form has been sent, and
// once the client has answered. See the footer button.
const clientFormLink = ref("");
// Whether THIS caller can chase the client, decided by the server rather than re-derived from a row: it is
// the reminder endpoint's own answer — permission, whose request.
const canRemind = ref(false);
const remindBlockedReason = ref("");

const load = async () => {
  if (!props.remsId) return;
  loading.value = true;
  try {
    const log = await remsApi.emailLog(props.remsId);
    events.value = log?.events || [];
    canRemind.value = !!log?.canRemind;
    remindBlockedReason.value = log?.remindBlockedReason || "";
    clientFormLink.value = webUrl(log?.clientFormLink);
  } catch (err) {
    notify.error(getApiErrorMessage(err));
    events.value = [];
    canRemind.value = false;
    remindBlockedReason.value = "";
    clientFormLink.value = "";
  } finally {
    loading.value = false;
  }
};

const openPreview = (ev) => {
  previewing.value = ev;
  previewOpen.value = true;
};

const copyClientFormLink = async () => {
  try {
    await navigator.clipboard.writeText(clientFormLink.value);
    notify.success("Client form link copied.");
  } catch {
    // Denied clipboard permission, or an insecure origin. Saying so beats a button that looks like it
    // worked — the link is on screen in the send dialog if they need it by hand.
    notify.warning("Could not copy the link. Your browser blocked clipboard access.");
  }
};

const onSent = () => {
  load();
  emit("sent");
};

watch(() => props.modelValue, (isOpen) => { if (isOpen) load(); });
</script>

<style scoped>
.event-subject {
  color: var(--ink-900);
  white-space: normal;
  word-break: break-word;
}
.preview-card {
  width: 640px;
  max-width: 92vw;
  border-radius: 12px;
}
.preview-subject-label {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.03em;
  text-transform: uppercase;
  color: var(--ink-500);
}
.preview-subject {
  font-size: 15px;
  font-weight: 600;
  color: var(--ink-900);
  word-break: break-word;
}
/* The body is whatever the template and the sender produced, so it is given room to scroll inside the
   card rather than pushing the actions off the bottom of a long email. */
.preview-body {
  max-height: 52vh;
  overflow: auto;
  word-break: break-word;
}
.preview-body :deep(img) { max-width: 100%; height: auto; }
.event-detail {
  white-space: normal;
  word-break: break-word;
}
/* Sits to the left of the buttons and wraps rather than pushing them off the card. */
.remind-note {
  white-space: normal;
  text-align: left;
  padding-right: 8px;
}
</style>
