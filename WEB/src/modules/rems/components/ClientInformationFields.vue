<template>
  <div>
    <!-- ENTITY TYPE COMES FIRST, before the client is even named. -->
    <div class="row q-col-gutter-md">
      <app-select
        :model-value="entityType" :options="entityTypeOptions" label="Entity Type" required
        class="col-12 col-sm-6 col-md-4" :readonly="setupReadonly || entityTypeLocked" :clearable="false"
        :hint="entityTypeLocked ? 'Locked — the intake form has been sent.' : ''"
        info="What kind of entity the client is. It is asked first because it decides the rest: which questions the client's intake form asks, which trades the Industry list offers, and how the client's name is captured. Fixed once the form goes out — and an Audit for a Government entity is a Government Audit, which asks for a contract number."
        @update:model-value="onEntityTypeChosen"
      />

      <!-- THE CLIENT SITS BESIDE THE ENTITY TYPE, because the one on the left is what the one on the right
           is FOR: it decides which clients the search offers. -->
      <div :class="nameCols">
        <!-- For a COMPANY this box IS the name, so it is required. -->
        <div class="app-field">
          <app-field-label :label="clientFieldLabel" :required="isOrganisationClient" />
          <q-input
            ref="clientFieldRef"
            v-model="clientQuery"
            outlined dense hide-bottom-space
            :readonly="readonly || clientLocked || !entityType"
            :placeholder="clientFieldPlaceholder"
            autocomplete="off"
            :aria-label="clientFieldLabel"
            :error="isOrganisationClient && attempted && !model.clientName"
            error-message="Search for the client, or type the new client's name."
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
              <!-- The padlock takes the clear button's corner once the invite has gone: it answers the
                   question a missing ✕ would otherwise raise, the same way the email field's does. -->
              <q-icon v-else-if="clientLocked" name="o_lock" size="18px" color="grey-6" />
              <q-icon
                v-else-if="clientQuery && !readonly" name="o_close" color="grey-6" class="cursor-pointer"
                aria-label="Clear client" @click="clearClient"
              />
              <!-- Whether this name resolved to a THF record or will file a new one. -->
              <q-icon name="o_info" size="18px" :color="linkedClient ? 'positive' : 'grey-6'" class="rf-note">
                <q-tooltip anchor="top right" self="bottom right" max-width="300px" :delay="200">
                  {{ clientLinkNote }}
                </q-tooltip>
              </q-icon>
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
                <!-- A company or a person, said in the one place the difference is not obvious from the
                     name. -->
                <q-item-section avatar class="cif-pick__kind">
                  <q-icon
                    :name="client.isOrganisation ? 'o_apartment' : 'o_person'"
                    size="18px" color="grey-7"
                  />
                </q-item-section>
                <q-item-section>
                  <!-- The name AS IT READS — surname first, with the generational particle after it and
                       in bold. -->
                  <q-item-label>
                    <app-name-with-suffix :name="client.name" :suffix="client.suffix" />
                  </q-item-label>
                  <q-item-label caption>
                    {{ client.email || "no email" }} · {{ client.phone || "no phone" }}
                  </q-item-label>
                </q-item-section>
              </q-item>
              <!-- What finding nobody means differs by kind — see noMatchNote. -->
              <q-item v-if="!clientOptions.length">
                <q-item-section class="text-grey-7">{{ noMatchNote }}</q-item-section>
              </q-item>
            </q-list>
          </q-menu>
        </div>
      </div>
    </div>

    <!-- What the client is called and how to reach them — the answers that follow from the pair above. -->
    <div class="row q-col-gutter-md">
      <!-- The generational particle on the name — Jr., Sr., II, III, IV — in a box of its own, and
           AFTER the search box rather than in front. -->
      <!-- THESE BOXES ARE THE NAME. Nothing is copied in from the search box — splitting a typed string
           on its first space files "Van Der Berg" under a surname of "Der Berg". -->
      <template v-if="isIndividualClient">
        <!-- READ-ONLY on a client picked out of the list, and typed only for a new one. -->
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
      </template>

      <!-- Only once the client is KNOWN to be a person. -->
      <app-text-field
        v-if="isIndividualClient && clientIdentitySettled"
        v-model="model.clientNameSuffix" label="Suffix" :class="suffixCols"
        placeholder="Jr." :readonly="readonly || clientLocked"
        :error="suffixTooLong" error-message="A suffix is at most 16 characters."
      >
        <template #append>
          <q-icon v-if="clientLocked" name="o_lock" size="18px" color="grey-6" />
          <q-btn
            v-else-if="!readonly" flat dense round size="sm" icon="o_arrow_drop_down" color="grey-7"
            aria-label="Suffix suggestions"
          >
            <q-menu anchor="bottom end" self="top end" auto-close>
              <q-list dense style="min-width: 150px;">
                <q-item
                  v-for="opt in SUFFIX_OPTIONS" :key="opt.value"
                  clickable :active="model.clientNameSuffix === opt.value"
                  active-class="bg-grey-2 text-primary"
                  @click="model.clientNameSuffix = opt.value"
                >
                  <q-item-section>
                    <q-item-label>{{ opt.label }}</q-item-label>
                    <q-item-label caption>{{ opt.caption }}</q-item-label>
                  </q-item-section>
                </q-item>
                <q-separator />
                <q-item clickable :disable="!model.clientNameSuffix" @click="model.clientNameSuffix = ''">
                  <q-item-section class="text-grey-7">No suffix</q-item-section>
                </q-item>
              </q-list>
            </q-menu>
          </q-btn>
        </template>
      </app-text-field>

      <!-- Required, not "one of email or mobile": the intake form is emailed, so a request without an
           address has nowhere to send the thing the whole request exists to collect. -->
      <app-text-field
        v-if="clientIdentitySettled"
        v-model="model.customerEmail" label="Client Email Address" type="email" required
        placeholder="jane@company.com" :class="contactCols"
        :readonly="readonly || clientLocked"
        :error="attempted && !hasEmail"
        error-message="The intake form is emailed to the client — an address is required."
      >
        <!-- The padlock IS the hint now: why the field will not take a keystroke, on the thing that is
             refusing them, instead of a caption line under a row of four fields. -->
        <template v-if="clientLocked" #append>
          <q-icon name="o_lock" size="18px" color="grey-6" class="rf-note">
            <q-tooltip anchor="top right" self="bottom right" max-width="300px" :delay="200">
              Locked — the intake form was sent to this address.
            </q-tooltip>
          </q-icon>
        </template>
      </app-text-field>

      <!-- Country + number: one component, one cell of the row it is given. -->
      <app-phone-input
        v-if="clientIdentitySettled"
        v-model="model.customerMobileNumber" v-model:country="mobileCountry"
        label="Client Phone Number" :class="contactCols" :readonly="readonly || clientLocked"
      />
    </div>

    <!-- How the referral relates to THF's records — DERIVED from what was done above, not asked before
         it. -->
    <div id="rf-type-question" class="rf-question">How does this referral relate to THF's records?</div>

    <div class="rf-chips" role="radiogroup" aria-labelledby="rf-type-question">
      <button
        v-for="opt in typeOptions" :key="opt.value"
        type="button" role="radio" :aria-checked="model.type === opt.value" :disabled="readonly"
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
import { ref, reactive, computed, watch, nextTick, onBeforeUnmount } from "vue";
import { remsApi, mediaApi } from "services/api";
import { useNotify } from "composables/useNotify";
import {
  useRemsMeta, remsIndustryOptions, remsIndustryFitsEntityType,
  REMS_EXISTING_CLIENT_TYPES, REMS_TYPE_BRAND_NEW_CLIENT, REMS_TYPE_EXISTING_CLIENT,
  isIndividualEntityType
} from "modules/rems/useRemsMeta";
import { CLIENT_NAME_SUFFIXES } from "modules/rems/remsContactRoles";
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
import AppMultiFileUpload from "components/common/AppMultiFileUpload.vue";
import AppStoredFileItem from "components/common/AppStoredFileItem.vue";

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
const { typeHint, entityTypeLabel, industryLabel } = useRemsMeta();

