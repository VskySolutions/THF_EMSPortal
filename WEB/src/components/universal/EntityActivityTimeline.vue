<template>
  <div class="uf-act">
    <q-inner-loading :showing="loading && !events.length" />
    <div v-if="!loading && !events.length" class="text-grey-6 q-pa-md text-center">No activity yet.</div>

    <!-- ONE LINE PER EVENT. A trail is read by scanning it, and the four things every entry says — what
         happened, what changed, who did. -->
    <div v-if="events.length" class="uf-act__list" role="list">
      <div v-for="e in events" :key="e.id" class="uf-act__row" role="listitem">
        <!-- The rail: a hairline behind the icons, so the list still reads as a sequence without spending a
             single pixel of row height on saying so. -->
        <span class="uf-act__dot">
          <q-icon :name="iconFor(e.eventType)" size="14px" />
        </span>

        <span class="uf-act__what ellipsis">
          {{ labelFor(e.eventType) }}
          <!-- The change itself, held back from the label: "Status changed" is the event, and "draft →
               awaiting_customer" is the detail somebody looks at only when the event matters. -->
          <span v-if="changeOf(e)" class="uf-act__change">{{ changeOf(e) }}</span>
          <q-tooltip v-if="changeOf(e)" max-width="360px" :delay="400">
            {{ labelFor(e.eventType) }} — {{ changeOf(e) }}
          </q-tooltip>
        </span>

        <span class="uf-act__who ellipsis">
          <q-icon v-if="!e.actorId" name="o_settings" size="13px" class="q-mr-xs" />
          {{ e.actorName || "System" }}
        </span>

        <span class="uf-act__when">{{ formatDateTime(e.occurredOnUtc) }}</span>
      </div>
    </div>

    <div v-if="hasMore" class="row justify-center q-mt-sm">
      <q-btn flat no-caps dense color="primary" label="Load more" :loading="loading" @click="loadMore" />
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from "vue";
import { ufActivityApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useDateFormat } from "composables/useDateFormat";

const props = defineProps({
  entityType: { type: Number, required: true },
  entityId: { type: String, required: true }
});

const notify = useNotify();
const { formatDateTime } = useDateFormat();

const events = ref([]);
const loading = ref(false);
const page = ref(1);
const total = ref(0);
// A row is one line now, so a page of fifty is a screenful or two rather than a scroll marathon — and
// it halves how often a reader has to press Load more to get back far enough to find anything.
const limit = 50;
const hasMore = ref(false);

const ICONS = {
  StatusChanged: "o_swap_horiz",
  FieldEdited: "o_edit",
  ConversationMessageAdded: "o_chat",
  TagApplied: "o_label",
  TagRemoved: "o_label_off",
  AttachmentUploaded: "o_attach_file",
  AttachmentDeleted: "o_delete",
  ChecklistItemCompleted: "o_check_circle",
  SyncCompleted: "o_cloud_done",
  SyncFailed: "o_error",
  Restored: "o_restore",
  // ---- REMS (ActivityEventTypes) ----
  // Without these a REMS request's timeline reads "RemsFormSent" and "RemsRelatedEntityStatusChanged" —
  // the fallback prints the raw type name, which is the API's word for the event and not anybody's.
  RemsCreated: "o_add_circle",
  RemsAssigned: "o_how_to_reg",
  RemsDeleted: "o_delete",
  RemsSentBack: "o_undo",
  RemsReturnedToAdmin: "o_reply",
  RemsFormBuilt: "o_dynamic_form",
  RemsFormSent: "o_send",
  RemsFormReminderSent: "o_mail",
  RemsFormSubmitted: "o_assignment_turned_in",
  RemsFormCorrected: "o_edit_note",
  RemsRelatedEntityStatusChanged: "o_account_tree",
  RemsEngagementUpdated: "o_work",
  RemsApprovalSent: "o_gavel",
  RemsApprovalResubmitted: "o_replay",
  RemsApproved: "o_check_circle",
  RemsRejected: "o_cancel",
  RemsFullyApproved: "o_task_alt",
  RemsApprovalStageAdvanced: "o_low_priority"
};
const iconFor = (type) => ICONS[type] || "o_circle";

