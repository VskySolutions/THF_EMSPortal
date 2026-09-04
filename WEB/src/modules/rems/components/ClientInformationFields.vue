<template>
  <div>
    <!-- ENTITY TYPE COMES FIRST, before the client is even named.
         It is the question every other question here is asked in the light of: it decides which trades
         the Industry list offers, which questions the client's own intake form will put to them, and —
         for an individual — how the client's name is asked for at all. Asking it fourth meant filling in
         a name and a trade and then discovering that answering this changed both.
         Fixed once the intake form goes out, which is why it locks rather than disappears. -->
    <div class="row q-col-gutter-md">
      <app-select
        :model-value="industryGroup" :options="industryGroupOptions" label="Entity Type" required
        class="col-12 col-sm-6 col-md-4" :readonly="setupReadonly || industryLocked" :clearable="false"
        :hint="industryLocked ? 'Locked — the intake form has been sent.' : ''"
        info="What kind of entity the client is. It is asked first because it decides the rest: which questions the client's intake form asks, which trades the Industry list offers, and how the client's name is captured. Fixed once the form goes out — and an Audit for a Government entity is a Government Audit, which asks for a contract number."
        @update:model-value="onEntityTypeChosen"
      />

      <!-- THE CLIENT SITS BESIDE THE ENTITY TYPE, because the one on the left is what the one on the
           right is FOR: it decides which clients the search offers, and — once nobody matches — whether
           the name is asked for in one box or three. Read together they are a single question, "who is
           this request for", so they are on a single line.
           The col cell and the field are separate elements on purpose: the results menu is `fit`ted to
           its parent, and a parent carrying the grid gutter's padding would hang the menu 16px wide of
           the box it belongs to. -->
      <div :class="nameCols">
        <!-- THE SEARCH BOX IS THE WAY IN, whichever kind of client this turns out to be. Picking a result
             links the request to that record; typing a name nobody matches files a brand-new client. The
             list is narrowed to the kind this entity type can be filed under — see the lookup call.
             What differs between the two kinds is only where the TYPED name lands. For a company it is
             the name itself, and this box is the only one needed. For a person it is not a name at all
             until it has been split, so the three boxes below open to ask for it properly. -->
        <div class="app-field">
          <app-field-label label="Client" required />
          <q-input
            ref="clientFieldRef"
            v-model="clientQuery"
            outlined dense hide-bottom-space
            :readonly="readonly || clientLocked || !industryGroup"
            :placeholder="industryGroup
              ? 'Search name, email or phone…'
              : 'Choose an Entity Type first — it decides how the client is named.'"
            autocomplete="off"
            aria-label="Client"
            :error="attempted && !model.clientName"
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
              <!-- Whether this name resolved to a THF record or will file a new one. In the corner of the
                   box that decides it, rather than on a divider underneath: it is a note about this
                   field, and as a full-width rule it read like a section heading between the client and
                   the way to reach them. -->
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
                     name. The list is already narrowed to the kind this entity type can be filed under,
                     so this confirms the narrowing rather than making a choice — which is exactly what it
                     is for: a picker that has quietly excluded half the clients should show which half it
                     kept. -->
                <q-item-section avatar class="cif-pick__kind">
                  <q-icon
                    :name="client.isOrganisation ? 'o_apartment' : 'o_person'"
                    size="18px" color="grey-7"
                  />
                </q-item-section>
                <q-item-section>
                  <!-- The name AS IT READS — surname first, with the generational particle after it and in
                       bold. Two clients who differ only by that particle are two different people, and a
                       list offering both of them as "Smith John" asks the partner to pick blind. -->
                  <q-item-label>
                    <app-name-with-suffix :name="client.name" :suffix="client.suffix" />
                  </q-item-label>
                  <q-item-label caption>
                    {{ client.email || "no email" }} · {{ client.phone || "no phone" }}
                  </q-item-label>
                </q-item-section>
              </q-item>
              <q-item v-if="!clientOptions.length">
                <q-item-section class="text-grey-7">
                  No match — “{{ clientQuery.trim() }}” will be filed as a brand-new
                  {{ lookupKindLabel }}.
                </q-item-section>
              </q-item>
            </q-list>
          </q-menu>
        </div>
      </div>
    </div>

    <!-- What the client is called and how to reach them — the answers that follow from the pair above.
         Three across on a desktop and stacked on a phone; once the client has answered, the page shares
         its width with their submitted form and the row folds to two (see `compact`).
         Every field here waits on the row above: the name parts open only when the search finds nobody,
         and the suffix, email and phone wait until it is known WHO the client is at all. On a request
         still being searched, this row is empty — which is the point. -->
    <div class="row q-col-gutter-md">
      <!-- The generational particle on the name — Jr., Sr., II, III, IV — in a box of its own, and AFTER
           the search box rather than in front of it: that is where it is read ("John Smith Jr."). Two
           reasons it is separate at all: the search matches THF's client records, and "John Smith Jr."
           finds nothing where "John Smith" finds the man; and a Person is filed under a given name and a
           family name, neither of which "Jr." is. It is appended to the name wherever the client is shown.
           Free text with the five as suggestions: the list is what most clients need, not all any client
           may have, and a suffix nobody thought to seed is not a reason to file somebody under the wrong
           name. Locked with the rest of the client's identity once the intake form has gone out. -->
      <!-- A NEW INDIVIDUAL, asked for their name in the parts a name has — open only once the search has
           come back with nobody, which is the moment this stops being a search and starts being a new
           client. Seeded from what was typed into the box above, as a suggestion to correct rather than a
           guess to live with: the platform files a person under a given name and a family name, and
           splitting one box on the first space makes "Van Der Berg" a surname of "Der Berg" behind a given
           name of "Van". These two also decide the order every client list reads and sorts in
           ("Smith John Jr."), so guessing at them was guessing at that too. -->
      <template v-if="showIndividualNameFields">
        <!-- READ-ONLY on a client picked out of the list, and typed only for a new one. Renaming somebody
             THF already has is not this request's to do — the server fills blanks on a matched client but
             never overwrites their name, so an edit here would look accepted and save nothing. -->
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

      <!-- Only once the client is KNOWN to be a person. A generational particle belongs to a human name,
           so a company is never asked for one — and neither is a request whose entity type has not been
           answered yet, because until it is there is nothing to say this name will be a person's.
           Positive test (`is an individual`) rather than a negative one (`is not a company`): the
           negative reads as true while the answer is still blank, which is how this box came to be on
           screen before anybody had said what kind of client it belonged to. -->
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
           address has nowhere to send the thing the whole request exists to collect.
           Held back while an individual client is still being searched for — see clientIdentitySettled. -->
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

      <!-- Country + number: one component, one cell of the row it is given. Held back with the email
           beside it while an individual client is still being searched for. -->
      <app-phone-input
        v-if="clientIdentitySettled"
        v-model="model.customerMobileNumber" v-model:country="mobileCountry"
        label="Client Phone Number" :class="contactCols" :readonly="readonly || clientLocked"
      />
    </div>

    <!-- How the referral relates to THF's records — DERIVED from what was done above, not asked before
         it. Picking a client out of the list means THF already has them; a name nobody matched means they
         are new. Both are answers the act of filling the box has already given, so this shows what was
         concluded rather than putting the question a second time.
         Still clickable, because a conclusion is not a lock: a partner who knows this is a second
         engagement for a client whose record has not been found yet can say so, and `typeChosenByUser`
         then keeps their answer from being overwritten by the next keystroke. -->
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

    <!-- The trade the client is in, and who at THF owns the relationship. Neither is a contact detail,
         but both are answers about THIS CLIENT, and the CSE is filed with the entity type on the same
         record and by the same write. Asking them over on the setup tab described one client in two
         places, and made an initiator open a second tab before the form could be sent at all.
         The entity type these two sit under is at the TOP of this tab rather than here — it is asked
         before the client is named, because it decides how the naming itself is asked.
         Both obey the SETUP's edit right rather than this tab's: in the two rework states the setup is
         back with the initiator while the client's details above are not theirs to change. -->
    <div class="row q-col-gutter-md q-mt-md">
      <!-- REQUIRED, and narrowed by the entity type ABOVE it — a Government entity is offered the three
           kinds of government and nothing else. The two do NOT partition cleanly, which is why the map
           behind this (REMS_INDUSTRY_BY_ENTITY_TYPE) is overlapping sets rather than a tree: Health Care
           and Educational Institutions each appear under two entity types.
           It was optional until now. The trade is how an engagement is classified and reported on, and a
           client whose trade nobody recorded at intake is one nobody goes back and records it for — so
           it is asked once, here, while somebody is looking at the client. -->
      <app-select
        :model-value="subIndustry" :options="industryOptions" label="Industry" required
        class="col-12 col-sm-6 col-md-4" :readonly="setupReadonly"
        :hint="industryHint" :info="industryInfo"
        :error="attempted && !!industryGroup && !subIndustry"
        error-message="Choose the trade this client is in."
        @update:model-value="onIndustryPicked"
      />
      <!-- The Client Service Executive. Scoped to the "CSE" role, so an empty picker means nobody holds
           it and the hint names what to assign. It sits beside the entity type because the two are
           written together — the endpoint behind the client's intake form requires the pair. -->
      <app-select
        :model-value="cseUserId" :options="cseOptions" label="CSE" required
        class="col-12 col-sm-6 col-md-4" :readonly="setupReadonly" :clearable="false" :hint="cseHint"
        info="Users holding the &quot;CSE&quot; role, assigned on a user's page in Administration → Users. The CSE owns the client relationship and becomes an approver on this request's engagement."
        @update:model-value="$emit('update:cseUserId', $event)"
      />
    </div>

    <!-- Business documents, not "any file at all": this box took an executable as happily as a letter,
         because it named no accepted types at all. It named no per-file size either, so a 40 MB scan was
         accepted here and refused by the server after it had been uploaded. -->
    <app-multi-file-upload
      v-if="!readonly"
      v-model="attachments" :label="files.length ? 'Add attachments' : 'Attachments'" class="q-mt-md"
      :max-files="MAX_ATTACHMENT_FILES" :accept="ATTACHMENT_ACCEPT" :max-size-mb="MAX_UPLOAD_MB"
      :hint="attachmentHint"
      :error-message="attachmentError"
    />

    <!-- Already-attached files, so a request being edited says what it is already carrying rather than
         showing an empty picker over documents nobody can see from here. Same row the picker above puts
         under a freshly chosen file — the icon for its type, its size, and a click that opens it — so a
         document looks the same before and after it is saved. The ✕ is offered only while the form is
         editable: the wrong document attached to a request is one every approver then reads.

         BELOW the picker, not above it, and that is the whole point: a file waiting to be saved is
         previewed by the picker, which renders its rows underneath the dropzone. With this list on top,
         the same document sat under the dropzone while it was staged and jumped over it the instant the
         auto-save landed — one file appearing to move on its own, for no reason the reader can see. Every
         upload on the platform puts what it is holding underneath itself, staged or saved. -->
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
//
// Client identification is unchanged from the old intake drawer — search and pick, or type a name nobody
// matched and file them as new. No field is treated as a unique key: the same client comes back for a
// second engagement, a third, an audit next year, so nothing here constrains a client to one request.
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
  // emailed to, and the number on file for them. One flag for the three because they lock together: the
  // invite was issued naming this client and sent to these details.
  clientLocked: { type: Boolean, default: false },
  typeOptions: { type: Array, default: () => [] },
  // What the request already carries — [{ id, fileName, url }] off the request detail.
  files: { type: Array, default: () => [] },
  attempted: { type: Boolean, default: false },
  // Set while the client's submitted form is open beside this one, which leaves the tab a fraction of
  // the page rather than the whole of it. It is a width the layout cannot read for itself: the columns
  // below are chosen off the VIEWPORT, and the viewport has not changed — only the share of it this
  // form was given.
  compact: { type: Boolean, default: false },

  // ---- The two classifications, which are NOT part of `model` ----
  // Entity Type belongs to the request's EMS form record and Industry to its engagement, so both are
  // written by endpoints of their own; the page owns the values and v-models them through here. Rendered
  // on this tab because they describe the client (see the template), not because the client record holds
  // them.
  //
  // Their edit right is the SETUP's, not this tab's — hence a second readonly flag rather than reusing
  // `readonly` above.
  setupReadonly: { type: Boolean, default: false },
  industryGroup: { type: String, default: null },
  industryGroupOptions: { type: Array, default: () => [] },
  // The invite has gone out under this entity type; changing it would ask the client different questions
  // from the ones they were sent.
  industryLocked: { type: Boolean, default: false },
  // Labelled "Industry"; still named for the data behind it. See the note at the top of useRemsMeta.
  subIndustry: { type: String, default: null },
  subIndustryOptions: { type: Array, default: () => [] },

  // ---- The CSE, which is not part of `model` either ----
  // It belongs to the request's EMS form record — what the client's invite is minted from — and is saved
  // by the same endpoint as the entity type above, which requires the pair. Asked here rather than on the
  // setup tab because it is the answer to "whose client is this?", and because the intake form cannot be
  // sent without it: an initiator should not have to open a second tab to send the first one's form.
  cseUserId: { type: String, default: null },
  cseOptions: { type: Array, default: () => [] },
  cseHint: { type: String, default: "" }
});
// `change` covers the ATTACHMENT picker only. Every other field on this section writes straight through
// to the object the page owns (see `model` below), so the page watches that and sees those itself; files
// wait here until save time and are invisible to it until then. The two classification updates are
// ordinary v-models: the page owns those values and writes them with endpoints of their own.
// `remove-file` is handed up rather than called here: detaching writes to the server against the request
// id, and it is the page that knows the request exists and owns the `files` list this reads.
const emit = defineEmits([
  "update:modelValue", "change", "update:industryGroup", "update:subIndustry", "update:cseUserId",
  "remove-file"
]);