// The parent owns the object; this component writes through it. Simpler than a full v-model round-trip
// for a form this size, and it keeps the parent's save path reading one object.
const model = reactive(props.modelValue);

// The top row's columns.
const nameCols = computed(() =>
  props.compact ? "col-12 col-sm-6" : "col-12 col-sm-6 col-md-5");
const suffixCols = computed(() =>
  props.compact ? "col-4 col-sm-3" : "col-4 col-sm-2 col-md-2");
// The two halves of a new individual's name, side by side under the search box that opened them. They
// share a line with each other on anything but a phone, because they are one answer asked in two parts.
const namePartCols = computed(() =>
  props.compact ? "col-6" : "col-6 col-sm-6 col-md-3");
const contactCols = computed(() =>
  props.compact ? "col-12 col-sm-6" : "col-12 col-sm-6 col-md-3");

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

defineExpose({ uploadAttachments });

const hasEmail = computed(() => !!model.customerEmail?.trim());

// ---- Industry, narrowed by the entity type ----
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
    "on the Entity Type, so the list is empty until that is chosen."));

// What just happened to a stored industry the new entity type does not offer. A field that empties itself
// with no explanation reads as data lost rather than as an answer that stopped applying.
const industryCleared = ref("");
// The cleared-industry note when there is one, otherwise the reason an empty picker is empty — a dropdown
// that opens on nothing reads as broken unless something says why.
const industryHint = computed(() =>
  industryCleared.value ||
  (props.entityType ? "" : "Choose an Entity Type first — it decides which trades are offered."));

