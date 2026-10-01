<template>
  <div>
    <!-- THE CLIENT COMES FIRST: who they are decides everything under it, the entity type included. -->
    <div class="row q-col-gutter-md">
      <div :class="searchCols">
        <div class="app-field">
          <app-field-label label="Client" required :info="clientInfo" />

          <!-- A new client has no record to find, so the box says so instead of taking a search. -->
          <q-input
            v-if="addingNew"
            model-value="New client" outlined dense hide-bottom-space readonly aria-label="Client"
          >
            <template #prepend><q-icon name="o_person_add" color="primary" /></template>
            <template #append>
              <q-icon v-if="clientLocked" name="o_lock" size="18px" color="grey-6" />
              <q-btn
                v-else-if="!readonly" flat dense no-caps size="sm" color="primary" icon="o_search"
                label="Search instead" @click="leaveNewClient"
              />
            </template>
          </q-input>

          <q-input
            v-else
            ref="clientFieldRef"
            v-model="clientQuery"
            outlined dense hide-bottom-space
            :readonly="readonly || clientLocked"
            placeholder="Search name, email or phone…"
            autocomplete="off"
            aria-label="Client"
            :error="attempted && !linkedClient && !clientFocused"
            error-message="Search for the client, or add a new one."
            @update:model-value="onClientTyped"
            @focus="onClientFocus"
            @blur="onClientBlur"
            @keydown.down.prevent="moveActive(1)"
            @keydown.up.prevent="moveActive(-1)"
            @keydown.enter.prevent="onClientEnter"
            @keydown.esc="clientMenu = false"
          >
            <template #prepend>
              <q-icon :name="linkedClient ? 'o_verified' : 'o_search'" :color="linkedClient ? 'positive' : 'grey-6'" />
            </template>
            <template #append>
              <q-spinner v-if="clientLoading" size="18px" color="primary" />
              <q-icon v-else-if="clientLocked" name="o_lock" size="18px" color="grey-6" />
              <q-icon
                v-else-if="clientQuery && !readonly" name="o_close" color="grey-6" class="cursor-pointer"
                aria-label="Clear client" @click="clearClient"
              />
            </template>
          </q-input>

          <q-menu
            v-model="clientMenu" fit no-focus no-refocus no-parent-event
            anchor="bottom start" self="top start" :offset="[0, 6]"
          >
            <q-list separator>
              <!-- mousedown.prevent keeps focus in the search box, so choosing a result never fires the
                   blur that closes this menu out from under the click. -->
              <q-item
                v-for="(client, i) in clientOptions" :key="client.id"
                clickable :active="i === activeIndex" active-class="bg-grey-2 text-primary"
                @mousedown.prevent @click="pickClient(client)"
              >
                <q-item-section avatar>
                  <q-icon
                    :name="client.isOrganisation ? 'o_apartment' : 'o_person'"
                    size="18px" color="grey-7"
                  />
                </q-item-section>
                <q-item-section>
                  <q-item-label>
                    <app-name-with-suffix :name="client.name" :suffix="client.suffix" />
                  </q-item-label>
                  <q-item-label caption>{{ client.email || "no email" }}</q-item-label>
                </q-item-section>
                <!-- What tells two clients of one name apart. -->
                <q-item-section side>
                  <app-option-badge v-if="client.entityType" :option="entityTypeOption(client.entityType)" />
                  <span v-else class="text-caption text-grey-6">Not recorded</span>
                </q-item-section>
              </q-item>
              <q-item v-if="!clientOptions.length">
                <q-item-section class="text-grey-7">{{ noMatchNote }}</q-item-section>
              </q-item>
              <q-item v-else-if="clientOptions.length >= LOOKUP_LIMIT" dense>
                <q-item-section class="text-caption text-grey-7">
                  Showing the first {{ LOOKUP_LIMIT }} — keep typing to narrow it down.
                </q-item-section>
              </q-item>
              <!-- The way out of a search that did not find them. -->
              <q-item
                clickable :active="activeIndex === clientOptions.length"
                active-class="bg-grey-2" @mousedown.prevent @click="startNewClient"
              >
                <q-item-section avatar>
                  <q-icon name="o_person_add" size="18px" color="primary" />
                </q-item-section>
                <q-item-section class="text-primary">{{ addNewLabel }}</q-item-section>
              </q-item>
            </q-list>
          </q-menu>
        </div>
      </div>

      <div v-if="canStartNew" class="col-auto">
        <q-btn
          outline no-caps color="primary" icon="o_person_add" label="New client" class="cif-new"
          @click="startNewClient"
        />
      </div>
    </div>

    <!-- Who the client is: read off their record, or typed for a new one. -->
    <div v-if="clientSettled" class="row q-col-gutter-md cif-row">
      <app-select
        :model-value="entityType" :options="offeredEntityTypes" label="Entity Type" required
        :class="entityTypeCols" :readonly="entityTypeReadonly" :clearable="false"
        :hint="entityTypeHint"
        :error="attempted && !entityType" error-message="Choose an entity type."
        info="What kind of entity the client is. It decides which questions the client's intake form asks, which trades the Industry list offers, and how the client's name is captured. A client already on file keeps the entity type on their record, and it is fixed for everyone once the form goes out."
        @update:model-value="onEntityTypeChosen"
      />

      <!-- THESE BOXES ARE THE NAME. Nothing is split out of a search term — splitting a typed string on
           its first space files "Van Der Berg" under a surname of "Der Berg". -->
      <template v-if="isIndividualClient">
        <app-text-field
          v-model="model.clientFirstName" label="First Name" required
          :class="namePartCols" :readonly="nameReadonly"
          :rules="nameRules('First Name')"
          :error="attempted && !model.clientFirstName?.trim()"
          error-message="A first name is required."
          @update:model-value="onNamePartTyped"
        >
          <template v-if="nameReadonly" #append>
            <q-icon name="o_lock" size="18px" color="grey-6" class="rf-note">
              <q-tooltip anchor="top right" self="bottom right" max-width="300px" :delay="200">
                {{ nameReadonlyNote }}
              </q-tooltip>
            </q-icon>
          </template>
        </app-text-field>
        <app-text-field
          v-model="model.clientLastName" label="Last Name" required
          :class="namePartCols" :readonly="nameReadonly"
          :rules="nameRules('Last Name')"
          :error="attempted && !model.clientLastName?.trim()"
          error-message="A last name is required."
          @update:model-value="onNamePartTyped"
        >
          <template v-if="nameReadonly" #append>
            <q-icon name="o_lock" size="18px" color="grey-6" class="rf-note">
              <q-tooltip anchor="top right" self="bottom right" max-width="300px" :delay="200">
                {{ nameReadonlyNote }}
              </q-tooltip>
            </q-icon>
          </template>
        </app-text-field>
        <suffix-field
          :model-value="fieldValue('clientNameSuffix')" :class="suffixCols"
          :readonly="readonly" :locked="clientLocked || recordHolds('clientNameSuffix')"
          :locked-note="lockNote('clientNameSuffix')"
          :error="suffixTooLong" error-message="A suffix is at most 16 characters."
          @update:model-value="onFieldTyped('clientNameSuffix', $event)"
          @blur="commitField('clientNameSuffix')" @picked="commitField('clientNameSuffix')"
        />
      </template>

      <app-text-field
        v-else-if="isOrganisationClient"
        v-model="model.clientCorporateName" label="Client/Entity Name" required
        :class="nameCols" :readonly="nameReadonly"
        :error="attempted && !model.clientCorporateName?.trim()"
        error-message="The client's name is required."
        @update:model-value="onCorporateNameTyped"
        @blur="linkNameOnFile"
      >
        <template v-if="nameReadonly" #append>
          <q-icon name="o_lock" size="18px" color="grey-6" class="rf-note">
            <q-tooltip anchor="top right" self="bottom right" max-width="300px" :delay="200">
              {{ nameReadonlyNote }}
            </q-tooltip>
          </q-icon>
        </template>
      </app-text-field>
    </div>

    <!-- How to reach them. Asked once it is known what kind of client they are. -->
    <div v-if="clientSettled && clientKind" class="row q-col-gutter-md cif-row">
      <!-- Required, not "one of email or mobile": the intake form is emailed, so a request without an
           address has nowhere to send the thing the whole request exists to collect. -->
      <app-text-field
        :model-value="fieldValue('customerEmail')" label="Client Email Address" type="email" required
        placeholder="jane@company.com" :class="contactCols"
        :readonly="fieldLocked('customerEmail')"
        :error="!!emailError" :error-message="emailError"
        @update:model-value="onFieldTyped('customerEmail', $event)"
        @blur="onEmailLeft"
      >
        <template v-if="lockNote('customerEmail')" #append>
          <q-icon name="o_lock" size="18px" color="grey-6" class="rf-note">
            <q-tooltip anchor="top right" self="bottom right" max-width="300px" :delay="200">
              {{ lockNote('customerEmail') }}
            </q-tooltip>
          </q-icon>
        </template>
      </app-text-field>

      <app-phone-input
        v-model:country="mobileCountry" :model-value="fieldValue('customerMobileNumber')"
        label="Client Phone Number" :class="contactCols" :readonly="fieldLocked('customerMobileNumber')"
        @update:model-value="onFieldTyped('customerMobileNumber', $event)"
        @blur="commitField('customerMobileNumber')"
      />
    </div>

    <!-- An email belongs to one client, so whoever already holds it is offered instead. -->
    <div v-if="emailHolder" class="cif-onfile">
      <q-icon name="o_error_outline" size="18px" />
      <span>
        <app-name-with-suffix :name="emailHolder.name" :suffix="emailHolder.suffix" />
        is already on file with this email, and an email belongs to one client.
      </span>
      <q-btn
        flat dense no-caps size="sm" color="primary" label="Use this client"
        @click="pickClient(emailHolder)"
      />
    </div>

    <div v-if="linkedClient && !readonly && !clientLocked" class="rf-hint">
      These details are the client's record. What it already holds is locked; a blank can be filled in here.
    </div>

    <!-- How the referral relates to THF's records — DERIVED from the client above, not asked before it. -->
    <template v-if="clientSettled">
      <div id="rf-type-question" class="rf-question">How does this referral relate to THF's records?</div>

      <div class="rf-chips" role="radiogroup" aria-labelledby="rf-type-question">
        <button
          v-for="opt in typeOptions" :key="opt.value"
          type="button" role="radio" :aria-checked="model.type === opt.value"
          :disabled="typeDisabled(opt.value)"
          class="rf-chip" :class="{ 'rf-chip--on': model.type === opt.value }"
          @click="chooseType(opt.value)"
        >
          {{ opt.label }}
          <template v-if="typeHint(opt.value)">
            <q-icon name="o_info" size="15px" class="rf-chip__info" />
            <q-tooltip anchor="top middle" self="bottom middle" max-width="320px" :delay="300">
              {{ typeHint(opt.value) }}
            </q-tooltip>
          </template>
        </button>
      </div>
      <div v-if="attempted && !model.type" class="rf-hint rf-hint--error">
        Choose how this referral relates to THF's records.
      </div>
      <!-- Why one chip refuses the click — under the row, since a disabled button shows no tooltip. -->
      <div v-else-if="!readonly" class="rf-hint">{{ typeNote }}</div>
    </template>

    <!-- The trade the client is in, and who at THF owns the relationship. -->
    <div class="row q-col-gutter-md q-mt-md">
      <!-- REQUIRED, and narrowed by the entity type ABOVE it — a Government entity is offered the three
           kinds of government and nothing else. -->
      <app-select
        :model-value="industry" :options="offeredIndustries" label="Industry" required
        class="col-12 col-sm-6 col-md-4" :readonly="setupReadonly"
        :hint="industryHint" :info="industryInfo"
        :error="attempted && !!entityType && !industry"
        error-message="Choose the trade this client is in."
        @update:model-value="onIndustryPicked"
      />
      <!-- The Client Service Executive. -->
      <app-select
        :model-value="cseUserId" :options="cseOptions" label="CSE" required
        class="col-12 col-sm-6 col-md-4" :readonly="setupReadonly" :clearable="false" :hint="cseHint"
        :error="attempted && !cseUserId" error-message="Choose a CSE."
        info="Users holding the &quot;CSE&quot; role, assigned on a user's page in Administration → Users. The CSE owns the client relationship and becomes an approver on this request's engagement."
        @update:model-value="$emit('update:cseUserId', $event)"
      />
    </div>

    <!-- Business documents, not "any file at all": this box took an executable as happily as a letter,
         because it named no accepted types at all. -->
    <app-multi-file-upload
      v-if="!readonly"
      v-model="attachments" :label="files.length ? 'Add attachments' : 'Attachments'" class="q-mt-md"
      :max-files="MAX_ATTACHMENT_FILES" :accept="ATTACHMENT_ACCEPT" :max-size-mb="MAX_UPLOAD_MB"
      :hint="attachmentHint"
      :error-message="attachmentError"
    />

    <!-- Already-attached files, so a request being edited says what it is already carrying rather than
         showing an empty picker over documents nobody can see from here. -->
    <div v-if="files.length" class="rf-files q-mt-md">
      <app-field-label label="Attached" />
      <div class="column q-gutter-xs">
        <app-stored-file-item
          v-for="file in files" :key="file.id"
          :file="file" :removable="!readonly" :disable="removingId === file.id"
          @remove="removeFile(file)"
        />
      </div>
    </div>
  </div>