const notify = useNotify();
const { typeHint, industryGroupLabel, subIndustryLabel } = useRemsMeta();

// The parent owns the object; this component writes through it. Simpler than a full v-model round-trip
// for a form this size, and it keeps the parent's save path reading one object.
const model = reactive(props.modelValue);

// The top row's columns. Four across the full page; the name and its suffix, then the email and the
// phone, once the page is shared with the client's answers. The suffix follows the name and is narrow —
// it is a particle on the end of a name, so it gets a particle's width and never a half-row of its own —
// and the two share a line even on a phone, which is how the pair reads as one answer.
// The client search box, sharing the first row with the Entity Type that decides what it offers. Wider
// than the select beside it: it holds a whole client name, and its results menu is fitted to its width.
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

// The cap on the SET, not on each file — the picker enforces MAX_UPLOAD_MB per file. Checked here because
// AppMultiFileUpload only knows about one file at a time. The numbers are the shared ones so that this
// field and the Universal Features panel cannot promise different things (see useFileDrop).
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
// there is nothing to attach media TO until the request has been created. So the files wait here and the
// parent asks for their media ids when it writes.
// Set while the picker is being emptied by a completed upload — the one change to it that is not the
// user choosing a file, and announcing it would ask the page to save the files it has just saved.
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
// The trades this kind of entity is in, out of the tenant's own list. Whatever is already stored stays
// offered whatever the map says, so opening an older engagement never drops the industry recorded on it.
const industryOptions = computed(() =>
  remsIndustryOptions(props.subIndustryOptions, props.industryGroup, props.subIndustry));

