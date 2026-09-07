<template>
  <q-page padding>
    <app-detail-header :items="breadcrumbs" :back-to="backTo">
      <template #actions>
        <!-- EVERY action on the request lives here. -->
        <div class="rf-head">
          <!-- The row helper rather than the bare code, so a request nobody has picked up says so here
               exactly as it does in EMS Review instead of reading as though an admin is already on it. -->
          <app-option-badge v-if="request" :option="requestStatusOption(request)" />

          <!-- What the page is doing with what has been typed. -->
          <div v-if="autoSaveOn" class="rf-save" :class="`rf-save--${saveChip.tone}`">
            <q-spinner v-if="saveState === 'saving'" size="14px" />
            <q-icon v-else :name="saveChip.icon" size="15px" />
            <q-tooltip>{{ saveMessage ? `${saveChip.text} — ${saveMessage}` : saveChip.text }}</q-tooltip>
          </div>

          <!-- The mode switch. -->
          <q-btn
            v-if="request && !isEditing && canEditAnything"
            unelevated no-caps color="primary" icon="o_edit" label="Edit"
            @click="setMode('edit')"
          />
          <q-btn
            v-if="request" outline no-caps color="primary" icon="o_forum" label="Conversation"
            @click="conversationOpen = true"
          />
          <!-- What the client has actually been sent, and whether it landed. -->
          <q-btn
            v-if="canReadEmailLog" outline no-caps color="primary" icon="o_mark_email_read"
            label="Email log" @click="emailLogOpen = true"
          />

          <!-- Claiming the request. -->
          <q-btn
            v-if="canPickUp" unelevated no-caps color="amber-8" icon="o_pan_tool_alt"
            label="Pick up" :loading="acting" @click="pickUp"
          >
            <q-tooltip>Take this request on — its engagement setup becomes yours to work</q-tooltip>
          </q-btn>

          <!-- The workflow moves: each one hands the request to somebody else. -->
          <!-- Submitting to the client belongs at the end of the tabs, where the work it completes is. -->
          <q-btn
            v-if="canSendToClient && !hasFinishTab" unelevated no-caps color="teal-7" icon="o_send"
            label="Submit to Client" :disable="!readyToSend" @click="openSend"
          >
            <q-tooltip v-if="!readyToSend">{{ sendBlockedReason }}</q-tooltip>
          </q-btn>
          <q-btn
            v-if="canRemind" unelevated no-caps color="amber-8" icon="o_notifications_active"
            label="Send reminder" @click="openReminder"
          />
          <!-- The client's own form link, for chasing them by any means other than the portal — a call, a
               message from somebody's own mailbox. -->
          <q-btn
            v-if="clientFormLink" outline no-caps color="primary" icon="o_content_copy"
            label="Client Form" @click="copyClientFormLink"
          >
            <q-tooltip>Copy the client's intake form link</q-tooltip>
          </q-btn>
          <!-- The reply to Send back, and the only one of these buttons the INITIATOR or the CSE presses:
               it appears on a request that came back to them for rework. -->
          <q-btn
            v-if="canReturnToAdmin" unelevated no-caps color="primary" icon="o_assignment_turned_in"
            label="Return to admin" :loading="acting" @click="returnToAdmin"
          >
            <q-tooltip max-width="300px">
              Done with the changes — hand the request back to the admin reviewing it, who is told it is
              waiting
            </q-tooltip>
          </q-btn>
          <!-- Send back and Hand back are two different moves that sound like one, and they sit two buttons
               apart. -->
          <q-btn
            v-if="canSendBack" outline no-caps color="orange-9" icon="o_assignment_return"
            label="Send back" @click="sendBackOpen = true"
          >
            <q-tooltip max-width="300px">
              Ask the partner or the CSE to change something — the request stays yours and comes back to
              you once they have
            </q-tooltip>
          </q-btn>
          <!-- Giving the request back to the queue. -->
          <q-btn
            v-if="canHandBack" outline no-caps color="grey-8" icon="o_undo"
            label="Hand back" :loading="acting" @click="handBack"
          >
            <q-tooltip max-width="300px">
              Give this request up — it goes back to EMS Review for any admin to pick up, and stops being
              yours
            </q-tooltip>
          </q-btn>
        </div>
      </template>
    </app-detail-header>

    <acting-as-banner />

    <div v-if="loading" class="row flex-center q-pa-xl"><q-spinner color="primary" size="36px" /></div>

    <q-banner v-else-if="errorMsg" class="bg-red-1 text-red-9 rounded-borders">
      <template #avatar><q-icon name="o_error" color="red-9" /></template>
      {{ errorMsg }}
    </q-banner>

    <template v-else>
      <!-- Why this request is back with its initiator, and who said so. -->
      <q-banner v-if="openSendBack" dense class="rf-alert rf-alert--warn q-mb-md">
        <template #avatar><q-icon name="o_assignment_return" color="orange-9" /></template>
        <!-- Who it went to, on the returns that recorded a choice — the reader may be the CSE looking at
             a request the admin handed to the partner, or the other way round. -->
        <div class="text-weight-medium">
          Sent back by {{ openSendBack.returnedBy || "an admin" }}
          <template v-if="openSendBack.returnedTo"> — for {{ openSendBack.returnedTo }} to action</template>
        </div>
        <div>{{ openSendBack.reason }}</div>
      </q-banner>

      <q-banner v-if="declinedReasons.length" dense class="rf-alert rf-alert--reject q-mb-md">
        <template #avatar><q-icon name="o_gavel" color="red-9" /></template>
        <div class="text-weight-medium">The approvers declined this request</div>
        <ul class="q-my-xs q-pl-md">
          <li v-for="(r, i) in declinedReasons" :key="i">{{ r }}</li>
        </ul>
      </q-banner>

      <q-banner v-if="lockedReason" dense class="rf-alert rf-alert--lock q-mb-md">
        <template #avatar><q-icon name="o_lock" color="blue-9" /></template>
        {{ lockedReason }}
      </q-banner>

      <!-- ─── The workspace: the client's answers beside the referral being built from them ────────── -->
      <!-- Once the client has submitted, this page is two things read against each other — what they told
           us, and what the firm is filling in on the strength of it. -->
      <div ref="workRef" class="rf-work" :class="{ 'rf-work--split': showSubmittedPane }">
        <template v-if="showSubmittedPane">
          <div class="rf-work__pane" :style="{ flexBasis: `${splitPct}%` }">
            <q-card flat bordered class="rf-card rf-submitted">
              <div class="rf-submitted__head">
                <q-icon name="o_description" size="18px" color="primary" class="q-mr-xs" />
                <div class="col">
                  <div class="text-subtitle2 text-weight-medium">Submitted EMS Form</div>
                  <div class="text-caption text-grey-7">{{ submittedNote }}</div>
                </div>
                <!-- Correcting the client's answers, in the corner of the pane that holds them. -->
                <q-btn
                  v-if="canEditSubmission" flat dense no-caps size="sm" color="primary" icon="o_edit"
                  label="Edit Form" @click="editFormOpen = true"
                >
                  <q-tooltip max-width="280px">
                    Correct what the client submitted. The change is recorded against your name; the
                    client is not asked again.
                  </q-tooltip>
                </q-btn>
              </div>
              <q-separator />
              <div class="rf-submitted__body">
                <submitted-form-panel ref="submittedPanelRef" :rems-id="remsId" />
              </div>
            </q-card>
          </div>

          <!-- Draggable, and reachable from the keyboard: the arrow keys move it in steps, Home puts it
               back to the 40 / 60 it starts at. -->
          <div
            class="rf-work__gutter" role="separator" tabindex="0"
            aria-label="Resize the submitted form pane"
            aria-orientation="vertical"
            :aria-valuenow="Math.round(splitPct)" :aria-valuemin="MIN_SPLIT" :aria-valuemax="MAX_SPLIT"
            @pointerdown="startDrag" @dblclick="setSplit(DEFAULT_SPLIT)" @keydown="onGutterKey"
          >
            <span class="rf-work__grip" />
          </div>
        </template>

        <div class="rf-work__pane rf-work__pane--main">
          <q-card flat bordered class="rf-card">
            <div class="rf-tabbar">
              <q-tabs
                v-model="tab" dense align="left" active-color="primary" indicator-color="primary"
                class="text-grey-7 rf-tabs col" no-caps inline-label
              >
                <q-tab
                  v-for="t in tabs" :key="t.name" :name="t.name" :icon="t.icon" :label="t.label" :disable="t.disable"
                />
              </q-tabs>

              <div class="rf-tabs__end">
                <!-- The one thing the tab strip cannot say for itself — why the rest of it is greyed out
                     — parked at the end of the strip it is about, and only while it is still true. -->
                <q-icon v-if="tabsNote" name="o_info" size="20px" color="primary" class="rf-tabs__note">
                  <q-tooltip anchor="bottom right" self="top right" max-width="320px" :delay="200">
                    {{ tabsNote }}
                  </q-tooltip>
                </q-icon>

                <!-- Step back. -->
                <q-btn
                  v-if="prevTab" flat dense no-caps color="primary" icon="o_chevron_left" label="Prev"
                  class="q-px-sm" @click="goTab(prevTab.name)"
                >
                  <q-tooltip>{{ prevTab.label }}</q-tooltip>
                </q-btn>

                <!-- The ONE save on the page: a request must exist before anything can be auto-saved
                     against it, so on a brand-new request the create IS the step forward. -->
                <q-btn
                  v-if="isNew" unelevated no-caps dense color="primary" icon-right="o_arrow_right"
                  label="Save as Draft &amp; Next" class="q-px-md" :loading="saving" @click="createDraft"
                />

                <!-- Filled while stepping on is the thing to do here, outlined where it shares the corner
                     with the two ways out — on that tab they are the point and this is the aside. -->
                <q-btn
                  v-else-if="showNext" dense no-caps color="primary" icon-right="o_chevron_right"
                  label="Next" class="q-px-md" :outline="atFinishTab" :unelevated="!atFinishTab"
                  @click="goTab(nextTab.name)"
                >
                  <q-tooltip>{{ nextTab.label }}</q-tooltip>
                </q-btn>

                <!-- Commission is where the initiator's own work ends: past it the tabs are the client's
                     answers and the approvers' round, neither of which they fill in. -->
                <q-btn
                  v-if="atFinishTab" unelevated dense no-caps color="teal-7" icon="o_send"
                  label="Submit to Client" class="q-px-md" :disable="!readyToSend" @click="openSend"
                >
                  <q-tooltip v-if="!readyToSend">{{ sendBlockedReason }}</q-tooltip>
                </q-btn>
              </div>
            </div>
            <q-separator />

            <q-tab-panels v-model="tab" keep-alive animated>
              <!-- ---------- Client Information ---------- -->
              <q-tab-panel name="client">
                <detail-grid v-if="!isEditing" :rows="clientRows" />
                <!-- Entity Type, Industry and the CSE are the page's to save — the entity type and the
                     CSE belong to the request's EMS form record and the industry to its engagement. -->
                <client-information-fields
                  v-else
                  ref="clientFieldsRef"
                  v-model="clientForm"
                  v-model:entity-type="setupForm.entityType"
                  v-model:industry="setupForm.industry"
                  v-model:cse-user-id="setupForm.cseUserId"
                  :cse-options="cseOptions"
                  :cse-hint="cseHint"
                  :readonly="!canEditClient"
                  :client-locked="clientLocked"
                  :compact="showSubmittedPane"
                  :setup-readonly="!canEditSetup"
                  :entity-type-options="entityTypeOptions"
                  :entity-type-locked="entityTypeLocked"
                  :industry-options="industryOptions"
                  :type-options="typeOptions"
                  :files="request?.files || []"
                  :attempted="attempted"
                  @change="markDirty('client')"
                  @remove-file="removeAttachment"
                />

                <!-- The client's other businesses, and whether each has been turned into its own request
                     yet. -->
                <additional-entities-panel
                  v-if="additionalEntities.length" :rows="additionalEntities" class="q-mt-md"
                  @create-ems="createFollowUp"
                />
              </q-tab-panel>

              <!-- ---------- Engagement Setup ---------- -->
              <q-tab-panel name="setup">
                <div v-if="!setupEngagement" class="text-grey-7">{{ noEngagementNote }}</div>

                <detail-grid v-else-if="!isEditing" :rows="setupRows" />

                <!-- The entity type goes down read-only: the Government Audit card keys off it. -->
                <engagement-setup-form
                  v-else
                  ref="setupRef"
                  :engagement="setupEngagement"
                  :entity-type="setupForm.entityType"
                  :dept-options="departmentOptions"
                  :service-line-options="serviceLineOptions"
                  :tax-form-options="taxFormOptions"
                  :tax-form-unavailable="taxFormUnavailable"
                  :department-directors="workspace?.departmentDirectors || []"
                  :executive-options="executiveOptions"
                  :billing-manager-options="billingManagerOptions"
                  :billing-period-options="billingPeriodOptions"
                  :personnel-level-options="personnelLevelOptions"
                  :editable="canEditSetup"
                  @change="markDirty('setup')"
                />
              </q-tab-panel>

              <!-- ---------- Marketing ---------- -->
              <q-tab-panel v-if="setupEngagement" name="marketing">
                <detail-grid v-if="!isEditing" :rows="marketingRows" />
                <engagement-marketing
                  v-else
                  ref="marketingRef"
                  :engagement="setupEngagement" :marketing-groups="marketingGroups"
                  :marketing-unavailable="marketingUnavailable" :editable="canEditSetup"
                  @change="markDirty('marketing')"
                />
              </q-tab-panel>

              <!-- ---------- Commission ---------- -->
              <q-tab-panel v-if="setupEngagement" name="commission">
                <!-- No hint line here. -->
                <detail-grid v-if="!isEditing" :rows="commissionRows" />
                <engagement-commission
                  v-else
                  ref="commissionRef"
                  :engagement="setupEngagement" :recipient-options="cseOptions" :editable="canEditSetup"
                  @change="markDirty('commission')"
                />
              </q-tab-panel>

              <!-- ---------- Approval ---------- -->
              <!-- Only ever reached by someone who may read it: the approver list is gated on managing
                   engagements. -->
              <q-tab-panel v-if="showApprovalTab" name="approval">
                <template v-if="engagement">
                  <engagement-approval
                    v-if="canManageApproval"
                    :engagement="engagement" :can-send="canRouteForApproval"
                    :marketing-saved="marketingComplete" @status-changed="load"
                  />
                  <q-banner v-else dense class="rf-alert rf-alert--lock">
                    <template #avatar><q-icon name="o_gavel" color="blue-9" /></template>
                    {{ approvalNote }}
                  </q-banner>
                  <approval-history :engagement-id="engagement.id" class="q-mt-md" />
                </template>
              </q-tab-panel>

              <!-- ---------- Activity ---------- -->
              <!-- What has HAPPENED to this request, as against the four tabs before it, which are what it
                   SAYS. Tags sit at the top of it rather than in the header. -->
              <q-tab-panel v-if="remsId" name="activity">
                <entity-tags-panel :entity-type="EntityType.Rems" :entity-id="remsId" class="q-mb-md" />
                <q-separator class="q-mb-md" />
                <entity-activity-timeline :entity-type="EntityType.Rems" :entity-id="remsId" />
              </q-tab-panel>
            </q-tab-panels>
          </q-card>
        </div>
      </div>

      <!-- No action bar: every button lives in one of the two corners at the top — the workflow moves in
           the header. -->
      <app-record-audit :audit="request?.audit" class="q-mt-md" />
    </template>

    <!-- All five act on a saved request, so none of them exist while one is being composed. -->
    <template v-if="remsId">
      <send-back-dialog
        v-model="sendBackOpen" :rems-number="request?.remsNumber"
        :initiator-name="request?.audit?.createdBy || ''" :cse-name="request?.cse?.name || ''"
        :cse-eligible="!!request?.canSendBackToCse"
        @confirm="sendBack"
      />
      <send-ems-dialog v-model="sendOpen" :rems-id="remsId" :subtitle="subtitle" @sent="load" />
      <send-ems-dialog
        v-model="reminderOpen" mode="reminder" :rems-id="remsId" :subtitle="subtitle" @sent="load"
      />
      <conversation-dialog v-model="conversationOpen" :request-id="remsId" :subtitle="subtitle" />
      <email-log-dialog v-model="emailLogOpen" :rems-id="remsId" :subtitle="subtitle" @sent="load" />
      <edit-submitted-form-dialog
        v-if="canEditSubmission" v-model="editFormOpen" :rems-id="remsId" :subtitle="subtitle"
        @saved="onSubmissionCorrected"
      />
    </template>
  </q-page>