</template>

<script setup>
// Section 1 of the REMS form: who the engagement is for, how to reach them, what kind of entity they are,
// what trade they are in, and which CSE owns them.
import { ref, reactive, computed, watch, nextTick, onMounted, onBeforeUnmount } from "vue";
import { remsApi, mediaApi } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import {
  useRemsMeta, remsIndustryOptions, remsIndustryFitsEntityType,
  REMS_EXISTING_CLIENT_TYPES, REMS_TYPE_BRAND_NEW_CLIENT, REMS_TYPE_EXISTING_CLIENT,
  isIndividualEntityType
} from "modules/rems/useRemsMeta";
import { nameRules } from "utils/personName";
import { dialFromIso, DEFAULT_COUNTRY_ISO } from "composables/useCountries";
import {
  ATTACHMENT_ACCEPT, ATTACHMENT_HINT, MAX_ATTACHMENT_FILES, MAX_ATTACHMENT_TOTAL_MB, MAX_UPLOAD_MB
} from "composables/useFileDrop";

import AppTextField from "components/common/AppTextField.vue";
import AppSelect from "components/common/AppSelect.vue";
import AppPhoneInput from "components/common/AppPhoneInput.vue";
import AppFieldLabel from "components/common/AppFieldLabel.vue";
import AppNameWithSuffix from "components/common/AppNameWithSuffix.vue";
import AppOptionBadge from "components/common/AppOptionBadge.vue";
import AppMultiFileUpload from "components/common/AppMultiFileUpload.vue";
import AppStoredFileItem from "components/common/AppStoredFileItem.vue";
import SuffixField from "modules/rems/components/SuffixField.vue";