// Said on the field, because a list that has just gone from twenty-nine values to three looks broken
// unless something says why.
const industryInfo = computed(() => (props.industryGroup
  ? "From the REMS Industry option list (Administration → Option Sets), narrowed to the trades a " +
    `${industryGroupLabel(props.industryGroup)} entity is in. Some trades belong to more than one ` +
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
  (props.industryGroup ? "" : "Choose an Entity Type first — it decides which trades are offered."));

// Changing the entity type can strand the industry: "Retail" is not a trade a Government entity is in,
// and leaving it would store a pair the picker cannot even show. So it is cleared — and said out loud.
//
// Done in the PICKER'S handler rather than in a watcher on the prop, deliberately. A watcher cannot tell
// the page seeding this form from the server apart from somebody choosing a different entity type, so on
// any engagement whose stored industry predates this pairing it would clear a saved answer on load — and
// the auto-save would then make that permanent. This only ever runs when a human opens the dropdown.
const onEntityTypeChosen = (value) => {
  // The CLIENT goes first, before the new entity type is announced. Changing between a person and a
  // company changes what a client even is here: the picker offers the other kind, the name is asked for
  // in one box instead of two, and a client already picked is one this request can no longer be filed
  // under. Left standing, a linked individual would sit under an entity type whose picker would never
  // have offered them — and would save that way.
  //
  // Only when the KIND changes. Commercial → Insurance is still a company, and the organisation picked
  // under one is perfectly valid under the other; clearing it there would be throwing away a good answer
  // to make a point.
  const wasIndividual = isIndividualEntityType(props.industryGroup);
  const willBeIndividual = isIndividualEntityType(value);
  if (!!props.industryGroup && wasIndividual !== willBeIndividual) resetClient();

  emit("update:industryGroup", value);
  industryCleared.value = "";
  if (remsIndustryFitsEntityType(value, props.subIndustry)) return;
  const stranded = subIndustryLabel(props.subIndustry);
  emit("update:subIndustry", null);
  industryCleared.value =
    `Industry cleared — ${stranded} is not a trade a ${industryGroupLabel(value)} entity is in.`;
};

// The note has done its job the moment a trade is chosen.
const onIndustryPicked = (value) => {
  industryCleared.value = "";
  emit("update:subIndustry", value);
};

// The suffix suggestions, and the one thing that can be wrong with a free-text suffix. Checked here so
// the field says so while it is being typed rather than the save coming back with a 400 on a name
// particle.
const SUFFIX_OPTIONS = CLIENT_NAME_SUFFIXES;
const suffixTooLong = computed(() => (model.clientNameSuffix?.trim().length || 0) > 16);

// ---- Client lookup ----
const clientQuery = ref(model.clientName || "");
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

// Which of the two things typing a name here did — matched a record, or named somebody new. Worth saying
// because the answer decides what gets filed, and nothing else on screen shows it. An empty box has done
// neither yet, so it explains the field instead of reporting on a name nobody has typed.
const clientLinkNote = computed(() => {
  if (props.clientLocked) {
    return "Locked — the intake form has gone out for this client. Changing who they are, or the details " +
      "it was sent to, would leave the request naming somebody nobody wrote to.";
  }
  if (linkedClient.value) {
    return "Linked to a THF client record — this request hangs off the client THF already has on file.";
  }
  if (!model.clientName?.trim()) {
    return "Search by name, email or phone. A name nothing matches is filed as a brand-new client.";
  }
  return "No match / New to THF — this name will be filed as a brand-new client.";
});

// Keep the box in step when the parent re-seeds after a save or reload.
watch(() => props.modelValue.clientName, (name) => {
  if ((name || "") !== clientQuery.value) clientQuery.value = name || "";
});

// Every term searches, however short: a minimum length would leave a client actually NAMED in two or
// three characters unfindable by typing their name. The debounce keeps the traffic down instead, and the
// server caps the result set at 20 whatever the term. Only an empty box searches for nothing.
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
      // Narrowed by the entity type answered above: a request for an Individual can only be filed under
      // a person, and one for any other entity type only under a company, so the picker offers the kind
      // this request can actually use rather than everybody. Before the entity type is answered it is
      // sent as null and the picker offers both — which is the honest answer at that point, and better
      // than an empty list the reader cannot explain.
      items = (await remsApi.clientLookup(term, props.industryGroup || undefined)) || [];
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
    // The search has come back. If nobody matched and this is an individual, the two name boxes have just
    // opened — fill them from what was typed so the common case needs no retyping.
    if (!linkedClient.value) seedNamePartsFromQuery();
  }, LOOKUP_DEBOUNCE_MS);
};