</template>

<script setup>
// THE REMS form: one tabbed page for creating, editing and reading a request.
import { ref, reactive, computed, watch, nextTick, onMounted, onBeforeUnmount } from "vue";
import { useRoute, useRouter, onBeforeRouteLeave } from "vue-router";
import { remsApi, getApiErrorMessage, webUrl, EntityType } from "services/api";
import { useNotify } from "composables/useNotify";
import { formatDateOnly } from "composables/useDateFormat";
import { useConfirm } from "composables/useConfirm";
import { usePermissions, Permissions } from "composables/usePermissions";
import {
  useRemsMeta, useRemsOptionSets, useRemsEngagementOptionSets, useRemsEntityTypes, REMS_SEAT_ROLES,
  isCasDepartment, isAssuranceDepartment, isGcsDepartment, isTaxDepartment, requiresClientAcceptanceForm,
  isIndividualEntityType
} from "modules/rems/useRemsMeta";
import { useAutoSave } from "modules/rems/useAutoSave";
import { REMS_STATUS, REMS_REWORK_STATUSES } from "modules/rems/remsStatus";
import { useAuthStore } from "stores/auth";

import AppDetailHeader from "components/common/AppDetailHeader.vue";
import AppOptionBadge from "components/common/AppOptionBadge.vue";
import AppRecordAudit from "components/common/AppRecordAudit.vue";
import EntityTagsPanel from "components/universal/EntityTagsPanel.vue";
import EntityActivityTimeline from "components/universal/EntityActivityTimeline.vue";
import ActingAsBanner from "modules/rems/components/ActingAsBanner.vue";
import DetailGrid from "modules/rems/components/DetailGrid.vue";
import ClientInformationFields from "modules/rems/components/ClientInformationFields.vue";
import AdditionalEntitiesPanel from "modules/rems/components/AdditionalEntitiesPanel.vue";
import ApprovalHistory from "modules/rems/components/ApprovalHistory.vue";
import SendBackDialog from "modules/rems/components/SendBackDialog.vue";
import EngagementSetupForm from "modules/rems/components/engagement/EngagementSetupForm.vue";
import EngagementMarketing from "modules/rems/components/engagement/EngagementMarketing.vue";
import EngagementCommission from "modules/rems/components/engagement/EngagementCommission.vue";
import EngagementApproval from "modules/rems/components/engagement/EngagementApproval.vue";
import SendEmsDialog from "modules/rems/components/SendEmsDialog.vue";
import SubmittedFormPanel from "modules/rems/components/SubmittedFormPanel.vue";
import EditSubmittedFormDialog from "modules/rems/components/EditSubmittedFormDialog.vue";
import ConversationDialog from "modules/rems/components/ConversationDialog.vue";
import EmailLogDialog from "modules/rems/components/EmailLogDialog.vue";