const props = defineProps({
  modelValue: { type: Object, required: true },
  readonly: { type: Boolean, default: false },
  // The client's identity is fixed once the intake form has gone out — who they are, the address it was
  // emailed to, and the number on file for them.
  clientLocked: { type: Boolean, default: false },
  typeOptions: { type: Array, default: () => [] },
  // What the request already carries — [{ id, fileName, url }] off the request detail.
  files: { type: Array, default: () => [] },
  attempted: { type: Boolean, default: false },
  // Set while the client's submitted form is open beside this one, which leaves the tab a fraction of the
  // page rather than the whole of it.
  compact: { type: Boolean, default: false },
  // The request as the server last described it, or null while it is still being composed. Names the
  // request being edited and, after each save, says what the client's record now holds.
  saved: { type: Object, default: null },

  // ---- The two classifications, which are NOT part of `model` ----
  // Entity Type belongs to the request's EMS form record and Industry to its engagement, so both are
  // written by endpoints of their own; the page owns the values and v-models them through here.
  setupReadonly: { type: Boolean, default: false },
  entityType: { type: String, default: null },
  entityTypeOptions: { type: Array, default: () => [] },
  // The invite has gone out under this entity type; changing it would ask the client different questions
  // from the ones they were sent.
  entityTypeLocked: { type: Boolean, default: false },
  industry: { type: String, default: null },
  industryOptions: { type: Array, default: () => [] },

  // ---- The CSE, which is not part of `model` either ----
  // It belongs to the request's EMS form record — what the client's invite is minted from — and is
  // saved by the same endpoint as the entity type above, which requires the pair.
  cseUserId: { type: String, default: null },
  cseOptions: { type: Array, default: () => [] },
  cseHint: { type: String, default: "" }
});
// `change` covers the ATTACHMENT picker only.
const emit = defineEmits([
  "update:modelValue", "change", "update:entityType", "update:industry", "update:cseUserId",
  "remove-file"
]);

const notify = useNotify();
const { confirm } = useConfirm();
const { typeHint, entityTypeLabel, entityTypeOption, industryLabel } = useRemsMeta();