// Which kind of client this request can be filed under, in the words the empty result uses. An entity
// type that has not been answered yet says the neutral thing rather than guessing at one of the two.
const lookupKindLabel = computed(() => {
  if (!props.industryGroup) return "client";
  return isIndividualEntityType(props.industryGroup) ? "individual client" : "organisation";
});

const autoType = (code) => (props.typeOptions.some((o) => o.value === code) ? code : "");

// The request's Type is DERIVED, not asked. Picking a client out of the list means THF already has them;
// a name nobody matched means they are new. Both are answers the act of filling the box has already
// given, so asking for them again would be asking somebody to say twice what they have just done.
// `typeChosenByUser` still lets a deliberate override stand — the derivation is a default, not a lock.
const syncTypeToClient = () => {
  if (linkedClient.value) {
    if (!REMS_EXISTING_CLIENT_TYPES.includes(model.type)) model.type = autoType(REMS_TYPE_EXISTING_CLIENT);
    return;
  }
  if (typeChosenByUser.value) return;
  model.type = model.clientName ? autoType(REMS_TYPE_BRAND_NEW_CLIENT) : "";
};

// ---- What the search box's text becomes ----
// The box is the way in for both kinds of client. What differs is only where a TYPED name lands once the
// search has come back with nobody:
//   organisation → straight into CorporateName. A company's name is one string and this box holds it.
//   individual   → nothing yet. A person is filed under a given name and a family name, so the two boxes
//                  below open, seeded from what was typed, and THEY are the name.
const isIndividualClient = computed(() => isIndividualEntityType(props.industryGroup));