const route = useRoute();
const router = useRouter();
const notify = useNotify();
const { confirm } = useConfirm();
const { has } = usePermissions();
const auth = useAuthStore();
const { emsFormActivity, requestStatusOption, approverRoleLabel } = useRemsMeta();
const { typeOptions, load: loadTypes } = useRemsOptionSets();
const { entityTypeOptions, load: loadEntityTypes } = useRemsEntityTypes();
const {
  departmentOptions, serviceLineOptions, industryOptions,
  marketingGroups, marketingUnavailable,
  taxFormOptions, taxFormUnavailable, billingPeriodOptions, personnelLevelOptions,
  load: loadEngagementOptions
} = useRemsEngagementOptionSets();

// The three routes this page answers on. Which one is live is the whole of "what am I doing here": there
// is no id sentinel and no mode flag to keep in step with it.
const ROUTE_NEW = "rems_request_new";
const ROUTE_EDIT = "rems_request_edit";
const ROUTE_VIEW = "rems_request";

const isNew = computed(() => route.name === ROUTE_NEW);
const remsId = computed(() => (isNew.value ? null : route.params.id || null));

const loading = ref(true);
const saving = ref(false);
const acting = ref(false);
const errorMsg = ref("");
const attempted = ref(false);

const request = ref(null);
const workspace = ref(null);
// The engagement as the SERVER last described it, re-read after each auto-save pass.
const engagementLive = ref(null);
// The workspace is refused to anyone the request is not about. Tracked so the setup tab can say which
// of the two silences it is — "not yours to work" or "this request never had an engagement".
const workspaceDenied = ref(false);
const sendBacks = ref([]);
const additionalEntities = ref([]);
const cseOptions = ref([]);
const executiveOptions = ref([]);
const billingManagerOptions = ref([]);

const clientFieldsRef = ref(null);
const setupRef = ref(null);
const marketingRef = ref(null);
const commissionRef = ref(null);

const conversationOpen = ref(false);
const emailLogOpen = ref(false);
const editFormOpen = ref(false);
const submittedPanelRef = ref(null);
const sendOpen = ref(false);
const reminderOpen = ref(false);
const sendBackOpen = ref(false);

const blankClient = () => ({
  // The name as one string — composed from the parts below for an individual, and the legal name itself
  // for an organisation. It is what the request is identified by everywhere it is not being edited.
  clientName: "",
  // The name in PARTS, which is how the form asks for it: two boxes for a person, one for a company.
  clientFirstName: "",
  clientLastName: "",
  clientCorporateName: "",
  // The generational suffix, kept apart from the name — see ClientInformationFields for why.
  clientNameSuffix: "",
  customerEmail: "",
  customerMobileNumber: "",
  type: "",
  existingClientReferenceId: null
});
const clientForm = reactive(blankClient());
// The fields the page owns rather than a tab.
const setupForm = reactive({ cseUserId: null, entityType: null, industry: null });

// ---- View vs Edit ----
// Two ways of looking at the same record: View renders every field as a label, Edit renders the controls.
const isEditing = computed(() => isNew.value || route.name === ROUTE_EDIT);

const engagement = computed(() => engagementLive.value || workspace.value?.engagement || null);
const engagementId = computed(() => engagement.value?.id || null);

// The engagement a brand-new request will get, so the setup tab has a shape to render before there is
// anything to write it to.
const newEngagement = Object.freeze({
  id: null,
  department: null,
  serviceLine: null,
  departmentDirector: null,
  engagementExecutive: null,
  billingManager: null,
  firstYearFeeEstimate: null,
  realizationPercentage: null,
  billingPeriod: null,
  billingProcessDescription: "",
  status: "Draft",
  marketingMethodIds: [],
  commissionSplits: [],
  audit: null,
  government: null,
  tax: null
});
// What the three engagement tabs are SEEDED from — deliberately the loaded workspace rather than the
// live view above, so a background re-read after an auto-save cannot overwrite a field mid-keystroke.
const setupEngagement = computed(() => (isNew.value ? newEngagement : workspace.value?.engagement || null));

// A request being created is a draft in every respect but existing, so the stage rules below read it as one.
const status = computed(() => (isNew.value ? REMS_STATUS.DRAFT : request.value?.status || ""));
const subtitle = computed(() =>
  [request.value?.remsNumber, request.value?.clientName].filter(Boolean).join(" — "));

const breadcrumbs = computed(() => [
  { label: "Home", icon: "o_home", to: "/" },
  { label: cameFromReview.value ? "EMS Review" : "My Requests", to: backTo.value },
  { label: isNew.value ? "New Request" : request.value?.remsNumber || "Request" }
]);
// Which list to go back to: whichever one the user can actually work this request from.
const cameFromReview = computed(() =>
  !isNew.value && has(Permissions.RemsEngagementsManage) && !isInitiatorStage.value);
const backTo = computed(() => (cameFromReview.value ? "/rems/ems-review" : "/rems/partner"));

// ---- Who may touch what, by stage ----
const isInitiatorStage = computed(() =>
  [REMS_STATUS.DRAFT, REMS_STATUS.AWAITING_CUSTOMER, REMS_STATUS.RETURNED_TO_INITIATOR,
    REMS_STATUS.CHANGES_REQUESTED].includes(status.value));
const isAdminStage = computed(() =>
  [REMS_STATUS.ADMIN_REVIEW, REMS_STATUS.AWAITING_ADMIN_CONFIRMATION].includes(status.value));
// Sent back for rework — by the admin, or by the approvers declining a round. It sits with the initiator,
// but it has not left the admin's desk: they asked for the change, and they are the only route onward.
const isReworkStage = computed(() => REMS_REWORK_STATUSES.includes(status.value));
// Everything freezes once a round is open, and stays frozen once approved.
const frozen = computed(() =>
  [REMS_STATUS.PENDING_APPROVAL, REMS_STATUS.APPROVED].includes(status.value));

const isAdmin = computed(() => has(Permissions.RemsEngagementsManage));

// THE admin on this request, not just an admin.
const assignedAdminId = computed(() => request.value?.assignedAdmin?.id || null);
// Super Admins and Tenant Admins are exempt from the whole ownership rule, so a request can be worked
// around when the admin holding it is away.
const isElevated = computed(() =>
  auth.roles.includes("SuperAdmin") || auth.roles.includes("TenantAdmin"));
const isHoldingAdmin = computed(() =>
  isElevated.value ||
  (isAdmin.value && !!assignedAdminId.value && assignedAdminId.value === auth.user?.userId));
// Nobody has taken this one.
const awaitingPickUp = computed(() =>
  !isNew.value && status.value !== REMS_STATUS.DRAFT && !assignedAdminId.value);

