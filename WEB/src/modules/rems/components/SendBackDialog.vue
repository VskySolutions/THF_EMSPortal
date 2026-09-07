<template>
  <q-dialog v-model="open">
    <q-card class="sbd">
      <q-card-section class="row items-center no-wrap">
        <div class="col">
          <div class="text-subtitle1 text-primary">Send back for rework</div>
          <div v-if="remsNumber" class="text-caption text-grey-7">{{ remsNumber }}</div>
        </div>
        <q-btn flat round dense icon="o_close" color="grey-7" @click="open = false" />
      </q-card-section>
      <q-separator />

      <q-card-section>
        <!-- Who to hand it to. -->
        <div class="sbd-label">Who should make the changes?</div>
        <div class="sbd-choices" role="radiogroup" aria-label="Who should make the changes?">
          <button
            type="button" role="radio" :aria-checked="target === 'initiator'"
            class="sbd-choice" :class="{ 'sbd-choice--on': target === 'initiator' }"
            @click="target = 'initiator'"
          >
            <q-icon name="o_person" size="18px" />
            <span class="sbd-choice__text">
              <span class="sbd-choice__name">{{ initiatorName || "The partner" }}</span>
              <span class="sbd-choice__role">Raised this request</span>
            </span>
          </button>
          <button
            v-if="cseOffered" type="button" role="radio" :aria-checked="target === 'cse'"
            class="sbd-choice" :class="{ 'sbd-choice--on': target === 'cse' }"
            @click="target = 'cse'"
          >
            <q-icon name="o_support_agent" size="18px" />
            <span class="sbd-choice__text">
              <span class="sbd-choice__name">{{ cseName }}</span>
              <span class="sbd-choice__role">CSE on this request</span>
            </span>
          </button>
        </div>

        <!-- Said rather than left to be inferred from a missing button: an admin who expects to see the CSE
             here needs to know why they cannot, and what would change it. -->
        <div v-if="cseName && !cseEligible" class="sbd-note">
          <q-icon name="o_info" size="16px" class="q-mr-xs" />
          {{ cseName }} cannot be given this: {{ initiatorName || "the partner" }} has not named a REMS
          delegate, so their rework stays with them.
        </div>

        <div class="text-body2 q-mb-md">
          They will be able to change the <strong>Engagement Setup</strong> only — the client's own
          answers stay read-only to them. They hand it back to you to confirm before it can go for
          approval.
        </div>
        <app-text-field
          v-model="reason" label="What needs changing?" type="textarea" autogrow required autofocus
          placeholder="Be specific — this is the whole instruction they get."
          :error="attempted && !reason.trim()"
          error-message="A reason is required. A return with no reason is not actionable."
        />
      </q-card-section>

      <q-separator />
      <q-card-actions align="right">
        <q-btn flat no-caps color="grey-8" label="Cancel" @click="open = false" />
        <q-btn unelevated no-caps color="orange-9" label="Send back" @click="confirm" />
      </q-card-actions>
    </q-card>
  </q-dialog>
</template>

<script setup>
// The admin's return of a request for engagement-setup rework. The reason is mandatory on the server too;
// asking for it here is so they are not told off after the fact.
import { ref, computed, watch } from "vue";
import AppTextField from "components/common/AppTextField.vue";

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  remsNumber: { type: String, default: "" },
  // The two people the rework can be handed to, for the choice above. An empty `cseName` means none is
  // named on the request, and the choice collapses to the one answer there is.
  initiatorName: { type: String, default: "" },
  cseName: { type: String, default: "" },
  // Whether the CSE may actually be handed the rework: the request's own `canSendBackToCse`, which the
  // server sets only when the initiator has a REMS delegate in force.
  cseEligible: { type: Boolean, default: false }
});
const emit = defineEmits(["update:modelValue", "confirm"]);

const reason = ref("");
const target = ref("initiator");
const attempted = ref(false);

// The CSE is a choice only when the request names one and the initiator has cover arranged.
const cseOffered = computed(() => !!props.cseName && props.cseEligible);

const open = computed({
  get: () => props.modelValue,
  set: (v) => emit("update:modelValue", v)
});

watch(open, (isOpen) => {
  if (isOpen) {
    reason.value = "";
    // The partner every time it opens, rather than whatever was chosen last: this is where returns went
    // before the choice existed, and a remembered selection is how a request goes to the wrong person.
    target.value = "initiator";
    attempted.value = false;
  }
});

const confirm = () => {
  attempted.value = true;
  if (!reason.value.trim()) return;
  emit("confirm", { reason: reason.value.trim(), returnTo: target.value });
};
</script>

<style scoped>
.sbd {
  width: 520px;
  max-width: 92vw;
  border-radius: 12px;
}

.sbd-label {
  font-size: 13px;
  font-weight: 600;
  color: var(--ink-900);
  margin-bottom: 8px;
}
.sbd-choices {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 18px;
}
/* Pulled up under the choices it explains, so it reads as a note on them rather than as a new paragraph. */
.sbd-note {
  display: flex;
  align-items: flex-start;
  margin: -10px 0 18px;
  font-size: 12.5px;
  line-height: 1.45;
  color: var(--ink-500);
}
.sbd-choice {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  flex: 1 1 200px;
  padding: 10px 14px;
  border: 1px solid var(--line);
  border-radius: 8px;
  background: var(--white);
  color: var(--ink-700);
  font: inherit;
  text-align: left;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s, color 0.15s;
}
.sbd-choice:hover { border-color: var(--teal-300); background: var(--teal-050); }
.sbd-choice:focus-visible { outline: 2px solid var(--teal-500); outline-offset: 2px; }
.sbd-choice--on,
.sbd-choice--on:hover {
  background: var(--teal-900);
  border-color: var(--teal-900);
  color: var(--white);
}
.sbd-choice__text { display: flex; flex-direction: column; min-width: 0; }
.sbd-choice__name {
  font-size: 13px;
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.sbd-choice__role { font-size: 11px; opacity: 0.75; }
</style>