// The parent owns the object; this component writes through it. Simpler than a full v-model round-trip
// for a form this size, and it keeps the parent's save path reading one object.
const model = reactive(props.modelValue);

const searchCols = computed(() =>
  props.compact ? "col-12 col-sm" : "col-12 col-sm-8 col-md-6");
const entityTypeCols = computed(() =>
  props.compact ? "col-12 col-sm-6" : "col-12 col-sm-6 col-md-3");
const nameCols = computed(() =>
  props.compact ? "col-12 col-sm-6" : "col-12 col-sm-6 col-md-5");
const suffixCols = computed(() =>
  props.compact ? "col-4 col-sm-3" : "col-4 col-sm-2 col-md-2");
// The two halves of a person's name share a line on anything but a phone: one answer asked in two parts.
const namePartCols = computed(() =>
  props.compact ? "col-6" : "col-6 col-sm-6 col-md-3");
const contactCols = computed(() =>
  props.compact ? "col-12 col-sm-6" : "col-12 col-sm-6 col-md-4");

const DEFAULT_DIAL_CODE = dialFromIso(DEFAULT_COUNTRY_ISO);
const mobileCountry = ref(DEFAULT_DIAL_CODE);
const attachments = ref([]);

// The cap on the SET, not on each file — the picker enforces MAX_UPLOAD_MB per file.
const MAX_TOTAL_BYTES = MAX_ATTACHMENT_TOTAL_MB * 1024 * 1024;

// The shared sentence plus the one thing that is true here and nowhere else.
const attachmentHint = `${ATTACHMENT_HINT} These stay internal to the request — the client never sees them.`;
const attachmentError = computed(() => {
  const total = attachments.value.reduce((sum, f) => sum + (f?.size || 0), 0);
  if (total <= MAX_TOTAL_BYTES) return "";
  return `These files total ${(total / 1024 / 1024).toFixed(1)} MB — the limit is ${MAX_ATTACHMENT_TOTAL_MB} MB across all of them.`;
});

// The file currently being detached, so its row greys out rather than the whole list doing so.
const removingId = ref(null);
const removeFile = (file) => {
  removingId.value = file.id;
  // The page answers by re-seeding `files`; either way the row stops being busy once the list changes.
  emit("remove-file", file, () => { removingId.value = null; });
};

// Uploading happens at the page's save, not as a side effect of picking a file: on a brand-new request
// there is nothing to attach media TO until the request has been created.
let clearingAttachments = false;
watch(attachments, () => { if (!clearingAttachments) emit("change"); }, { deep: true });

// `remsId` is the request the files are filed under on the server — the page passes it because it is
// the page, not this component, that knows whether the request has been created yet.
const uploadAttachments = async (remsId = null) => {
  if (attachmentError.value) throw new Error(attachmentError.value);
  const pending = [...attachments.value];
  if (!pending.length) return [];
  const entity = remsId ? { type: "Rems", id: remsId } : null;
  const media = await Promise.all(pending.map((file) => mediaApi.upload(file, "Attachment", entity)));
  // Cleared only once every upload has landed — a failure leaves the picker as the user left it, so a
  // retried save re-sends the same files rather than silently dropping them.
  clearingAttachments = true;
  attachments.value = [];
  await nextTick();
  clearingAttachments = false;
  return media.map((m) => m?.id).filter(Boolean);
};

// ---- The client: one on file, or a new one ----
// The search term, or the name of the client picked with it.
const clientQuery = ref(model.existingClientReferenceId ? (model.clientName || "") : "");
// `entityType` and `isOrganisation` are undefined on a request opened already linked, until its
// client's record has been read (onMounted).
const linkedClient = ref(model.existingClientReferenceId
  ? { id: model.existingClientReferenceId, name: model.clientName }
  : null);
// A request naming a client it is not linked to is describing a new one.
const addingNew = ref(
  !model.existingClientReferenceId && (!!model.clientName?.trim() || !!props.entityType));
const clientSettled = computed(() => !!linkedClient.value || addingNew.value);
const canStartNew = computed(() => !props.readonly && !props.clientLocked && !clientSettled.value);

const clientOptions = ref([]);
const clientLoading = ref(false);
const clientMenu = ref(false);
const clientSearched = ref(false);
const clientFocused = ref(false);
const lookupFailed = ref(false);
const activeIndex = ref(-1);
const clientFieldRef = ref(null);

const clientInfo = computed(() => {
  if (props.clientLocked) {
    return "Locked — the intake form has gone out for this client. Changing who they are would leave " +
      "the request naming somebody nobody wrote to.";
  }
  if (linkedClient.value) return "Linked to a THF client record — the details below are read off it.";
  if (addingNew.value) return "A client THF does not have yet. The details below file a new record.";
  return "Search by name, email or phone, across every entity type. Pick the client to fill in their " +
    "details, or add a new one.";
});

// A person or an organisation, which decides the boxes the name is asked in. A client on file is what
// their record says; a new one is what the entity type chosen for them says.
const clientKind = computed(() => {
  const onRecord = linkedClient.value?.isOrganisation;
  if (typeof onRecord === "boolean") return onRecord ? "organisation" : "individual";
  if (props.entityType) return isIndividualEntityType(props.entityType) ? "individual" : "organisation";
  if (!linkedClient.value) return "";
  return model.clientCorporateName?.trim() ? "organisation" : "individual";
});
const isIndividualClient = computed(() => clientKind.value === "individual");
const isOrganisationClient = computed(() => clientKind.value === "organisation");

// The name as this component last composed it, so its own composing is not read as a re-seed.
let composedName = model.clientName || "";

// Follow the parent's re-seed of a linked client's name.
watch(() => props.modelValue.clientName, (name) => {
  if (!linkedClient.value || (name || "") === composedName) return;
  clientQuery.value = name || "";
});