// The client tab belongs to the initiator while the request is theirs, and to the admin while it is his.
const canEditClient = computed(() => {
  if (frozen.value) return false;
  if (isAdminStage.value) return isHoldingAdmin.value;
  if (isReworkStage.value) return isAdmin.value;
  return [REMS_STATUS.DRAFT, REMS_STATUS.AWAITING_CUSTOMER].includes(status.value);
});
// The setup is what the rework IS, so it stays open to the initiator and the CSE throughout — and to an
// admin, for the same reason the client tab is (the server agrees: RemsSetupAccess.CanWork).
const canEditSetup = computed(() => {
  if (frozen.value) return false;
  if (isAdminStage.value) return isHoldingAdmin.value;
  return isInitiatorStage.value;
});
// Whether there is anything on this page this user could change at this stage — what decides if the Edit
// switch is offered at all.
const canEditAnything = computed(() => canEditClient.value || canEditSetup.value);

// Whether the page has anything of its own to write. Same answer as above, kept separate because they are
// separate questions — one asks what this user may change, the other what the page's Save writes.
const canSaveForm = computed(() => canEditClient.value || canEditSetup.value);

// The intake form has gone out naming this client, at this address and number.
const clientLocked = computed(() => !isNew.value && status.value !== REMS_STATUS.DRAFT);
const entityTypeLocked = computed(() => !isNew.value && status.value !== REMS_STATUS.DRAFT);

const lockedReason = computed(() => {
  if (status.value === REMS_STATUS.PENDING_APPROVAL) {
    return "This request is with the approvers. Every field is read-only until they decide.";
  }
  if (status.value === REMS_STATUS.APPROVED) return "This engagement is approved and permanently read-only.";
  if (status.value === REMS_STATUS.AWAITING_CUSTOMER && !canEditClient.value) {
    return "The intake form is with the client.";
  }
  // The state this whole page is read-only in for a reason the reader can do something about.
  if (isAdminStage.value && isAdmin.value && !isHoldingAdmin.value) {
    return assignedAdminId.value
      ? `${request.value?.assignedAdmin?.name || "Another admin"} picked this request up. Only they can work it.`
      : "Nobody has picked this request up yet. Pick it up to work its engagement setup.";
  }
  return "";
});

const noEngagementNote = computed(() => (workspaceDenied.value
  ? "The engagement setup is with whoever the request is with — you can read the request itself on the first tab."
  : "This request has no engagement on file. It was raised before the setup moved onto this page."));

// ---- Actions available now ----
// None of them apply to a request that does not exist yet: they all act on a saved record.
const canSendToClient = computed(() =>
  !isNew.value && status.value === REMS_STATUS.DRAFT && has(Permissions.RemsFormsSend));
const canRemind = computed(() =>
  status.value === REMS_STATUS.AWAITING_CUSTOMER && has(Permissions.RemsFormsSend));
// There is a log to read once something has been emailed; before the intake link goes out it would open
// on nothing. The reminder inside it is gated separately, by the server.
const canReadEmailLog = computed(() =>
  !isNew.value && has(Permissions.RemsEmailLogRead) && emsFormActivity(request.value));

// The client's intake link, already resolved to an absolute URL. Non-empty only in the window the server
// hands it over in — the form is out with the client and unanswered — so the button gates on it directly.
const clientFormLink = computed(() => webUrl(request.value?.clientFormLink));

const copyClientFormLink = async () => {
  try {
    await navigator.clipboard.writeText(clientFormLink.value);
    notify.success("Client form link copied.");
  } catch {
    // Denied clipboard permission, or an insecure origin. Saying so beats a button that looks like it
    // worked — the Send EMS dialog shows the same link on screen for copying by hand.
    notify.warning("Could not copy the link. Your browser blocked clipboard access.");
  }
};
const canReturnToAdmin = computed(() =>
  [REMS_STATUS.RETURNED_TO_INITIATOR, REMS_STATUS.CHANGES_REQUESTED].includes(status.value));
// Both are moves only the admin HOLDING the request can make — returning it for rework and routing it to
// the approvers are the two ways it leaves their desk.
const canSendBack = computed(() => isAdminStage.value && isHoldingAdmin.value);
const canRouteForApproval = computed(() =>
  isAdminStage.value && isHoldingAdmin.value && has(Permissions.RemsApprovalsSend));

// Claiming the request, and giving it back.
const canPickUp = computed(() =>
  awaitingPickUp.value && isAdmin.value && has(Permissions.RemsRequestsAssign));
const canHandBack = computed(() => isHoldingAdmin.value && has(Permissions.RemsRequestsAssign));

const pickUp = async () => {
  // Asked the same way Hand back is, and for the same reason: this moves the request from everybody to one
  // person.
  const ok = await confirm({
    title: "Pick this request up",
    message: "The request becomes yours and its engagement setup opens for you to work. No other admin " +
      "can take it while you hold it — Hand back is what returns it to the queue. Continue?",
    confirmLabel: "Pick up"
  });
  if (!ok) return;
  acting.value = true;
  try {
    await remsApi.pickUp(remsId.value);
    notify.success(`${request.value?.remsNumber || "This request"} is yours. Its engagement setup is now open to you.`);
  } catch (err) {
    // Most often "somebody else got there first" — the reload below is what shows who.
    notify.error(getApiErrorMessage(err));
  } finally {
    acting.value = false;
    await load();
  }
};

const handBack = async () => {
  const ok = await confirm({
    title: "Hand back to the queue",
    message: "This puts the request back in EMS Review as waiting for pickup, and its engagement setup " +
      "goes read-only to you. Any admin can take it from there — including you. Continue?",
    confirmLabel: "Hand back"
  });
  if (!ok) return;
  acting.value = true;
  try {
    await flushSaves();
    await remsApi.handBack(remsId.value);
    notify.success("Handed back. It is waiting for pickup again.");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    acting.value = false;
    await load();
  }
};

// The lighter of the two completeness bars: enough to ask the client for their details.
const round2 = (n) => Math.round(n * 100) / 100;
const commissionTotal = computed(() => round2(
  (engagement.value?.commissionSplits || []).reduce((sum, s) => sum + (Number(s.percentage) || 0), 0)));
const commissionCount = computed(() => (engagement.value?.commissionSplits || []).length);
const commissionAllocated = computed(() => commissionTotal.value === 100);

const readyToSend = computed(() =>
  !!clientForm.customerEmail?.trim() && !!setupForm.cseUserId && !!setupForm.entityType &&
  !!setupForm.industry && commissionAllocated.value);
const sendBlockedReason = computed(() => {
  if (!clientForm.customerEmail?.trim()) return "The client has no email address to send the form to.";
  if (!setupForm.cseUserId) return "Choose a CSE first — it is on the Client Information tab.";
  if (!setupForm.entityType) {
    return "Choose an entity type on the Client Information tab — it decides what the client is asked.";
  }
  // Required now, where it used to be optional. It is how the engagement is classified and reported on,
  // and a client whose trade nobody recorded at intake is one nobody goes back and records it for.
  if (!setupForm.industry) {
    return "Choose an industry on the Client Information tab — the Entity Type beside it narrows the list.";
  }
  if (!commissionAllocated.value) {
    // Naming nobody is its own sentence.
    if (!commissionCount.value) {
      return "No commission recipients yet — the Commission tab must name recipients adding up to 100% " +
        "before this request can be sent to the client.";
    }
    return `Commission totals ${commissionTotal.value}% — the recipients on the Commission tab must add ` +
      "up to 100% before this request can be sent to the client.";
  }
  return "";
});

// Whether the client has answered — what puts their form in the left pane.
const hasSubmission = computed(() => !!workspace.value?.client);

// ---- The submitted-form pane ----
// Shown whenever there is a submission to show, in BOTH modes: reading a request and correcting one are
// each done against what the client actually said.
const showSubmittedPane = computed(() => !isNew.value && hasSubmission.value);

// Whether the client's answers are this caller's to correct.
const canEditSubmission = computed(() => showSubmittedPane.value && isAdmin.value && !frozen.value);

// The pane's caption.
const submittedNote = computed(() => (canEditSubmission.value
  ? "What the client submitted. Corrections are recorded against the admin who makes them."
  : "Read-only snapshot of exactly what the client submitted."));

// A correction replaces the stored snapshot, so the pane behind the dialog has to re-read it.
const onSubmissionCorrected = () => { submittedPanelRef.value?.reload(); };

// 40 / 60 — the left pane is a record to consult, the right one is the work.
const DEFAULT_SPLIT = 40;
const MIN_SPLIT = 25;
const MAX_SPLIT = 65;
const SPLIT_KEY = "rems.request.splitPct";

const clampSplit = (pct) => Math.min(MAX_SPLIT, Math.max(MIN_SPLIT, pct));