// A company has no generational particle, so the Suffix box is not offered for one — nor on the picker,
// where an organisation result carries none to fill it with.
const isOrganisationClient = computed(() => !!props.industryGroup && !isIndividualClient.value);

// Open once the search has actually run and found nobody. Before that the reader is still searching, and
// two empty name boxes under a search box they have not finished using is a form getting ahead of them.
// The search has run for an individual and come back with nobody — the moment this stops being a search
// and starts being a new client. Kept apart from `showIndividualNameFields` below because
// `clientIdentitySettled` needs THIS one: the boxes are also shown for a client who was found, and
// defining the two in terms of each other would be circular.
const individualSearchExhausted = computed(() =>
  isIndividualClient.value && !linkedClient.value && clientSearched.value && !!clientQuery.value.trim());

// Whether we yet know WHO this client is — and therefore whether the fields that describe them are worth
// putting on screen.
//
// While an individual is still being searched for, they are not. The suffix, the email and the phone all
// belong to one particular person, and mid-search there is no particular person: pick a result and all
// three arrive filled from that record; find nobody and they belong to a new client whose name has not
// been settled yet. Asking for them in between invites somebody to type the contact details of a client
// the next keystroke replaces.
//
// Written as a list of the cases where the answer IS known, never as "not one of the cases where it is
// not". A negative test reads as true while the entity type is still blank, which is how these fields —
// and the suffix box above them — came to be on screen before anybody had said what kind of client this
// was, let alone who.
const clientIdentitySettled = computed(() => {
  // Nothing at all is known until the entity type is answered: it is what decides whether the box beside
  // it is a name or a search, so before it there is not even a question on screen for these to follow.
  if (!props.industryGroup) return false;
  // A client picked out of the list — all three of these arrive filled from that record.
  if (linkedClient.value) return true;
  // An organisation: the box being typed into IS the name, so there is no searching-then-revealing phase
  // to wait out.
  if (isOrganisationClient.value) return true;
  // An individual: known once the search has come back with nobody and the name boxes have opened.
  return individualSearchExhausted.value;
});