const LABELS = {
  StatusChanged: "Status changed",
  FieldEdited: "Field edited",
  ConversationMessageAdded: "Message posted",
  TagApplied: "Tag applied",
  TagRemoved: "Tag removed",
  AttachmentUploaded: "Attachment uploaded",
  AttachmentDeleted: "Attachment deleted",
  ChecklistItemCompleted: "Checklist item completed",
  SyncCompleted: "Sync completed",
  SyncFailed: "Sync failed",
  Restored: "Record restored",
  // The REMS lifecycle, worded as the thing that HAPPENED rather than as the state it left behind —
  // a trail is a list of events, and the columns beside it already say who did it and when.
  RemsCreated: "Request raised",
  RemsAssigned: "Picked up by an admin",
  RemsDeleted: "Request deleted",
  RemsSentBack: "Sent back to the initiator",
  RemsReturnedToAdmin: "Handed back to the admin",
  RemsFormBuilt: "Intake form prepared",
  RemsFormSent: "Intake form emailed to the client",
  RemsFormReminderSent: "Reminder sent to the client",
  RemsFormSubmitted: "Client submitted their intake form",
  RemsFormCorrected: "Client's answers corrected",
  RemsRelatedEntityStatusChanged: "Related client moved on",
  RemsEngagementUpdated: "Engagement setup updated",
  RemsApprovalSent: "Routed for approval",
  RemsApprovalResubmitted: "Resubmitted for approval",
  RemsApproved: "Approver signed",
  RemsRejected: "Approver declined",
  RemsFullyApproved: "Fully approved",
  RemsApprovalStageAdvanced: "Next approval stage asked"
};

// An unmapped type renders as its own name rather than as a blank: a row nobody has worded yet is still
// worth seeing, and the raw name says which one it is.
const labelFor = (type) => LABELS[type] || type;

// The value change, or "" where the event carries none. Kept apart from the label so the row's second
// column can hold the event in the reading weight and the change in a quieter one.
const changeOf = (e) => {
  if (e.oldValue && e.newValue) return `${e.oldValue} → ${e.newValue}`;
  return e.newValue || "";
};

const load = async (reset = true) => {
  loading.value = true;
  try {
    const res = await ufActivityApi.list({
      entityType: props.entityType,
      entityId: props.entityId,
      page: page.value,
      limit
    });
    const data = res?.data || [];
    events.value = reset ? data : [...events.value, ...data];
    total.value = res?.meta?.totalRecords || events.value.length;
    hasMore.value = events.value.length < total.value;
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loading.value = false;
  }
};

const loadMore = () => {
  page.value += 1;
  load(false);
};

onMounted(() => load());
defineExpose({ load: () => { page.value = 1; return load(); } });
</script>

<style scoped>
.uf-act {
  position: relative;
}

/* The rail, drawn once behind the whole list rather than per row — it costs no height and it is what
   keeps a dense list reading as a sequence rather than as a table of unrelated lines. */
.uf-act__list {
  position: relative;
}
.uf-act__list::before {
  content: "";
  position: absolute;
  left: 10px;
  top: 10px;
  bottom: 10px;
  width: 1px;
  background: #e3e9f0;
}

/* Four columns that line up all the way down: what happened, who, when. The middle column is the only
   one that flexes, and it is the one allowed to truncate. */
.uf-act__row {
  display: grid;
  grid-template-columns: 21px minmax(0, 1fr) minmax(0, auto) auto;
  align-items: center;
  gap: 10px;
  padding: 5px 4px;
  font-size: 12.5px;
  line-height: 18px;
  border-radius: 4px;
}
.uf-act__row:hover {
  background: #f6f9fc;
}

.uf-act__dot {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 21px;
  height: 21px;
  border-radius: 50%;
  background: #fff;
  border: 1px solid #dbe4ec;
  color: var(--q-primary);
  position: relative;
  z-index: 1;
}

.uf-act__what {
  color: #1f2933;
  min-width: 0;
}
/* The detail behind the event, in the caption weight: it is read second, and on the rows that carry a
   long one it is the part that gives way. */
.uf-act__change {
  color: #6b7885;
  margin-left: 6px;
}

.uf-act__who {
  color: #5a6675;
  min-width: 0;
  text-align: right;
}

/* Tabular figures so the timestamps line up as a column of numbers rather than as a ragged edge. */
.uf-act__when {
  color: #8895a4;
  white-space: nowrap;
  font-variant-numeric: tabular-nums;
}

/* Below sm the two right-hand columns fold under the event, which is the only way four columns fit on a
   phone without the middle one truncating to nothing. */
@media (max-width: 599px) {
  .uf-act__row {
    grid-template-columns: 21px minmax(0, 1fr);
    row-gap: 2px;
  }
  .uf-act__who,
  .uf-act__when {
    grid-column: 2;
    text-align: left;
  }
}
</style>