const readStoredSplit = () => {
  try {
    const stored = Number(window.localStorage.getItem(SPLIT_KEY));
    // Clamped on the way in as well as on the way out: what comes back is whatever is in that browser's
    // storage.
    return Number.isFinite(stored) && stored > 0 ? clampSplit(stored) : DEFAULT_SPLIT;
  } catch {
    // Private windows and blocked site data throw on the accessor itself. The default is a fine answer.
    return DEFAULT_SPLIT;
  }
};

const workRef = ref(null);
const splitPct = ref(readStoredSplit());

const setSplit = (pct) => {
  splitPct.value = clampSplit(pct);
  try {
    window.localStorage.setItem(SPLIT_KEY, String(splitPct.value));
  } catch { /* nothing to do — the pane simply starts at the default next time */ }
};

// Pointer events rather than mouse: the same handler then covers a trackpad, a touch screen and a pen,
// and setPointerCapture keeps the drag alive when the pointer outruns the four-pixel gutter.
const startDrag = (event) => {
  const box = workRef.value?.getBoundingClientRect();
  if (!box?.width) return;
  event.preventDefault();
  const target = event.currentTarget;
  target.setPointerCapture?.(event.pointerId);

  const move = (e) => setSplit(((e.clientX - box.left) / box.width) * 100);
  const stop = () => {
    // Throws NotFoundError when the pointer is already gone, which is exactly the case a pointercancel
    // reports — and the listeners below still have to come off either way.
    try { target.releasePointerCapture?.(event.pointerId); } catch { /* already released */ }
    target.removeEventListener("pointermove", move);
    target.removeEventListener("pointerup", stop);
    target.removeEventListener("pointercancel", stop);
    document.body.classList.remove("rf-dragging");
  };

  // On the gutter itself, which holds the capture — a listener on the document would keep firing after
  // the page navigated away mid-drag.
  target.addEventListener("pointermove", move);
  target.addEventListener("pointerup", stop);
  target.addEventListener("pointercancel", stop);
  // Stops the drag from selecting the text it passes over, and keeps the resize cursor throughout.
  document.body.classList.add("rf-dragging");
};

const onGutterKey = (event) => {
  const step = event.shiftKey ? 10 : 2;
  if (event.key === "ArrowLeft") setSplit(splitPct.value - step);
  else if (event.key === "ArrowRight") setSplit(splitPct.value + step);
  else if (event.key === "Home") setSplit(DEFAULT_SPLIT);
  else return;
  event.preventDefault();
};
const marketingComplete = computed(() => (engagement.value?.marketingMethodIds?.length || 0) > 0);
const openSendBack = computed(() => sendBacks.value.find((s) => !s.resolvedOnUtc) || null);
const declinedReasons = ref([]);

const cseHint = computed(() => (cseOptions.value.length
  ? ""
  : "Nobody holds the \"CSE\" role — assign it on a user's page in Administration → Users."));

// ---- The Approval tab ----
// Reading the approver list is gated on managing engagements, which the initiator does not hold — so
// showing them the section would render a bare 403 on a tab they could do nothing with anyway.
const ROUND_OPENED = ["PendingApproval", "Approved", "Rejected"];
const approvalStarted = computed(() => ROUND_OPENED.includes(engagement.value?.status));
const canManageApproval = computed(() => isAdmin.value);
const showApprovalTab = computed(() =>
  !!engagement.value && (canManageApproval.value || approvalStarted.value));

const approvalNote = computed(() => {
  const s = engagement.value?.status;
  if (s === "PendingApproval") return "This engagement is with its approvers. You will be notified when they decide.";
  if (s === "Approved") return "This engagement is fully approved.";
  if (s === "Rejected") {
    return "The approvers declined and the setup is back with you. Revise it, then return the request to the admin.";
  }
  return "The admin sends this for approval once the client's intake has been reviewed.";
});

// ---- Tabs ----
// One per part of the referral.
const TABS = [
  { name: "client", label: "Client Information", icon: "o_person" },
  { name: "setup", label: "Engagement Setup", icon: "o_work" },
  { name: "marketing", label: "Marketing", icon: "o_campaign" },
  { name: "commission", label: "Commission", icon: "o_payments" },
  { name: "approval", label: "Approval", icon: "o_approval" },
  // Last, because it is the only tab that is not part of filling the request in: it is the record's own
  // trail, and its tags.
  { name: "activity", label: "Activity", icon: "o_history" }
];

const tabs = computed(() => {
  const shown = {
    client: true,
    setup: true,
    marketing: !!setupEngagement.value,
    commission: !!setupEngagement.value,
    approval: showApprovalTab.value,
    // There is no trail before there is a record. A request being composed has neither.
    activity: !isNew.value
  };
  return TABS.filter((t) => shown[t.name]).map((t) => ({
    ...t,
    // Everything past the first tab is written against a request id, and a request being composed has
    // none until its first save.
    disable: isNew.value && t.name !== "client"
  }));
});

// What the greyed-out tabs mean, for the one state in which they are greyed out.
const tabsNote = computed(() => (isNew.value
  ? "Start with the client. Saving this first tab files the request as a draft, which is what the " +
    "remaining tabs are filled against — and from that point everything you type saves itself."
  : ""));

// Held in the URL, so a reload or a shared link comes back to the tab that was open.
const tab = computed({
  get: () => {
    const wanted = route.query.tab;
    return tabs.value.some((t) => t.name === wanted && !t.disable) ? wanted : "client";
  },
  set: (name) => {
    router.replace({ query: { ...route.query, tab: name === "client" ? undefined : name } });
  }
});

// ---- Stepping through the tabs ----
// The strip is the map; these are the path through it, so nobody has to know which tab comes next.
const goTab = (name) => { tab.value = name; };

const stepTab = (delta) => {
  const list = tabs.value;
  for (let i = list.findIndex((t) => t.name === tab.value) + delta; i >= 0 && i < list.length; i += delta) {
    if (!list[i].disable) return list[i];
  }
  return null;
};
const prevTab = computed(() => stepTab(-1));
const nextTab = computed(() => stepTab(1));

// Where the initiator's own work ends. What lies past Commission is somebody else's: the approvers' round.
const FINISH_TAB = "commission";
const hasFinishTab = computed(() => tabs.value.some((t) => t.name === FINISH_TAB && !t.disable));
// Only where there is something to finish WITH — Submit to Client is the whole of finishing here.
const atFinishTab = computed(() => tab.value === FINISH_TAB && canSendToClient.value);
// Finishing does not REPLACE stepping on.
const showNext = computed(() => !!nextTab.value);

// ---- View mode: the same fields, as a record rather than a form ----
// Resolved through the same option lists the pickers use, so a code the tenant has relabelled reads the
// same in both modes — and an unknown code shows itself rather than rendering blank.
const labelOf = (options, value) =>
  (value ? options.find((o) => o.value === value)?.label || value : "");

const nameOf = (options, id) => (id ? options.find((o) => o.value === id)?.label || "" : "");

const currency = (v) =>
  (v === null || v === undefined || v === "" ? "" : `$${Number(v).toLocaleString()}`);

// Calendar dates read MM/DD/YYYY and are never timezone-shifted — see formatDateOnly.
const dateOnly = (v) => formatDateOnly(v, "");

// Marketing options arrive grouped for the picker; flattened here to turn stored ids back into labels.
const marketingLabels = computed(() => {
  const byId = new Map();
  (marketingGroups.value || []).forEach((g) => (g.items || []).forEach((i) => byId.set(i.value, i.label)));
  return (engagement.value?.marketingMethodIds || []).map((id) => byId.get(id) || id);
});

const commissionLabels = computed(() =>
  (engagement.value?.commissionSplits || [])
    .map((s) => `${s.employee?.name || nameOf(cseOptions.value, s.employeeId)} — ${s.percentage}%`));

const clientRows = computed(() => [
  // Read as one name, the particle after it — the pair is edited as two boxes because they are two
  // different things to store, not because they are two things about the client.
  { label: "Client", value: clientForm.clientName, suffix: clientForm.clientNameSuffix || "" },
  { label: "Client Email Address", value: clientForm.customerEmail },
  { label: "Client Phone Number", value: clientForm.customerMobileNumber },
  { label: "Relationship to THF", value: labelOf(typeOptions.value, clientForm.type) },
  { label: "Entity Type", value: labelOf(entityTypeOptions.value, setupForm.entityType) },
  { label: "Industry", value: labelOf(industryOptions.value, setupForm.industry) },
  // Whose client this is. Read here rather than under the engagement setup, which is where it is asked.
  { label: "CSE", value: nameOf(cseOptions.value, setupForm.cseUserId) },
  // Read off the request rather than a picker: nobody chooses this, an admin claims it. Blank until one
  // does, and the badge in the header is what says the request is waiting for that.
  { label: "Reviewing Admin", value: request.value?.assignedAdmin?.name || "Waiting for pickup" },
  // "Message from Partner" is not asked for any more, so this shows only on the older requests that
  // carry one — dropping the row outright would hide text somebody wrote and nothing else displays.
  {
    label: "Message from Partner",
    value: request.value?.description,
    wide: true,
    html: true,
    hideWhenEmpty: true
  },
  {
    label: "Attachments",
    value: (request.value?.files || []).map((f) => f.fileName).filter(Boolean),
    wide: true
  }
]);