// A person's name, in the two parts it is filed under — shown whenever the client is KNOWN and is a
// person, whether that is because one was picked out of the list or because none was found.
//
// On a picked client they are read-only (see the fields themselves). They are there to show WHO was
// picked: the request lists show the name surname-first, so without these the only place the parts are
// visible is the record itself. Filled from that record by pickClient.
const showIndividualNameFields = computed(() =>
  isIndividualClient.value && clientIdentitySettled.value);

// A matched client's name is theirs, not this request's. ResolveClientPersonAsync fills BLANKS on a
// client somebody picked — an email, a phone, a missing particle — but never renames them, so an editable
// name box here would take a change, look as though it had been accepted, and save nothing.
const nameReadonly = computed(() => props.readonly || props.clientLocked || !!linkedClient.value);
const nameReadonlyNote = computed(() => (props.clientLocked
  ? "Locked — the intake form has been sent."
  : "This is a client THF already has. Their name is edited on their own record, not here — clear the " +
    "client above to file a new one instead."));

// The composed name the rest of the platform identifies this request by, kept in step with whichever
// boxes are on screen. SURNAME FIRST for a person — "Smith John" — which is the same reading the client
// lists sort by and the database composes onto Persons.ClientDisplayName. One client, one name, one
// order: a request that called them "John Smith" here while every list called them "Smith John" was one
// client under two names.
//
// The particle stays out of it, as it does on the server: it is carried beside the name in
// clientNameSuffix and joined on where the name is READ.
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