// Changing the entity type can strand the industry: "Retail" is not a trade a Government entity is in, and
// leaving it would store a pair the picker cannot even show.
const onEntityTypeChosen = (value) => {
  // The CLIENT goes first, before the new entity type is announced.
  const wasIndividual = isIndividualEntityType(props.entityType);
  const willBeIndividual = isIndividualEntityType(value);
  if (!!props.entityType && wasIndividual !== willBeIndividual) resetClient();

  emit("update:entityType", value);
  industryCleared.value = "";
  if (remsIndustryFitsEntityType(value, props.industry)) return;
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

// The suffix suggestions, and the one thing that can be wrong with a free-text suffix.
const SUFFIX_OPTIONS = CLIENT_NAME_SUFFIXES;
const suffixTooLong = computed(() => (model.clientNameSuffix?.trim().length || 0) > 16);

// ---- Client lookup ----
// Holds the name for an organisation; for an individual, only a linked client's name, else empty.
const clientQuery = ref(
  isIndividualEntityType(props.entityType) && !model.existingClientReferenceId
    ? ""
    : (model.clientName || ""));
const linkedClient = ref(model.existingClientReferenceId
  ? { id: model.existingClientReferenceId, name: model.clientName }
  : null);
const clientOptions = ref([]);
const clientLoading = ref(false);
const clientMenu = ref(false);
const clientSearched = ref(false);
const clientFocused = ref(false);
const activeIndex = ref(-1);
const clientFieldRef = ref(null);
// A saved type is a deliberate answer, whoever gave it — editing the client name must not rewrite it.
const typeChosenByUser = ref(!!model.type);

// Which of the two things typing a name here did — matched a record, or named somebody new.
const clientLinkNote = computed(() => {
  if (props.clientLocked) {
    return "Locked — the intake form has gone out for this client. Changing who they are, or the details " +
      "it was sent to, would leave the request naming somebody nobody wrote to.";
  }
  if (linkedClient.value) {
    return "Linked to a THF client record — this request hangs off the client THF already has on file.";
  }
  if (isIndividualClient.value) {
    return "Optional. Search by name, email or phone to link a client THF already has — their name and " +
      "contact details then fill in below. Leave it empty and the name typed below is filed as a " +
      "brand-new client.";
  }
  if (!model.clientName?.trim()) {
    return "Search by name, email or phone. A name nothing matches is filed as a brand-new client.";
  }
  return "No match / New to THF — this name will be filed as a brand-new client.";
});

// The label and placeholder differ by kind: one holds the client's name, the other looks one up.
const clientFieldLabel = computed(() => (isIndividualClient.value ? "Find an existing client" : "Client"));
const clientFieldPlaceholder = computed(() => {
  if (!props.entityType) return "Choose an Entity Type first — it decides how the client is named.";
  return isIndividualClient.value
    ? "Optional — search name, email or phone…"
    : "Search name, email or phone…";
});

// Follow the parent's re-seed, but not on an unlinked individual — that would rewrite their search term.
watch(() => props.modelValue.clientName, (name) => {
  if (isIndividualClient.value && !linkedClient.value) return;
  if ((name || "") !== clientQuery.value) clientQuery.value = name || "";
});

// Every term searches, however short: a minimum length would leave a client actually NAMED in two or three
// characters unfindable by typing their name.
const LOOKUP_DEBOUNCE_MS = 500;
let lookupTimer = null;
// Bumped on every query so a slow response for an abandoned term cannot land on top of a newer one.
let lookupSeq = 0;

const runLookup = (term) => {
  clearTimeout(lookupTimer);
  lookupSeq += 1;
  const seq = lookupSeq;
  clientSearched.value = false;
  if (!term) {
    clientLoading.value = false;
    clientOptions.value = [];
    clientMenu.value = false;
    return;
  }
  clientLoading.value = true;
  lookupTimer = setTimeout(async () => {
    let items = [];
    try {
      // Narrowed by the entity type answered above: a request for an Individual can only be filed under a
      // person, and one for any other entity type only under a company.
      items = (await remsApi.clientLookup(term, props.entityType || undefined)) || [];
    } catch {
      // A failed lookup reads as "no match": filing the client as new is the only thing an empty result
      // would have allowed anyway.
      items = [];
    }
    if (seq !== lookupSeq) return;
    clientOptions.value = items;
    activeIndex.value = items.length ? 0 : -1;
    clientLoading.value = false;
    clientSearched.value = true;
    clientMenu.value = clientFocused.value;
    linkExactMatchIfSettled();
  }, LOOKUP_DEBOUNCE_MS);
};

// Which kind of client this request can be filed under, in the words the empty result uses. An entity
// type that has not been answered yet says the neutral thing rather than guessing at one of the two.
const lookupKindLabel = computed(() => {
  if (!props.entityType) return "client";
  return isIndividualEntityType(props.entityType) ? "individual client" : "organisation";
});

// For a company the typed name is what gets filed; for a person the name comes from the boxes below.
const noMatchNote = computed(() => {
  const term = clientQuery.value.trim();
  return isIndividualClient.value
    ? `No match for “${term}” — fill in the name below to file a brand-new individual client.`
    : `No match — “${term}” will be filed as a brand-new ${lookupKindLabel.value}.`;
});

const autoType = (code) => (props.typeOptions.some((o) => o.value === code) ? code : "");

// The request's Type is DERIVED, not asked.
const syncTypeToClient = () => {
  if (linkedClient.value) {
    if (!REMS_EXISTING_CLIENT_TYPES.includes(model.type)) model.type = autoType(REMS_TYPE_EXISTING_CLIENT);
    return;
  }
  if (typeChosenByUser.value) return;
  model.type = model.clientName ? autoType(REMS_TYPE_BRAND_NEW_CLIENT) : "";
};

// ---- What the search box's text becomes ----
// The box is the way in for both kinds of client.
const isIndividualClient = computed(() => isIndividualEntityType(props.entityType));

// A company has no generational particle, so the Suffix box is not offered for one — nor on the picker,
// where an organisation result carries none to fill it with.
const isOrganisationClient = computed(() => !!props.entityType && !isIndividualClient.value);

// The suffix, email and phone are askable as soon as the entity type is answered. Positive test, not
// "is not a company" — that reads as true while the entity type is still blank.
const clientIdentitySettled = computed(() => !!props.entityType);

// A matched client's name is theirs, not this request's.
const nameReadonly = computed(() => props.readonly || props.clientLocked || !!linkedClient.value);
const nameReadonlyNote = computed(() => (props.clientLocked
  ? "Locked — the intake form has been sent."
  : "This is a client THF already has. Their name is edited on their own record, not here — clear the " +
    "client above to file a new one instead."));

// The composed name the rest of the platform identifies this request by, kept in step with whichever boxes
// are on screen.
const composeClientName = () => {
  model.clientName = isOrganisationClient.value
    ? (model.clientCorporateName || "").trim()
    : [model.clientLastName, model.clientFirstName]
      .map((p) => (p || "").trim()).filter(Boolean).join(" ");
  syncTypeToClient();
};

const onNamePartTyped = () => {
  model.clientCorporateName = "";
  composeClientName();
};

const onClientTyped = (val) => {
  const term = (val || "").trim();
  if (linkedClient.value && term !== (linkedClient.value.name || "").trim()) detachClient();

  if (isOrganisationClient.value) {
    // A company's name is one string, and this box is it. Straight to CorporateName, which is also what
    // types the client record as an organisation when it saves.
    model.clientCorporateName = term;
    model.clientFirstName = "";
    model.clientLastName = "";
    composeClientName();
  }
  // For an INDIVIDUAL this box only searches — a search term is not a name, so nothing is copied out of
  // it and emptying it takes no name away.
  runLookup(term);
};

const autofilled = reactive({ email: "", phone: "", suffix: "" });

const sameEmail = (a, b) =>
  String(a || "").trim().toLowerCase() === String(b || "").trim().toLowerCase();

// AppPhoneInput normalises whatever it is handed, so recognising its own autofill cannot be a string
// comparison. Compare the digits from the right, past any dial code.
const samePhone = (a, b) => {
  const x = String(a || "").replace(/\D/g, "");
  const y = String(b || "").replace(/\D/g, "");
  return !!x && !!y && (x.endsWith(y) || y.endsWith(x));
};

const releaseAutofill = () => {
  if (autofilled.suffix && model.clientNameSuffix === autofilled.suffix) model.clientNameSuffix = "";
  if (autofilled.email && sameEmail(model.customerEmail, autofilled.email)) model.customerEmail = "";
  if (autofilled.phone && samePhone(model.customerMobileNumber, autofilled.phone)) {
    model.customerMobileNumber = "";
    mobileCountry.value = DEFAULT_DIAL_CODE;
  }
  autofilled.email = "";
  autofilled.phone = "";
  autofilled.suffix = "";
};

const pickClient = (client) => {
  if (!client || props.readonly) return;
  releaseAutofill();
  linkedClient.value = client;
  clientQuery.value = client.name || "";
  model.existingClientReferenceId = client.id;
  // The name in PARTS, straight off their record — the lookup returns them for exactly this.
  model.clientFirstName = client.firstName || "";
  model.clientLastName = client.lastName || "";
  model.clientCorporateName = client.corporateName || "";
  composeClientName();
  // The particle on THEIR name, brought across with the name it belongs to.
  if (client.suffix && !model.clientNameSuffix?.trim() && !props.clientLocked) {
    model.clientNameSuffix = client.suffix;
    autofilled.suffix = client.suffix;
  }
  if (client.email && !model.customerEmail?.trim() && !props.clientLocked) {
    model.customerEmail = client.email;
    autofilled.email = client.email;
  }
  if (client.phone && !model.customerMobileNumber?.trim()) {
    // Blanked first so THIS client's number decides the country, not the last one's.
    mobileCountry.value = null;
    model.customerMobileNumber = client.phone;
    autofilled.phone = client.phone;
  }
  syncTypeToClient();
  clientMenu.value = false;
  activeIndex.value = -1;
};

const detachClient = () => {
  linkedClient.value = null;
  model.existingClientReferenceId = null;
  releaseAutofill();
  syncTypeToClient();
};

// Taking the client out takes their contact details with them: the address and number on screen are the
// ones that client is reached.
const resetClient = () => {
  clientQuery.value = "";
  model.clientName = "";
  // Every part of the name goes with it, not just the joined string — the parts are what the request is
  // actually saved from now, so leaving them behind would file the cleared client anyway.
  model.clientFirstName = "";
  model.clientLastName = "";
  model.clientCorporateName = "";
  // The suffix belongs to the name it was typed beside, so it goes with it. Left standing, the next
  // client typed into this box would inherit the last one's "Jr.".
  model.clientNameSuffix = "";
  // A manual "brand-new / existing" override belonged to the client being cleared. Released, so the next
  // one derives its own answer rather than inheriting a decision made about somebody else.
  typeChosenByUser.value = false;
  runLookup("");
  detachClient();
  if (!props.clientLocked) model.customerEmail = "";
  model.customerMobileNumber = "";
  mobileCountry.value = DEFAULT_DIAL_CODE;
};

// Clears the whole client for a company, or for a picked person (the details on screen are theirs).
// With nobody picked it is only a search term — the name typed below is not ours to throw away.
const clearClient = () => {
  if (isOrganisationClient.value || linkedClient.value) {
    resetClient();
  } else {
    clientQuery.value = "";
    runLookup("");
  }
  clientFieldRef.value?.focus();
};

// THF treats a name already on file as the same client, so a typed name matching one exactly is linked to
// it rather than filed as somebody new.
const soleExactMatch = computed(() => {
  const name = (model.clientName || "").trim().toLowerCase();
  if (!name) return null;
  const matches = clientOptions.value.filter((c) => (c.name || "").trim().toLowerCase() === name);
  return matches.length === 1 ? matches[0] : null;
});

// Held until they leave the box: linking on each keystroke would grab "Acme" while they were still typing
// "Acme Industries", then unlink on the very next letter.
const linkExactMatchIfSettled = () => {
  if (linkedClient.value || clientFocused.value || !clientSearched.value) return;
  const match = soleExactMatch.value;
  if (!match) return;
  pickClient(match);
  notify.info(`“${match.name}” is already a THF client — linked to their record.`);
};

// Overriding the conclusion above. Marking it chosen is what stops the next keystroke in the search box
// from deriving it away again.
const chooseType = (value) => {
  if (props.readonly) return;
  typeChosenByUser.value = true;
  model.type = value;
  // Saying "brand-new" lets go of whoever was linked: the name on screen is about to be filed as a new
  // client, and leaving a reference to somebody else's record behind it would file it against them.
  if (value === REMS_TYPE_BRAND_NEW_CLIENT && linkedClient.value) detachClient();
};

const openMenuIfResults = () => {
  if (clientOptions.value.length || (clientSearched.value && !!clientQuery.value.trim())) {
    clientMenu.value = true;
  }
};

const onClientFocus = () => {
  if (props.readonly) return;
  clientFocused.value = true;
  openMenuIfResults();
};

const onClientBlur = () => {
  clientFocused.value = false;
  clientMenu.value = false;
  linkExactMatchIfSettled();
};

const moveActive = (delta) => {
  if (!clientMenu.value) {
    openMenuIfResults();
    return;
  }
  const count = clientOptions.value.length;
  if (!count) return;
  activeIndex.value = (activeIndex.value + delta + count) % count;
};

const onClientEnter = () => {
  if (clientMenu.value && activeIndex.value >= 0) pickClient(clientOptions.value[activeIndex.value]);
};

onBeforeUnmount(() => {
  clearTimeout(lookupTimer);
});
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