const setupRows = computed(() => {
  const e = engagement.value;
  if (!e) return [];
  // The same sequence the form is filled in, so reading a request and typing one describe it in the same
  // order.
  return [
    { label: "Service Line", value: labelOf(serviceLineOptions.value, e.serviceLine) },
    { label: "Department", value: labelOf(departmentOptions.value, e.department) },
    { label: "Department Director", value: e.departmentDirector?.name },
    { label: "Engagement Executive", value: e.engagementExecutive?.name },
    { label: "Billing Manager", value: e.billingManager?.name },
    // Every row from here down is asked of some departments and not others, so the summary asks the same
    // questions the form did.
    ...(isAssuranceDepartment(e.department)
      ? [{ label: "Engagement Fee", value: currency(e.engagementFee) }]
      : isGcsDepartment(e.department)
        ? []
        : [{ label: "First-Year Fee Estimate", value: currency(e.firstYearFeeEstimate) }]),
    { label: "% Realization", value: e.realizationPercentage == null ? "" : `${e.realizationPercentage}%` },
    ...(isCasDepartment(e.department)
      ? [
        { label: "Billing Frequency", value: labelOf(billingPeriodOptions.value, e.billingPeriod) },
        { label: "Description of Billing Process", value: e.billingProcessDescription, wide: true }
      ]
      : []),
    ...(isAssuranceDepartment(e.department)
      ? [
        { label: "Fiscal Year End of Client", value: dateOnly(e.audit?.clientFiscalYearEnd) },
        {
          label: "Admin Fees",
          value: e.audit?.adminFeesApply ? (currency(e.audit.adminFeesAmount) || "Yes") : "No"
        }
      ]
      : []),
    ...(isGcsDepartment(e.department)
      ? [
        { label: "Purchase Order No.", value: e.government?.purchaseOrderNumber },
        { label: "Purchase Order Amount", value: currency(e.government?.purchaseOrderAmount) },
        { label: "PO Beginning Date", value: dateOnly(e.government?.purchaseOrderStartDate) },
        { label: "PO Ending Date", value: dateOnly(e.government?.purchaseOrderEndDate) },
        { label: "Purchase Order", value: e.government?.purchaseOrderMediaId ? "On file" : "Not yet provided" },
        { label: "Personnel Level", value: labelOf(personnelLevelOptions.value, e.government?.personnelLevel) },
        { label: "Bill Rate / Hour", value: currency(e.government?.billRatePerHour) }
      ]
      : []),
    ...(isTaxDepartment(e.department)
      ? [
        { label: "Fiscal Year End", value: dateOnly(e.tax?.fiscalYearEnd) },
        { label: "Original Due Date", value: dateOnly(e.tax?.originalDueDate) },
        { label: "First Extension Due Date", value: dateOnly(e.tax?.firstExtensionDueDate) }
      ]
      : []),
    // The remaining conditional blocks say nothing when they do not apply, so they are hidden rather than
    // shown empty — unlike the fields above, where blank IS the record.
    { label: "Contract Number", value: e.government?.contractNumber, hideWhenEmpty: true },
    {
      label: "Signed Client Acceptance Form",
      value: requiresClientAcceptanceForm(e.department)
        ? (e.audit?.clientAcceptanceFormMediaId ? "On file" : "Not yet provided")
        : "",
      hideWhenEmpty: true
    }
  ];
});

const marketingRows = computed(() => [{ label: "Marketing", value: marketingLabels.value, wide: true }]);
const commissionRows = computed(() => [{ label: "Commission", value: commissionLabels.value, wide: true }]);

// ---- Load ----
// Every people picker here is scoped to the ROLE of the same name, held on the user's own page.

const toOptions = (rows) => (rows || []).map((r) => ({ label: r.name, value: r.id }));

// The unscoped admin list is not fetched any more: it fed the "Assign to Admin" picker, and every picker
// left here names the seat it fills.
const loadPickers = async () => {
  const [cse, execs, billing] = await Promise.all([
    remsApi.admins(REMS_SEAT_ROLES.CSE).catch(() => []),
    remsApi.admins(REMS_SEAT_ROLES.ENGAGEMENT_EXECUTIVE).catch(() => []),
    remsApi.admins(REMS_SEAT_ROLES.BILLING_MANAGER).catch(() => [])
  ]);
  cseOptions.value = toOptions(cse);
  executiveOptions.value = toOptions(execs);
  billingManagerOptions.value = toOptions(billing);
};

// The CSE, the Entity Type and the Industry as they were picked BEFORE the request existed.
let pendingSetupPick = null;

const seedForms = (detail, ws) => {
  // The composed name — "Smith John Jr." for a person, the legal name for an organisation. The form
  // splits it back into its parts for an individual; for an organisation it is the name whole.
  clientForm.clientName = detail.clientName ?? "";
  clientForm.clientFirstName = detail.clientFirstName || "";
  clientForm.clientLastName = detail.clientLastName || "";
  clientForm.clientCorporateName = detail.clientCorporateName || "";
  clientForm.clientNameSuffix = detail.clientNameSuffix || "";
  clientForm.customerEmail = detail.customerEmail || "";
  clientForm.customerMobileNumber = detail.customerMobileNumber || "";
  clientForm.type = detail.type || "";
  clientForm.existingClientReferenceId = detail.existingClientReferenceId || null;
  setupForm.cseUserId = detail.cse?.id || pendingSetupPick?.cseUserId || null;
  setupForm.entityType =
    ws?.entityType || detail.entityType || pendingSetupPick?.entityType || null;
  setupForm.industry = ws?.engagement?.industry || pendingSetupPick?.industry || null;
};

// The reasons behind the LAST failed round, so the initiator sees what to fix without opening history.
const loadDeclineReasons = async (id) => {
  if (!id || status.value !== REMS_STATUS.CHANGES_REQUESTED) return [];
  const rounds = await remsApi.approvalHistory(id).catch(() => []);
  const last = rounds.find((r) => r.status === "Rejected");
  return (last?.decisions || [])
    .filter((d) => d.status === "Rejected" && d.reason)
    .map((d) => `${d.approver} (${approverRoleLabel(d.role)}): ${d.reason}`);
};

// The engagement as the server now holds it, WITHOUT re-seeding the tabs from it — read after the page's
// own writes so the approval tab and the send-for-approval gate follow what actually landed.
const refreshEngagement = async () => {
  if (isNew.value || !remsId.value) return;
  const ws = await remsApi.engagement(remsId.value).catch(() => null);
  if (ws) engagementLive.value = ws.engagement || null;
};

// ---- What the page writes ----
// Only the client half is enforced, because it is what the API requires to accept a request at all.
const clientProblem = () => {
  // Point at the box that is actually blank: for an individual that is First/Last Name, not the search.
  if (!clientForm.clientName?.trim()) {
    return isIndividualEntityType(setupForm.entityType)
      ? "Give the client's first and last name."
      : "Search for the client, or type the new client's name.";
  }
  if (!clientForm.type) return "Choose how this referral relates to THF's records.";
  // The email, specifically.
  if (!clientForm.customerEmail?.trim()) {
    return "Give the client's email address — the intake form is emailed to them.";
  }
  return "";
};

// No `description`: "Message from Partner" is not on the form, and leaving the field out of the payload is
// what preserves it — the endpoint reads an omitted field as "leave this alone".
const clientPayload = () => ({
  type: clientForm.type,
  clientName: clientForm.clientName,
  // The PARTS, so the server does not have to guess where a name splits.
  clientFirstName: clientForm.clientFirstName || "",
  clientLastName: clientForm.clientLastName || "",
  clientCorporateName: clientForm.clientCorporateName || "",
  clientNameSuffix: clientForm.clientNameSuffix || "",
  customerEmail: clientForm.customerEmail || null,
  customerMobileNumber: clientForm.customerMobileNumber || null,
  existingClientReferenceId: clientForm.existingClientReferenceId || null
});

// ---- Auto-save ----
// Everything after the first save writes itself, part by part, in the order the records depend on each
// other: the request, then the CSE + entity type on its intake form.
const autoSaveOn = computed(() =>
  !isNew.value && !!remsId.value && isEditing.value && canSaveForm.value);