// A SUGGESTION, not a decision. What was typed into the search box is split on the first space to fill
// the two name boxes the moment they open, so the common case — "John Smith" — needs no retyping, and the
// uncommon one is corrected in boxes the reader can see rather than by a rule they cannot.
// Only ever into empty boxes: a reader who has already corrected the split must not have it undone by the
// next keystroke in the search box above.
const seedNamePartsFromQuery = () => {
  if (!isIndividualClient.value || linkedClient.value) return;
  if (model.clientFirstName?.trim() || model.clientLastName?.trim()) return;
  const term = (clientQuery.value || "").trim();
  if (!term) return;
  const cut = term.indexOf(" ");
  model.clientFirstName = cut === -1 ? term : term.slice(0, cut);
  model.clientLastName = cut === -1 ? "" : term.slice(cut + 1).trim();
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
  } else {
    // A person's name is not settled by this box — the two below settle it. Typing here only re-opens the
    // search, and clearing it clears the name that was being built from the boxes it seeded.
    if (!term) {
      model.clientFirstName = "";
      model.clientLastName = "";
    }
  }
  composeClientName();
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
  // The name in PARTS, straight off their record — the lookup returns them for exactly this. Composing
  // clientName from the parts rather than taking the search result's own string keeps one rule for how a
  // name is built, whether it came from a picker or from two boxes. The two now agree on the ORDER as
  // well, but the picker's `name` has the particle already on the end of it, and this form carries that
  // in a box of its own — taking the string whole would put "Jr." into the name and into the Suffix box.
  model.clientFirstName = client.firstName || "";
  model.clientLastName = client.lastName || "";
  model.clientCorporateName = client.corporateName || "";
  composeClientName();
  // The particle on THEIR name, brought across with the name it belongs to. Without it a request raised
  // against that record reads "John Smith" everywhere beside a list that says "John Smith Jr." — and
  // that particle is the only thing telling him apart from his father, who is also a client. Only into
  // an empty box, and released again if the client is taken back out, exactly as the email and phone
  // below are.
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
// ones that client is reached on, so leaving them standing would send the next client's intake form to
// the last one's inbox. Unconditional, unlike the autofill release above — details typed by hand or
// loaded with a saved request belong to the client that was just removed just as much as autofilled ones
// do. A locked email is the exception: the form has already gone to that address, so it is not ours to
// clear (the field is read-only for the same reason).
// Everything the client answer consists of, put back to nothing. Two callers: the ✕ on the search box,
// and a change of entity type that makes the client on screen one this request can no longer be filed
// under.
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

const clearClient = () => {
  resetClient();
  clientFieldRef.value?.focus();
};

// THF treats a name already on file as the same client, so a typed name matching one exactly is linked to
// it rather than filed as somebody new. Two records under one name is a real ambiguity, so that resolves
// to nothing and the partner picks. The server applies the same rule on save, over the same search.
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