// ---- Entity type and industry ----
// A client on file keeps the type their record holds, so it is locked wherever there is one.
const entityTypeReadonly = computed(() => {
  if (props.setupReadonly || props.entityTypeLocked) return true;
  if (!linkedClient.value) return false;
  const onRecord = linkedClient.value.entityType;
  // Not read yet: locked if the request already carries one, which is the safe guess.
  return onRecord === undefined ? !!props.entityType : !!onRecord;
});

const entityTypeHint = computed(() => {
  if (props.entityTypeLocked) return "Locked — the intake form has been sent.";
  if (!linkedClient.value || props.setupReadonly) return "";
  return entityTypeReadonly.value
    ? "From the client's record."
    : "Not on the client's record yet — choose it here.";
});

// A client on file is offered only the types their kind of record can hold.
const offeredEntityTypes = computed(() => {
  if (!linkedClient.value || !clientKind.value) return props.entityTypeOptions;
  return props.entityTypeOptions.filter((o) =>
    isIndividualEntityType(o.value) === isIndividualClient.value || o.value === props.entityType);
});

// The trades this kind of entity is in, out of the tenant's own list.
const offeredIndustries = computed(() =>
  remsIndustryOptions(props.industryOptions, props.entityType, props.industry));

// Said on the field, because a list that has just gone from twenty-nine values to three looks broken
// unless something says why.
const industryInfo = computed(() => (props.entityType
  ? "From the REMS Industry option list (Administration → Option Sets), narrowed to the trades a " +
    `${entityTypeLabel(props.entityType)} entity is in. Some trades belong to more than one ` +
    "entity type and appear under each."
  : "From the REMS Industry option list (Administration → Option Sets). Which trades are offered depends " +
    "on the client's Entity Type, so the list is empty until that is known."));

// What just happened to a stored industry the new entity type does not offer. A field that empties itself
// with no explanation reads as data lost rather than as an answer that stopped applying.
const industryCleared = ref("");
const industryHint = computed(() => {
  if (industryCleared.value || props.entityType) return industryCleared.value;
  return clientSettled.value
    ? "Choose an Entity Type first — it decides which trades are offered."
    : "Choose the client first — their entity type decides which trades are offered.";
});

// Every change of entity type comes through here, chosen by hand or read off a record, so the industry
// is checked against it either way: "Retail" is not a trade a Government entity is in.
const applyEntityType = (value) => {
  if (props.setupReadonly || props.entityTypeLocked) return;
  emit("update:entityType", value || null);
  industryCleared.value = "";
  if (!value || remsIndustryFitsEntityType(value, props.industry)) return;
  const stranded = industryLabel(props.industry);
  emit("update:industry", null);
  industryCleared.value =
    `Industry cleared — ${stranded} is not a trade a ${entityTypeLabel(value)} entity is in.`;
};

// The note has done its job the moment a trade is chosen.
const onIndustryPicked = (value) => {
  industryCleared.value = "";
  emit("update:industry", value);
};

// ---- The name ----
// The composed name the rest of the platform identifies this request by. `kind` is passed by a caller
// that has just decided it, since the entity type it follows reaches the props a tick later.
const composeClientName = (kind = clientKind.value) => {
  model.clientName = kind === "organisation"
    ? (model.clientCorporateName || "").trim()
    : [model.clientLastName, model.clientFirstName]
      .map((p) => (p || "").trim()).filter(Boolean).join(" ");
  composedName = model.clientName;
  syncTypeToClient();
};

const onNamePartTyped = () => {
  model.clientCorporateName = "";
  composeClientName();
};

const onCorporateNameTyped = () => {
  model.clientFirstName = "";
  model.clientLastName = "";
  composeClientName();
};

// A name searched for and not found, held until the entity type says which box takes it. It is only
// ever offered to the one box an organisation's name is asked in — never split into a first and a last.
let searchedName = "";

const onEntityTypeChosen = (value) => {
  // A new client's name is asked in different boxes for a person and for an organisation, so what was
  // typed into the other kind's goes with the change.
  if (!linkedClient.value) {
    const individual = isIndividualEntityType(value);
    if (individual) {
      model.clientCorporateName = "";
    } else {
      model.clientFirstName = "";
      model.clientLastName = "";
      model.clientNameSuffix = "";
      if (!model.clientCorporateName?.trim()) model.clientCorporateName = searchedName;
    }
    searchedName = "";
    composeClientName(individual ? "individual" : "organisation");
  }
  applyEntityType(value);
};

// A matched client's name is theirs, not this request's.
const nameReadonly = computed(() => props.readonly || props.clientLocked || !!linkedClient.value);
const nameReadonlyNote = computed(() => (props.clientLocked
  ? "Locked — the intake form has been sent."
  : "This is a client THF already has. Their name is edited on their own record, not here — clear the " +
    "client above to file a new one instead."));

// ---- What a client's RECORD already holds ----
// A request fills a blank on the record of a client on file and never overwrites what is there, so a
// field the record holds is locked rather than taking edits the server would drop.
const held = reactive({ customerEmail: "", customerMobileNumber: "", clientNameSuffix: "" });
const holdFrom = (record) => {
  held.customerEmail = record?.email || "";
  held.customerMobileNumber = record?.phone || "";
  held.clientNameSuffix = record?.suffix || "";
};
if (linkedClient.value) {
  holdFrom({ email: model.customerEmail, phone: model.customerMobileNumber, suffix: model.clientNameSuffix });
}

const recordHolds = (key) => !!linkedClient.value && !!held[key];
const fieldLocked = (key) => props.readonly || props.clientLocked || recordHolds(key);
const lockNote = (key) => {
  if (props.clientLocked) {
    return key === "customerEmail"
      ? "Locked — the intake form was sent to this address."
      : "Locked — the intake form has been sent.";
  }
  return recordHolds(key)
    ? "From the client's record, which a request cannot change. It is edited on their own record."
    : "";
};