// What the request said last time the page read or wrote it. Compared before a write so opening a record,
// touching nothing, and switching tabs does not file a save of the identical thing.
let clientBaseline = "";
const clientSnapshot = () => JSON.stringify(clientPayload());

const {
  state: saveState, message: saveMessage, pending: savePending,
  mark: markDirty, flush: flushSaves, suspend: suspendSaves, resume: resumeSaves, reset: resetSaves
} = useAutoSave({
  client: async () => {
    if (!canEditClient.value) return "";
    const problem = clientProblem();
    if (problem) {
      // The chip says the tab cannot be saved; this is what points at the field that is why.
      attempted.value = true;
      return problem;
    }

    // The fields and the attachments are marked by the same flag but are two different writes, and
    // either can be the only one there is — a file picked with nothing retyped must still upload.
    if (clientSnapshot() !== clientBaseline) {
      request.value = await remsApi.update(remsId.value, clientPayload());
      clientBaseline = clientSnapshot();
    }
    // Uploaded now rather than on selection, so the files land on a request that exists.
    const mediaIds = (await clientFieldsRef.value?.uploadAttachments(remsId.value)) || [];
    if (mediaIds.length) request.value = await remsApi.addFiles(remsId.value, mediaIds);
    return "";
  },
  form: async () => {
    if (!canEditSetup.value) return "";
    // CSE and Entity Type live on the EMS form record, which is what the client's link is minted from
    // (`entityType` on the wire).
    if (!setupForm.cseUserId || !setupForm.entityType) {
      return setupForm.entityType
        ? "The CSE and the Entity Type are saved together — choose a CSE on the Client Information tab."
        : "The CSE and the Entity Type are saved together — choose an Entity Type on the Client Information tab.";
    }
    await remsApi.saveForm(remsId.value, {
      cseUserId: setupForm.cseUserId,
      entityType: setupForm.entityType
    });
    return "";
  },
  // The client's trade.
  industry: async () => {
    if (!canEditSetup.value || !engagementId.value) return "";
    // Empty string, not null — the endpoint reads null as "leave this field alone", so clearing the
    // picker has to say so out loud or the old value comes back on the next read.
    await remsApi.updateEngagement(engagementId.value, { industry: setupForm.industry ?? "" });
    return "";
  },
  // Only a request raised before engagements existed has none, and there is nowhere to put the setup for
  // one of those. Every request created since has one — raised in the same transaction as the request.
  setup: async () => {
    if (!canEditSetup.value || !engagementId.value) return "";
    await setupRef.value?.saveSetup(engagementId.value, remsId.value);
    return "";
  },
  marketing: async () => {
    if (!canEditSetup.value || !engagementId.value) return "";
    return (await marketingRef.value?.saveMarketing(engagementId.value)) || "";
  },
  commission: async () => {
    if (!canEditSetup.value || !engagementId.value) return "";
    await commissionRef.value?.saveCommission(engagementId.value);
    return "";
  }
}, {
  enabled: () => autoSaveOn.value,
  onSaved: refreshEngagement
});

// The state the page owns itself. The tabs below own theirs and announce it (@change).
watch(clientForm, () => markDirty("client"), { deep: true });
// `setupForm` is two writes, so it is two flags rather than one deep watcher: the CSE and the entity type
// go to the EMS form record together, the industry to the engagement.
watch([() => setupForm.cseUserId, () => setupForm.entityType], () => markDirty("form"));
watch(() => setupForm.industry, () => markDirty("industry"));

const saveChip = computed(() => ({
  saving: { tone: "busy", icon: "o_sync", text: "Saving…" },
  saved: { tone: "ok", icon: "o_cloud_done", text: "All changes saved" },
  pending: { tone: "busy", icon: "o_edit", text: "Unsaved changes" },
  blocked: { tone: "warn", icon: "o_pending", text: "Not saved yet" },
  error: { tone: "bad", icon: "o_cloud_off", text: "Not saved" }
}[saveState.value] || { tone: "idle", icon: "o_cloud_sync", text: "Saves as you type" }));

const load = async () => {
  // Seeding writes to the very state the watchers above listen to, and that is the page catching up with
  // the server rather than the user typing.
  suspendSaves();
  try {
    // Nothing to read for a request that does not exist: the blank form IS the state.
    if (isNew.value) {
      loading.value = false;
      return;
    }
    loading.value = true;
    errorMsg.value = "";
    try {
      // A refused workspace is not a failed page: the request itself still reads, and the setup tab
      // says why it is empty. Settled rather than awaited so one outcome cannot hide the other.
      const [detail, wsResult] = await Promise.all([
        remsApi.get(remsId.value),
        remsApi.engagement(remsId.value).then((ws) => ({ ws }), () => ({ denied: true }))
      ]);
      const ws = wsResult.ws || null;
      workspaceDenied.value = !!wsResult.denied;
      request.value = detail;
      workspace.value = ws;
      engagementLive.value = ws?.engagement || null;
      seedForms(detail, ws);
      sendBacks.value = await remsApi.sendBacks(remsId.value).catch(() => []);
      additionalEntities.value = ws?.additionalEntities || [];
      declinedReasons.value = await loadDeclineReasons(ws?.engagement?.id);
    } catch (err) {
      errorMsg.value = getApiErrorMessage(err);
    } finally {
      loading.value = false;
    }
  } finally {
    clientBaseline = clientSnapshot();
    // A tick, so the tabs re-rendering off the freshly seeded state settles before edits count again.
    await nextTick();
    resetSaves();
    resumeSaves();
    // Whatever was picked before the request existed is writable against it now.
    if (pendingSetupPick) {
      if (pendingSetupPick.entityType || pendingSetupPick.cseUserId) markDirty("form");
      if (pendingSetupPick.industry) markDirty("industry");
      pendingSetupPick = null;
    }
  }
};

// One request per page, but the page outlives the id: creating one replaces "new" with its id, and the
// Create-EMS action on another of the client's businesses moves it to a different request entirely.
watch(remsId, (id) => {
  if (id) load();
});

// Leaving a tab commits what was typed on it rather than waiting out the debounce, and arriving at
// Approval re-reads the engagement so the round reflects the setup that was just saved.
watch(tab, async (name) => {
  await flushSaves();
  if (name === "approval") await refreshEngagement();
});

// Reading and editing are two paths now, so switching between them is a navigation rather than a query
// flip.
const setMode = async (mode) => {
  await flushSaves();
  router.replace({
    name: mode === "edit" ? ROUTE_EDIT : ROUTE_VIEW,
    params: { id: remsId.value },
    query: { ...route.query }
  });
};

// ---- The one save: filing the draft ----
// A request has to exist before anything else on this page can be written against it, so the first tab is
// committed by hand.
const createDraft = async () => {
  attempted.value = true;
  const problem = clientProblem();
  if (problem) {
    notify.warning(problem);
    return;
  }

  saving.value = true;
  // The create writes the client row only.
  pendingSetupPick = {
    cseUserId: setupForm.cseUserId,
    entityType: setupForm.entityType,
    industry: setupForm.industry
  };
  // Held outside the try: once the request exists, this page is about THAT request whatever fails
  // afterwards, or a second attempt would file a second copy of it.
  let created = null;
  try {
    created = await remsApi.create(clientPayload());
    const mediaIds = (await clientFieldsRef.value?.uploadAttachments(created.id)) || [];
    if (mediaIds.length) await remsApi.addFiles(created.id, mediaIds);
    notify.success(`${created.remsNumber} saved as a draft. Everything from here saves itself.`);
  } catch (err) {
    const detail = getApiErrorMessage(err);
    // A failure after the request itself was written is not "nothing happened", and saying so would send
    // the user back to a form they think is unsaved.
    notify.error(created
      ? `${created.remsNumber} was created, but its attachments did not save: ${detail}`
      : detail);
  } finally {
    saving.value = false;
    // Leaves the URL on the request that now exists, open for editing, on the tab that comes next.
    if (created) {
      router.replace({ name: ROUTE_EDIT, params: { id: created.id }, query: { tab: "setup" } });
    } else {
      // Nothing was created, so nothing reloads and the picks are still on screen where they were made.
      pendingSetupPick = null;
    }
  }
};

// Takes one already-saved attachment off the request.
const removeAttachment = async (file, done) => {
  try {
    const ok = await confirm({
      title: "Remove attachment",
      message: `Take "${file.fileName || "this file"}" off this request? It stops being part of what the ` +
        "approvers see. This does not delete the file itself.",
      confirmLabel: "Remove",
      type: "danger"
    });
    if (!ok) return;
    request.value = await remsApi.removeFile(remsId.value, file.id);
    notify.success("Attachment removed.");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    done?.();
  }
};

// ---- Workflow moves ----
// Each of these hands the request to somebody else, so whatever is still sitting in the debounce goes with
// it.
const openSend = async () => {
  await flushSaves();
  sendOpen.value = true;
};

const openReminder = async () => {
  await flushSaves();
  reminderOpen.value = true;
};

// `payload` is { reason, returnTo } off the dialog — returnTo names which of the two people on the request
// is being asked to do the work.
const sendBack = async (payload) => {
  acting.value = true;
  try {
    await flushSaves();
    await remsApi.sendBack(remsId.value, payload);
    const to = payload.returnTo === "cse"
      ? (request.value?.cse?.name || "the CSE")
      : (request.value?.audit?.createdBy || "the initiator");
    notify.success(`Sent back to ${to}.`);
    await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    acting.value = false;
    sendBackOpen.value = false;
  }
};

const returnToAdmin = async () => {
  // Confirmed because it is the initiator's last move on the request: the setup goes read-only to them
  // the moment it lands with the admin, and getting it back means asking for another send-back.
  const admin = request.value?.assignedAdmin?.name || "the admin";
  const ok = await confirm({
    title: "Return to admin",
    message: `This hands the revised engagement setup to ${admin} to confirm, and notifies them. ` +
      "The setup is read-only to you until they act on it. Continue?",
    confirmLabel: "Return to admin"
  });
  if (!ok) return;
  acting.value = true;
  try {
    await flushSaves();
    await remsApi.returnToAdmin(remsId.value);
    notify.success("Returned to the admin for confirmation.");
    await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    acting.value = false;
  }
};

// Raise a fresh request for one of the client's other businesses, pre-filled from the contact they gave.
const createFollowUp = async (row) => {
  try {
    await flushSaves();
    const follow = await remsApi.create({
      clientName: row.fullName,
      customerEmail: row.emailAddress || undefined,
      customerMobileNumber: row.phoneNumber || undefined,
      type: clientForm.type,
      fromAdditionalEntityId: row.id
    });
    notify.success(`${follow.remsNumber} created for ${row.fullName}.`);
    router.push({ name: ROUTE_EDIT, params: { id: follow.id } });
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// Navigating away commits the debounce; closing the tab cannot be awaited, so that one is a warning.
onBeforeRouteLeave(async () => { await flushSaves(); });

const warnOnUnload = (e) => {
  if (!savePending.value) return;
  e.preventDefault();
  e.returnValue = "";
};

onMounted(async () => {
  window.addEventListener("beforeunload", warnOnUnload);
  await Promise.all([loadTypes(), loadEntityTypes(), loadEngagementOptions(), loadPickers()]);
  await load();
});

onBeforeUnmount(() => window.removeEventListener("beforeunload", warnOnUnload));
</script>

<style scoped>
.rf-card {
  border-radius: 12px;
}

/* ── The two panes ────────────────────────────────────────────────────────────────────────────────
   With a submission to show this is a flex row: the submitted form on a flex-basis the reader drags, the
   referral form taking whatever is left. WITHOUT one — a new request, a draft, anything the client has
   not answered yet — it is an ordinary block and the form has the page, exactly as it did before the
   pane existed.
   The flex declarations belong to --split for that reason. Left on the wrapper unconditionally, a lone
   pane is a flex item at its initial `flex: 0 1 auto`, which sizes it to its CONTENT rather than to the
   row: the form came out about 60% wide on /rems/requests/new and narrower still on a short tab. */
.rf-work {
  min-width: 0;
}
.rf-work--split {
  display: flex;
  align-items: flex-start;
}
.rf-work__pane {
  min-width: 0;   /* without it a wide table inside a pane sets the pane's width and the drag does nothing */
}
/* flex-basis is set inline from the split percentage; growing is the other pane's job. */
.rf-work--split .rf-work__pane { flex: 0 0 auto; }
.rf-work--split .rf-work__pane--main { flex: 1 1 0; }

.rf-work__gutter {
  flex: 0 0 12px;
  align-self: stretch;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: col-resize;
  /* The hit area is twelve pixels; the line drawn inside it is two. A four-pixel target is a target
     people miss. */
  touch-action: none;
}
.rf-work__grip {
  width: 2px;
  height: 44px;
  border-radius: 2px;
  background: var(--line);
  transition: background 0.15s, height 0.15s;
}
.rf-work__gutter:hover .rf-work__grip,
.rf-work__gutter:focus-visible .rf-work__grip {
  background: var(--q-primary);
  height: 72px;
}
.rf-work__gutter:focus-visible {
  outline: 2px solid var(--teal-500);
  outline-offset: -2px;
  border-radius: 6px;
}

/* The submitted form scrolls inside its own pane rather than making the page taller: the point of the
   split is that both halves are on screen together. Sticky so the pane keeps pace as the form beside it
   is scrolled. */
.rf-submitted {
  position: sticky;
  top: 12px;
  display: flex;
  flex-direction: column;
  max-height: calc(100vh - 140px);
}
.rf-submitted__head {
  display: flex;
  align-items: flex-start;
  padding: 12px 14px;
}
.rf-submitted__body {
  overflow: auto;
  padding: 14px;
}

/* Under a laptop there is no width to split: two panes of a 1024px page leave the left one too narrow to
   read an address in and the right one too narrow to lay a form out in. They stack instead, the client's
   answers first — they are what the rest is filled in against. */
@media (max-width: 1023px) {
  .rf-work--split {
    flex-direction: column;
    align-items: stretch;
  }
  /* `auto` basis, !important, because the inline flex-basis from the drag would otherwise set the
     HEIGHT of the stacked pane. */
  .rf-work--split .rf-work__pane { flex: 1 1 auto !important; }
  .rf-work__gutter { display: none; }
  .rf-submitted {
    position: static;
    max-height: 60vh;
    margin-bottom: 16px;
  }
}
.rf-tabs {
  padding: 0 4px;
  /* col gives flex-basis 0; without this the strip cannot shrink past its tabs and its own overflow
     arrows never appear — it just pushes the note off the card instead. */
  min-width: 0;
}
.rf-tabbar {
  display: flex;
  flex-wrap: nowrap;
  align-items: center;
}

/* The right corner of the strip: the note about the tabs, and the step through them.
   flex-shrink 0 so the tabs give up their width first and fall back to their own overflow arrows. */
.rf-tabs__end {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 0 0 auto;
  padding: 6px 12px 6px 8px;
}

/* On a phone there is no width to share: three dense buttons and a six-tab strip on one line leaves
   neither readable. The actions take their own line under the tabs instead. */
@media (max-width: 599px) {
  .rf-tabbar { flex-wrap: wrap; }
  .rf-tabs { flex: 1 1 100%; }
  .rf-tabs__end {
    width: 100%;
    justify-content: flex-end;
    flex-wrap: wrap;
    border-top: 1px solid var(--line);
    padding: 8px 12px;
  }
}
/* A cursor that says "hover me": the icon carries the whole message and nothing about an icon otherwise
   suggests there is more behind it. */
.rf-tabs__note { cursor: help; }

/* min-width:0 is what lets this shrink inside the header's action group; without it the box refuses to go
   below the width of everything in it and nothing ever wraps. */
.rf-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  min-width: 0;
}

/* The save indicator. It reports; it does not act — so it takes the header's STATUS size rather than its
   button size, and comes out a dot rather than a square that would sit among the buttons looking like
   one more of them. Round, because it carries an icon and no words: the badges beside it are pills for
   the same reason, and neither is the rounded rectangle a button is. Both numbers are inherited down the
   actions row, so this stays in step with them without repeating either here. */
.rf-save {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: var(--dh-status-height, 24px);
  height: var(--dh-status-height, 24px);
  border-radius: 50%;
}
.rf-save--idle { border: 1px solid var(--line); color: var(--ink-500); }
.rf-save--busy { background: var(--teal-050); color: var(--teal-900); }
.rf-save--ok { background: #e8f5ec; color: #1b5e34; }
.rf-save--warn { background: #fff4e5; color: #8a5300; }
.rf-save--bad { background: #fdecea; color: #8a1c12; }

.rf-alert {
  border-radius: 10px;
}
.rf-alert--warn { background: #fff4e5; color: #8a5300; }
.rf-alert--reject { background: #fdecea; color: #8a1c12; }
.rf-alert--lock { background: var(--teal-050); color: var(--teal-900); }

/* A phone spends the panel gutter on the fields instead, and the header actions line up with the
   breadcrumb above them once they have a line of their own. */
@media (max-width: 599px) {
  .rf-card :deep(.q-tab-panel) {
    padding: 12px 10px;
  }
  .rf-head {
    justify-content: flex-start;
  }
}

</style>

<style>
/* Unscoped, because it is set on <body>: while the divider is being dragged the pointer travels over
   text, and without this every pass selects a paragraph and the cursor keeps flicking back to an I-beam. */
body.rf-dragging {
  cursor: col-resize;
  user-select: none;
}
</style>