// Whoever already holds the email typed for a NEW client, which the save would refuse.
const emailHolder = ref(null);

// What is typed into a linked client's blank is handed over on LEAVING the box. The first value saved
// is the one their record keeps, so auto-save must not catch it half-typed.
const fieldDraft = reactive({ customerEmail: null, customerMobileNumber: null, clientNameSuffix: null });
const fieldValue = (key) => fieldDraft[key] ?? model[key] ?? "";
const onFieldTyped = (key, value) => {
  if (key === "customerEmail") emailHolder.value = null;
  if (linkedClient.value) fieldDraft[key] = value ?? "";
  else model[key] = value ?? "";
};
const commitField = (key) => {
  if (fieldDraft[key] === null) return;
  model[key] = String(fieldDraft[key]).trim();
  fieldDraft[key] = null;
};
const dropFieldDrafts = () => {
  Object.keys(fieldDraft).forEach((key) => { fieldDraft[key] = null; });
};

// AppPhoneInput normalises whatever it is handed, so telling two numbers apart cannot be a string
// comparison. Compare the digits from the right, past any dial code.
const samePhone = (a, b) => {
  const x = String(a || "").replace(/\D/g, "");
  const y = String(b || "").replace(/\D/g, "");
  return !!x && !!y && (x.endsWith(y) || y.endsWith(x));
};

// After a save the server says what the client's record now holds: a blank this request filled is
// theirs from then on, and a value somebody else filled first is the one that stands.
watch(() => props.saved, (saved) => {
  if (!saved || !linkedClient.value || saved.existingClientReferenceId !== linkedClient.value.id) return;
  holdFrom({ email: saved.customerEmail, phone: saved.customerMobileNumber, suffix: saved.clientNameSuffix });
  if (held.customerEmail && fieldDraft.customerEmail === null) model.customerEmail = held.customerEmail;
  if (held.clientNameSuffix && fieldDraft.clientNameSuffix === null) {
    model.clientNameSuffix = held.clientNameSuffix;
  }
  if (held.customerMobileNumber && fieldDraft.customerMobileNumber === null &&
    !samePhone(model.customerMobileNumber, held.customerMobileNumber)) {
    mobileCountry.value = null;
    model.customerMobileNumber = held.customerMobileNumber;
  }
});

const suffixTooLong = computed(() => String(fieldValue("clientNameSuffix")).trim().length > 16);
const hasEmail = computed(() => !!String(fieldValue("customerEmail")).trim());

const emailError = computed(() => {
  if (emailHolder.value) return "A client is already on file with this email address.";
  return props.attempted && !hasEmail.value
    ? "The intake form is emailed to the client — an address is required."
    : "";
});

// ---- A new client who is already on file ----
// The same two checks the save makes, asked of the server as each box is left.
let emailCheckSeq = 0;
let nameCheckSeq = 0;

// Whether the address typed would become a client's email: a new client's, or the blank on one on file.
const givesEmail = () =>
  addingNew.value || (!!linkedClient.value && !recordHolds("customerEmail"));

const checkEmailOnFile = async () => {
  const email = (model.customerEmail || "").trim();
  emailHolder.value = null;
  if (!givesEmail() || !email || props.readonly) return;
  emailCheckSeq += 1;
  const seq = emailCheckSeq;
  const found = await remsApi.clientOnFile({ email, remsId: props.saved?.id }).catch(() => null);
  // Answered for an address that has since been retyped, or for a client since changed.
  if (seq !== emailCheckSeq || !givesEmail() || (model.customerEmail || "").trim() !== email) return;
  // The client being filled in is not a clash with themselves.
  const holder = found?.byEmail || null;
  emailHolder.value = holder && holder.id !== linkedClient.value?.id ? holder : null;
};

const onEmailLeft = () => {
  commitField("customerEmail");
  checkEmailOnFile();
};

// THF treats an organisation's name already on file as the same client, so a new one typed under it is
// linked to that record rather than filed as a second.
const linkNameOnFile = async () => {
  const name = (model.clientCorporateName || "").trim();
  if (!addingNew.value || !name || props.readonly) return;
  nameCheckSeq += 1;
  const seq = nameCheckSeq;
  const found = await remsApi.clientOnFile({ name, remsId: props.saved?.id }).catch(() => null);
  if (seq !== nameCheckSeq || !addingNew.value || (model.clientCorporateName || "").trim() !== name) return;
  if (!found?.byName) return;
  pickClient(found.byName);
  notify.info(`“${found.byName.name}” is already a THF client — linked to their record.`);
};

// ---- The search ----
// Every term searches, however short: a minimum length would leave a client actually NAMED in two or three
// characters unfindable by typing their name.
const LOOKUP_DEBOUNCE_MS = 500;
// The server's page size. Reaching it means there may be more than are shown.
const LOOKUP_LIMIT = 20;
let lookupTimer = null;
// Bumped on every query so a slow response for an abandoned term cannot land on top of a newer one.
let lookupSeq = 0;

const runLookup = (term) => {
  clearTimeout(lookupTimer);
  lookupSeq += 1;
  const seq = lookupSeq;
  clientSearched.value = false;
  lookupFailed.value = false;
  if (!term) {
    clientLoading.value = false;
    clientOptions.value = [];
    clientMenu.value = false;
    return;
  }
  clientLoading.value = true;
  lookupTimer = setTimeout(async () => {
    let items = [];
    let failed = false;
    try {
      items = (await remsApi.clientLookup(term, props.saved?.id)) || [];
    } catch {
      // Said as a failure, not as "no match": nobody found is what invites filing a client twice.
      failed = true;
    }
    if (seq !== lookupSeq) return;
    clientOptions.value = items;
    lookupFailed.value = failed;
    activeIndex.value = items.length ? 0 : -1;
    clientLoading.value = false;
    clientSearched.value = true;
    clientMenu.value = clientFocused.value;
    linkExactMatchIfSettled();
  }, LOOKUP_DEBOUNCE_MS);
};

const noMatchNote = computed(() => (lookupFailed.value
  ? "The search is not available right now — try again in a moment."
  : `No client matches “${clientQuery.value.trim()}”.`));

const addNewLabel = computed(() => {
  const term = clientQuery.value.trim();
  return term ? `Add “${term}” as a new client` : "Add a new client";
});

// ---- The request's Type, which follows the client ----
const autoType = (code) => (props.typeOptions.some((o) => o.value === code) ? code : "");

// The request's Type is DERIVED, not asked: "existing client" means a THF record is linked, "brand-new"
// means a name with no record behind it. A third answer a tenant has added contradicts neither, so once
// chosen it stands until the client is cleared.
const isDerivedType = (code) =>
  !code || code === REMS_TYPE_BRAND_NEW_CLIENT || REMS_EXISTING_CLIENT_TYPES.includes(code);

const syncTypeToClient = () => {
  if (!isDerivedType(model.type)) return;
  if (linkedClient.value) {
    model.type = autoType(REMS_TYPE_EXISTING_CLIENT);
    return;
  }
  model.type = model.clientName ? autoType(REMS_TYPE_BRAND_NEW_CLIENT) : "";
};

// The chip that would contradict the client above is not on offer: a linked record is never brand-new, and
// an existing client is linked by finding them in the search, not by saying so.
const typeDisabled = (code) => {
  if (props.readonly) return true;
  return linkedClient.value
    ? code === REMS_TYPE_BRAND_NEW_CLIENT
    : REMS_EXISTING_CLIENT_TYPES.includes(code);
};

// Why one chip refuses the click, for the state the client above is in.
const typeNote = computed(() => {
  const lead = "Decided by the client above";
  if (props.clientLocked) return `${lead}, whose details are locked — the intake form has been sent.`;
  if (linkedClient.value) return `${lead}, who is on THF's records. Clear the client to file somebody new.`;
  return `${lead}, who is new to THF. Search instead to link a client already on file.`;
});

const chooseType = (value) => {
  if (typeDisabled(value)) return;
  model.type = value;
};

// ---- Choosing, adding and clearing ----
const pickClient = (client) => {
  if (!client || props.readonly || props.clientLocked) return;
  addingNew.value = false;
  searchedName = "";
  emailHolder.value = null;
  dropFieldDrafts();
  linkedClient.value = client;
  clientQuery.value = client.name || "";
  clientOptions.value = [];
  clientSearched.value = false;
  model.existingClientReferenceId = client.id;
  // Everything on screen becomes THEIR record's — it is what the server keeps for a client on file.
  model.clientFirstName = client.firstName || "";
  model.clientLastName = client.lastName || "";
  model.clientCorporateName = client.corporateName || "";
  model.clientNameSuffix = client.suffix || "";
  model.customerEmail = client.email || "";
  // Blanked first so THIS client's number decides the country, not the last one's.
  mobileCountry.value = client.phone ? null : DEFAULT_DIAL_CODE;
  model.customerMobileNumber = client.phone || "";
  holdFrom(client);
  composeClientName(client.isOrganisation ? "organisation" : "individual");
  applyEntityType(client.entityType);
  clientMenu.value = false;
  activeIndex.value = -1;
};

// Taking the client out takes everything that was theirs with them, the entity type included.
const resetClient = ({ keepQuery = false } = {}) => {
  if (!keepQuery) {
    clientQuery.value = "";
    runLookup("");
  }
  linkedClient.value = null;
  addingNew.value = false;
  searchedName = "";
  emailHolder.value = null;
  dropFieldDrafts();
  holdFrom(null);
  model.existingClientReferenceId = null;
  model.clientName = "";
  composedName = "";
  model.clientFirstName = "";
  model.clientLastName = "";
  model.clientCorporateName = "";
  model.clientNameSuffix = "";
  // Blanked, so the next client derives its own rather than inheriting a decision made about somebody else.
  model.type = "";
  model.customerEmail = "";
  model.customerMobileNumber = "";
  mobileCountry.value = DEFAULT_DIAL_CODE;
  applyEntityType(null);
};

const clearClient = () => {
  resetClient();
  clientFieldRef.value?.focus();
};

const looksLikeEmail = (term) => /^\S+@\S+$/.test(term);
const looksLikePhone = (term) => term.replace(/\D/g, "").length >= 7 && /^[+\d(][\d\s().-]*$/.test(term);

// What was searched for is most of an answer already, so it is put where it belongs.
const startNewClient = () => {
  if (props.readonly || props.clientLocked) return;
  const term = clientQuery.value.trim();
  resetClient();
  addingNew.value = true;
  if (looksLikeEmail(term)) {
    model.customerEmail = term;
    checkEmailOnFile();
  } else if (!looksLikePhone(term)) {
    searchedName = term;
  }
};

const leaveNewClient = async () => {
  const typed = [
    model.clientFirstName, model.clientLastName, model.clientCorporateName,
    model.customerEmail, model.customerMobileNumber
  ].some((v) => (v || "").trim());
  if (typed) {
    const ok = await confirm({
      title: "Search for a client instead",
      message: "The details typed for the new client will be cleared. Continue?",
      confirmLabel: "Clear and search"
    });
    if (!ok) return;
  }
  resetClient();
  await nextTick();
  clientFieldRef.value?.focus();
};

const onClientTyped = (val) => {
  const term = (val || "").trim();
  // Typing over a picked client's name is looking for somebody else.
  if (linkedClient.value && term !== (linkedClient.value.name || "").trim()) {
    resetClient({ keepQuery: true });
  }
  runLookup(term);
};

// A term that IS a client's name, and only one client's, picks them — typing it out in full and moving
// on is as clear a choice as clicking the row.
const soleExactMatch = computed(() => {
  const name = clientQuery.value.trim().toLowerCase();
  if (!name) return null;
  const matches = clientOptions.value.filter((c) => (c.name || "").trim().toLowerCase() === name);
  return matches.length === 1 ? matches[0] : null;
});

// Held until they leave the box: linking on each keystroke would grab "Acme" while they were still typing
// "Acme Industries", then unlink on the very next letter.
const linkExactMatchIfSettled = () => {
  if (clientSettled.value || clientFocused.value || !clientSearched.value) return;
  const match = soleExactMatch.value;
  if (!match) return;
  pickClient(match);
  notify.info(`“${match.name}” is already a THF client — linked to their record.`);
};

const openMenuIfResults = () => {
  if (clientSearched.value && !!clientQuery.value.trim()) clientMenu.value = true;
};

const onClientFocus = () => {
  if (props.readonly || props.clientLocked) return;
  clientFocused.value = true;
  openMenuIfResults();
};

const onClientBlur = () => {
  clientFocused.value = false;
  clientMenu.value = false;
  linkExactMatchIfSettled();
};

// The rows, and the "add a new client" row after them.
const moveActive = (delta) => {
  if (!clientMenu.value) {
    openMenuIfResults();
    return;
  }
  const count = clientOptions.value.length + 1;
  activeIndex.value = (activeIndex.value + delta + count) % count;
};

const onClientEnter = () => {
  if (!clientMenu.value || activeIndex.value < 0) return;
  if (activeIndex.value === clientOptions.value.length) startNewClient();
  else pickClient(clientOptions.value[activeIndex.value]);
};

// A request opened already linked knows its client only by id. Their record is what says which fields
// it holds and which entity type it carries.
onMounted(async () => {
  if (!linkedClient.value || props.readonly || props.clientLocked) return;
  const { id } = linkedClient.value;
  const record = await remsApi.client(id, props.saved?.id).catch(() => null);
  if (!record || linkedClient.value?.id !== id) return;
  linkedClient.value = record;
  holdFrom(record);
  // The record's type stands where this request has none of its own yet.
  if (!props.entityType && record.entityType) applyEntityType(record.entityType);
});

onBeforeUnmount(() => {
  clearTimeout(lookupTimer);
});

// `addingNew` and `emailHolder` are what the page's own checks cannot see from the saved fields alone.
defineExpose({ uploadAttachments, addingNew, emailHolder });
</script>

<style scoped>
/* Field hints: on the field they are about, at the end of it, and only reading themselves out when
   someone asks. */
.rf-note { cursor: help; }

.rf-hint {
  margin-top: 6px;
  font-size: 12px;
  color: var(--ink-500);
}
.rf-hint--error { color: #c10015; }

/* A gutter row pulls itself up by its own gutter, which lands it flush against the row above. */
.cif-row { margin-top: 0; }

/* Level with the search box beside it, which sits under a label this button does not have. */
.cif-new {
  margin-top: 18px;
  height: 40px;
}

/* A client already on file under the email typed for a new one, with the way to use them instead. */
.cif-onfile {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px 10px;
  margin-top: 8px;
  padding: 8px 12px;
  border-radius: 8px;
  background: #fdecea;
  color: #8a1c12;
  font-size: 13px;
}

/* The attached files stack as preview rows (AppStoredFileItem) rather than wrapping as a line of links,
   so each one carries its type icon, its size and its own ✕. */
.rf-files { display: block; }

.rf-question {
  margin: 10px 0 10px;
  font-size: 13px;
  font-weight: 600;
  color: var(--ink-900);
}
/* .rf-typerow wrapped these chips alongside the Parent Client box and kept the two top-aligned so the
   chips did not shift as the box appeared. With the box gone the chips are the whole row and lay
   themselves out. */
.rf-chips { display: flex; flex-wrap: wrap; gap: 10px; }
.rf-chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 9px 16px;
  border: 1px solid var(--line);
  border-radius: 8px;
  background: var(--white);
  color: var(--ink-700);
  font: inherit;
  font-size: 13px;
  line-height: 1.2;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s, color 0.15s;
}
.rf-chip:disabled { cursor: default; opacity: 0.7; }
/* A chip the client above has ruled out, beside one that is live: muted, so it reads as not on offer
   before it is clicked and does nothing. */
.rf-chip:disabled:not(.rf-chip--on) {
  opacity: 1;
  color: var(--ink-300);
  cursor: not-allowed;
}
.rf-chip__info { opacity: 0.5; transition: opacity 0.15s; }
.rf-chip:hover .rf-chip__info,
.rf-chip--on .rf-chip__info { opacity: 0.9; }
/* HOVER IS FOR THE CHIPS THAT ARE NOT SELECTED, and the :not() saying so is load-bearing.
   Without it this selector — three classes — outspecifies `.rf-chip--on:hover`, which is two, and wins.
   It sets only a background, so a hovered SELECTED chip was repainted to near-white while its text stayed
   white: a chip with nothing readable on it. */
.rf-chip:not(:disabled):not(.rf-chip--on):hover {
  border-color: var(--teal-300);
  background: var(--teal-050);
}
.rf-chip:focus-visible { outline: 2px solid var(--teal-500); outline-offset: 2px; }
.rf-chip--on {
  background: var(--teal-900);
  border-color: var(--teal-900);
  color: var(--white);
}
/* The selected chip still answers the pointer — one step lighter along the same ramp, so it stays dark
   enough for the white text it keeps. A hover that changes nothing reads as a control that is dead. */
.rf-chip--on:not(:disabled):hover {
  background: var(--teal-800);
  border-color: var(--teal-800);
}
</style>
