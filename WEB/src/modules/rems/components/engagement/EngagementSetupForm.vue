<template>
  <div>
    <!-- Core engagement placement + team + fee/realization (AC-REMS-014.5-10). -->
    <q-form ref="formRef" greedy>
      <div class="row q-col-gutter-md">
        <!-- ── What the firm does, where the work sits, and who heads that ──────────────────────── -->
        <app-select
          v-model="core.serviceLine" :options="serviceLineOptions" label="Service Line" required
          class="col-12 col-sm-4" :readonly="!editable" :clearable="false"
          :rules="[requiredRule('a Service Line')]"
          info="From the REMS Service Line option list (Administration → Option Sets). What the firm is actually engaged to do."
        />
        <app-select
          v-model="core.department" :options="deptOptions" label="Department" required class="col-12 col-sm-4"
          :readonly="!editable" :clearable="false" :rules="[requiredRule('a Department')]"
          info="From the REMS Department option list (Administration → Option Sets). The choice decides what else this form asks: CAS is asked how it is billed, Audit needs a signed CAF, Tax a fiscal year end."
        />
        <!-- Read-only Department Director: the selected department's head, resolved as soon as the
             department is picked and written server-side on save (AC-REMS-014.7). -->
        <app-readonly-field
          :model-value="directorName" label="Department Director" placeholder="Not assigned"
          :hint="directorHint" :hint-alert="directorHintAlert" class="col-12 col-sm-4"
        />

        <!-- ── The two people who run it ────────────────────────────────────────────────────────── -->
        <!-- Each is scoped to the ROLE of its own name. -->
        <app-select
          v-model="core.engagementExecutiveId" :options="executiveOptions" label="Engagement Executive"
          required class="col-12 col-sm-6" :readonly="!editable"
          :rules="[requiredRule('an Engagement Executive')]" :hint="executiveHint"
          info="Lists users holding the &quot;Engagement Executive&quot; role, assigned on a user's page in Administration → Users."
        />
        <app-select
          v-model="core.billingManagerId" :options="billingManagerOptions" label="Billing Manager"
          required class="col-12 col-sm-6" :readonly="!editable"
          :rules="[requiredRule('a Billing Manager')]" :hint="billingManagerHint"
          info="Lists users holding the &quot;Billing Manager&quot; role, assigned on a user's page in Administration → Users."
        />

        <!-- ── What it is worth ─────────────────────────────────────────────────────────────────── -->
        <!-- Two fee questions, and an engagement is asked exactly one of them. -->
        <app-text-field
          v-if="showFeeEstimate"
          v-model="core.firstYearFeeEstimate" label="First-Year Fee Estimate" type="number"
          class="col-12 col-sm-6" :readonly="!editable" :rules="feeRules"
        >
          <template #prepend><span class="text-grey-7">$</span></template>
        </app-text-field>
        <app-text-field
          v-if="showEngagementFee"
          v-model="core.engagementFee" label="Engagement Fee" type="number"
          class="col-12 col-sm-6" :readonly="!editable" :rules="engagementFeeRules"
        >
          <template #prepend><span class="text-grey-7">$</span></template>
        </app-text-field>
        <app-text-field
          v-model="core.realizationPercentage" label="% Realization" type="number" required
          :class="realizationCols" :readonly="!editable" :rules="realizationRules"
        >
          <template #append><span class="text-grey-7">%</span></template>
        </app-text-field>

        <!-- ── How it is billed ─────────────────────────────────────────────────────────────────── -->
        <!-- CAS only. -->
        <template v-if="showBilling">
          <app-select
            v-model="core.billingPeriod" :options="billingPeriodOptions" label="Billing Frequency"
            class="col-12 col-sm-4" :readonly="!editable"
            info="From the REMS Billing Frequency option list (Administration → Option Sets)."
          />
          <app-text-field
            v-model="core.billingProcessDescription" label="Description of Billing Process"
            class="col-12 col-sm-8" :readonly="!editable" autogrow
            placeholder="e.g. three progress bills against the fixed fee, the balance on delivery"
            :rules="billingDescriptionRules"
          />
        </template>
      </div>

      <!-- No save button of its own. -->

      <!-- Conditional: Audit and Assurance → required signed CAF PDF upload (AC-REMS-014.11/12). -->
      <q-card v-if="showAudit" flat bordered class="rems-inner q-mt-md">
        <q-card-section class="q-py-sm text-subtitle2 text-primary">
          <q-icon name="o_fact_check" size="18px" class="q-mr-xs" />{{ attestCardTitle }}
        </q-card-section>
        <q-separator />
        <q-card-section>
          <!-- Where the form stands, then the picker, then the document itself — and the document is at
               the BOTTOM in both states. -->
          <q-banner v-if="hasCaf && cafFile" dense class="bg-teal-1 text-teal-9 rounded-borders q-mb-sm">
            <template #avatar><q-icon name="o_swap_horiz" color="teal-9" /></template>
            Saving replaces the signed client-acceptance form on file with the one you have just chosen.
          </q-banner>
          <q-banner v-else-if="hasCaf" dense class="bg-green-1 text-green-9 rounded-borders q-mb-sm">
            <template #avatar><q-icon name="o_verified" color="green-9" /></template>
            A signed client-acceptance form is on file.
          </q-banner>
          <q-banner v-else-if="cafFile" dense class="bg-teal-1 text-teal-9 rounded-borders q-mb-sm">
            <template #avatar><q-icon name="o_upload_file" color="teal-9" /></template>
            The signed client-acceptance form is attached when you save this request.
          </q-banner>
          <q-banner v-else dense class="bg-orange-1 text-orange-9 rounded-borders q-mb-sm">
            <template #avatar><q-icon name="o_warning" color="orange-9" /></template>
            A signed client-acceptance form (PDF) is required before this
            {{ showAssurance ? "assurance" : "audit" }} engagement can be sent for approval.
          </q-banner>
          <app-single-file-upload
            v-if="editable"
            v-model="cafFile" accept=".pdf" :max-size-mb="MAX_UPLOAD_MB"
            :label="hasCaf ? 'Replace signed CAF (PDF)' : 'Upload signed CAF (PDF)'"
            :hint="`PDF only, up to ${MAX_UPLOAD_MB} MB`"
          />
          <!-- The document itself, not just the claim that one exists: the same preview row every other
               saved file gets, and a click opens it in a new tab. -->
          <app-stored-file-item
            v-if="hasCaf" :file="storedCaf" :removable="editable" :disable="removingCaf" class="q-mt-sm"
            @remove="removeCaf"
          />

          <!-- Assurance only. -->
          <template v-if="showAssurance">
            <q-separator class="q-my-md" />
            <div class="row q-col-gutter-md">
              <app-date-field
                v-model="audit.clientFiscalYearEnd" label="Fiscal Year End of Client"
                class="col-12 col-sm-4" :readonly="!editable"
              />
              <div class="col-12 col-sm-4 column justify-center">
                <q-checkbox
                  v-model="audit.adminFeesApply" :disable="!editable"
                  label="Admin fees apply"
                />
              </div>
              <app-text-field
                v-if="audit.adminFeesApply"
                v-model="audit.adminFeesAmount" label="Admin Fees" type="number"
                class="col-12 col-sm-4" :readonly="!editable" :rules="adminFeesRules"
              >
                <template #prepend><span class="text-grey-7">$</span></template>
              </app-text-field>
            </div>
          </template>
        </q-card-section>
      </q-card>

      <!-- Conditional: Government Audit (Department=audit + Entity Type=government) → contract number +
           Florida 1% state-fee flag (AC-REMS-014.13). -->
      <q-card v-if="showGovernment" flat bordered class="rems-inner q-mt-md">
        <q-card-section class="q-py-sm text-subtitle2 text-primary">
          <q-icon name="o_gavel" size="18px" class="q-mr-xs" />Government Audit — Contract
        </q-card-section>
        <q-separator />
        <q-card-section>
          <!-- Same `row q-col-gutter-md` as every other field row; the checkbox centres itself inside its
               own cell so it lines up with the input beside it rather than with that input's label. -->
          <div class="row q-col-gutter-md">
            <app-text-field
              v-model="gov.contractNumber" label="Contract Number" required class="col-12 col-sm-6"
              :readonly="!editable"
            />
            <div class="col-12 col-sm-6 column justify-center">
              <q-checkbox
                v-model="gov.floridaOnePercentStateFeeApplies" :disable="!editable"
                label="Florida 1% state fee applies"
              />
            </div>
          </div>

          <!-- Contract / PO dates copied from the submission — shown read-only for context. -->
          <div v-if="hasContractDates" class="rems-copied q-mt-md">
            <div class="rems-copied__item"><span>Contract Start</span>{{ dateOnly(gov.contractStartDate) }}</div>
            <div class="rems-copied__item"><span>Contract End</span>{{ dateOnly(gov.contractEndDate) }}</div>
            <div class="rems-copied__item"><span>PO Start</span>{{ dateOnly(gov.purchaseOrderStartDate) }}</div>
            <div class="rems-copied__item"><span>PO End</span>{{ dateOnly(gov.purchaseOrderEndDate) }}</div>
          </div>
        </q-card-section>
      </q-card>

      <!-- Conditional: GCS → the purchase order the engagement is set up against, and the level and rate
           it is staffed at. -->
      <q-card v-if="showGcs" flat bordered class="rems-inner q-mt-md">
        <q-card-section class="q-py-sm text-subtitle2 text-primary">
          <q-icon name="o_request_quote" size="18px" class="q-mr-xs" />GCS — Purchase Order &amp; Rate
        </q-card-section>
        <q-separator />
        <q-card-section>
          <div class="row q-col-gutter-md">
            <app-text-field
              v-model="gov.purchaseOrderNumber" label="Purchase Order No." class="col-12 col-sm-6"
              :readonly="!editable" :rules="purchaseOrderNumberRules"
              placeholder="e.g. PO-2026-0184"
            />
            <app-text-field
              v-model="gov.purchaseOrderAmount" label="Purchase Order Amount" type="number"
              class="col-12 col-sm-6" :readonly="!editable" :rules="purchaseOrderAmountRules"
            >
              <template #prepend><span class="text-grey-7">$</span></template>
            </app-text-field>
            <app-date-field
              v-model="gov.purchaseOrderStartDate" label="PO Beginning Date" class="col-12 col-sm-6"
              :readonly="!editable"
            />
            <app-date-field
              v-model="gov.purchaseOrderEndDate" label="PO Ending Date" class="col-12 col-sm-6"
              :readonly="!editable"
            />
            <app-select
              v-model="gov.personnelLevel" :options="personnelLevelOptions" label="Personnel Level"
              class="col-12 col-sm-6" :readonly="!editable"
              info="From the REMS Personnel Level option list (Administration → Option Sets)."
            />
            <app-text-field
              v-model="gov.billRatePerHour" label="Bill Rate / Hour" type="number"
              class="col-12 col-sm-6" :readonly="!editable" :rules="billRateRules"
            >
              <template #prepend><span class="text-grey-7">$</span></template>
            </app-text-field>
          </div>

          <!-- The purchase order itself. -->
          <!-- Same order as the CAF card above: what is on file, the picker, then the document at the
               bottom — which is where the picker previews an unsaved. -->
          <div class="q-mt-md">
            <q-banner v-if="hasPurchaseOrderFile && poFile" dense class="bg-teal-1 text-teal-9 rounded-borders q-mb-sm">
              <template #avatar><q-icon name="o_swap_horiz" color="teal-9" /></template>
              Saving replaces the purchase order on file with the one you have just chosen.
            </q-banner>
            <q-banner v-else-if="poFile" dense class="bg-teal-1 text-teal-9 rounded-borders q-mb-sm">
              <template #avatar><q-icon name="o_upload_file" color="teal-9" /></template>
              The purchase order is attached when you save this request.
            </q-banner>
            <app-single-file-upload
              v-if="editable"
              v-model="poFile" :accept="PURCHASE_ORDER_ACCEPT" :max-size-mb="MAX_UPLOAD_MB"
              :label="hasPurchaseOrderFile ? 'Replace purchase order' : 'Upload purchase order'"
              :hint="`PDF, image, Word or Excel, up to ${MAX_UPLOAD_MB} MB`"
            />
            <!-- The ✕ takes it back off the engagement, as it does on the CAF above: uploading again
                 replaces the order. -->
            <app-stored-file-item
              v-if="hasPurchaseOrderFile" :file="storedPurchaseOrder" :removable="editable"
              :disable="removingPurchaseOrder" class="q-mt-sm" @remove="removePurchaseOrder"
            />
          </div>
        </q-card-section>
      </q-card>

      <!-- Conditional: Tax → fiscal year end + calculated due dates + tax-form checklist
           (AC-REMS-014.14). -->
      <q-card v-if="showTax" flat bordered class="rems-inner q-mt-md">
        <q-card-section class="q-py-sm text-subtitle2 text-primary">
          <q-icon name="o_receipt_long" size="18px" class="q-mr-xs" />Tax — Fiscal Year &amp; Forms
        </q-card-section>
        <q-separator />
        <q-card-section>
          <!-- The two due dates follow from the fiscal year end — the 15th of the fourth month after it,
               and six months past that — and are then EDITABLE. They were read-only until now. -->
          <div class="row q-col-gutter-md">
            <app-date-field
              v-model="tax.fiscalYearEnd" label="Fiscal Year End" class="col-12 col-sm-4" :readonly="!editable"
            />
            <app-date-field
              v-model="tax.originalDueDate" label="Original Due Date" class="col-12 col-sm-4"
              :readonly="!editable" :hint="dueDateHint"
            />
            <app-date-field
              v-model="tax.firstExtensionDueDate" label="First Extension Due Date" class="col-12 col-sm-4"
              :readonly="!editable"
            />
          </div>

          <div class="section-subhead">Tax Forms</div>
          <div v-if="taxFormUnavailable" class="text-caption text-grey-6">
            The tax-form list could not be loaded for your account.
          </div>
          <!-- Three across on a desktop, two on a tablet, one on a phone. -->
          <div v-else class="row q-col-gutter-x-md">
            <q-checkbox
              v-for="opt in taxFormOptions" :key="opt.value" v-model="tax.taxFormIds" :val="opt.value"
              :label="opt.label" :disable="!editable" class="col-12 col-sm-6 col-md-4"
            />
          </div>
        </q-card-section>
      </q-card>
    </q-form>
  </div>
</template>

<script setup>
// The request's engagement setup (AC-REMS-014/015): what the firm does, where the work sits and the mapped
// department director (read-only), the engagement team, and then whatever the chosen DEPARTMENT is asked.
import { ref, computed, watch, nextTick } from "vue";
import { remsApi, mediaApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { formatDateOnly } from "composables/useDateFormat";
import { MAX_UPLOAD_MB } from "composables/useFileDrop";
import {
  isTaxDepartment, isGovernmentAudit, isCasDepartment, isAssuranceDepartment,
  isGcsDepartment, requiresClientAcceptanceForm
} from "modules/rems/useRemsMeta";
import AppSelect from "components/common/AppSelect.vue";
import AppTextField from "components/common/AppTextField.vue";
import AppDateField from "components/common/AppDateField.vue";
import AppReadonlyField from "components/common/AppReadonlyField.vue";
import AppSingleFileUpload from "components/common/AppSingleFileUpload.vue";
import AppStoredFileItem from "components/common/AppStoredFileItem.vue";

const props = defineProps({
  engagement: { type: Object, required: true },
  deptOptions: { type: Array, default: () => [] },
  // Rendered as "Service Line"; still named for the data behind it. See the note at the top of useRemsMeta.
  serviceLineOptions: { type: Array, default: () => [] },
  taxFormOptions: { type: Array, default: () => [] },
  taxFormUnavailable: { type: Boolean, default: false },
  billingPeriodOptions: { type: Array, default: () => [] },
  // How a GCS engagement is staffed (REMS.PersonnelLevel).
  personnelLevelOptions: { type: Array, default: () => [] },
  // Tenant department → director map: [{ department, director: { userId, name } }]. A department's
  // director is its department head, set on the user's detail page.
  departmentDirectors: { type: Array, default: () => [] },
  // Holders of the "Engagement Executive" / "Billing Manager" roles — these two pickers are scoped to the
  // seat they fill rather than to every admin.
  executiveOptions: { type: Array, default: () => [] },
  billingManagerOptions: { type: Array, default: () => [] },
  editable: { type: Boolean, default: true },

  // Read-only here, and owned by the Client Information tab. Present because the Government Audit card
  // below appears only for an Audit department on a Government entity.
  entityType: { type: String, default: null }
});
// `change` says the engagement half has something to save — the page cannot see the local copies below.
const emit = defineEmits(["change"]);
const formRef = ref(null);
const notify = useNotify();
const { confirm } = useConfirm();

// Set while this component is writing to its own state rather than the user: re-seeding from a fresh
// engagement view, or clearing the CAF picker once its file is uploaded.
let syncing = false;

// Calendar dates read MM/DD/YYYY and are never timezone-shifted — see formatDateOnly, which every screen
// showing a DateOnly now shares rather than keeping a copy of.
const dateOnly = formatDateOnly;

/** The tax due dates a fiscal year end implies: the ORIGINAL is the 15th of the fourth month after it, and
    the FIRST EXTENSION is six months past that. */
const deriveDueDates = (fiscalYearEnd) => {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(fiscalYearEnd || ""));
  if (!m) return { originalDueDate: "", firstExtensionDueDate: "" };
  const addMonths = (year, month, count) => {
    const zero = year * 12 + (month - 1) + count;
    return { year: Math.floor(zero / 12), month: (zero % 12) + 1 };
  };
  const pad = (n) => String(n).padStart(2, "0");
  const original = addMonths(Number(m[1]), Number(m[2]), 4);
  const extension = addMonths(original.year, original.month, 6);
  return {
    originalDueDate: `${original.year}-${pad(original.month)}-15`,
    firstExtensionDueDate: `${extension.year}-${pad(extension.month)}-15`
  };
};

// ---- Core engagement fields (local editable copy, re-synced from the source view) ----
const buildCore = (e) => ({
  department: e.department || null,
  // No `industry`: the industry is asked on the Client Information tab and written by the page.
  serviceLine: e.serviceLine || null,
  engagementExecutiveId: e.engagementExecutive?.id || null,
  billingManagerId: e.billingManager?.id || null,
  firstYearFeeEstimate: e.firstYearFeeEstimate ?? "",
  engagementFee: e.engagementFee ?? "",
  realizationPercentage: e.realizationPercentage ?? "",
  billingPeriod: e.billingPeriod || null,
  billingProcessDescription: e.billingProcessDescription ?? ""
});
const core = ref(buildCore(props.engagement));

// One row, two cards: the government audit's contract block and the GCS purchase order.
const buildGov = (g) => ({
  contractNumber: g?.contractNumber || "",
  floridaOnePercentStateFeeApplies: g?.floridaOnePercentStateFeeApplies ?? false,
  contractStartDate: g?.contractStartDate || null,
  contractEndDate: g?.contractEndDate || null,
  originalTerm: g?.originalTerm || null,
  renewalTerms: g?.renewalTerms || null,
  purchaseOrderStartDate: g?.purchaseOrderStartDate || null,
  purchaseOrderEndDate: g?.purchaseOrderEndDate || null,
  purchaseOrderNumber: g?.purchaseOrderNumber || "",
  purchaseOrderAmount: g?.purchaseOrderAmount ?? "",
  personnelLevel: g?.personnelLevel || null,
  billRatePerHour: g?.billRatePerHour ?? ""
});
const gov = ref(buildGov(props.engagement.government));

// The Assurance half of the attest detail. The CAF beside it is not here — it arrives as an upload and is
// linked by an endpoint of its own, which Audit engagements use too.
const buildAudit = (a) => ({
  clientFiscalYearEnd: a?.clientFiscalYearEnd || "",
  adminFeesApply: a?.adminFeesApply ?? false,
  adminFeesAmount: a?.adminFeesAmount ?? ""
});
const audit = ref(buildAudit(props.engagement.audit));

// The two due dates come off the stored row where it has them.
const buildTax = (t) => {
  const fiscalYearEnd = t?.fiscalYearEnd || "";
  const derived = deriveDueDates(fiscalYearEnd);
  return {
    fiscalYearEnd,
    originalDueDate: t?.originalDueDate || derived.originalDueDate,
    firstExtensionDueDate: t?.firstExtensionDueDate || derived.firstExtensionDueDate,
    taxFormIds: [...(t?.taxFormIds || [])]
  };
};
const tax = ref(buildTax(props.engagement.tax));

// Re-sync every local form when the parent adopts a fresh engagement view.
watch(() => props.engagement, (e) => {
  syncing = true;
  core.value = buildCore(e);
  gov.value = buildGov(e.government);
  audit.value = buildAudit(e.audit);
  tax.value = buildTax(e.tax);
  cafOverride.value = undefined;
  govOverride.value = undefined;
  nextTick(() => { syncing = false; });
});

// Changing the fiscal year end re-derives both dates: the reader has just told us what they follow from.
// Typing over either afterwards is what sticks — nothing recomputes until the year end moves again.
watch(() => tax.value.fiscalYearEnd, (fye, previous) => {
  if (syncing || fye === previous) return;
  const derived = deriveDueDates(fye);
  tax.value.originalDueDate = derived.originalDueDate;
  tax.value.firstExtensionDueDate = derived.firstExtensionDueDate;
});

// ---- Conditional visibility keys off the LOCALLY selected department (immediate) ----
// Every one of these reads `core.value.department` rather than the saved engagement, so picking a
// department shows or hides its questions on the spot rather than after a round trip.
const department = computed(() => core.value.department);

// The signed client-acceptance form: Audit and Assurance both.
const showAudit = computed(() => requiresClientAcceptanceForm(department.value));
// The three questions that are Assurance's alone, inside that same card.
const showAssurance = computed(() => isAssuranceDepartment(department.value));
const showTax = computed(() => isTaxDepartment(department.value));
const showGcs = computed(() => isGcsDepartment(department.value));
// The billing pair is asked of CAS engagements and no others (see isCasDepartment).
const showBilling = computed(() => isCasDepartment(department.value));
// The entity type is the page's, not this form's local copy — it is saved by a different endpoint — so
// this reads the prop. Same rule the API applies when the round is routed.
const showGovernment = computed(() => isGovernmentAudit(department.value, props.entityType));

// ---- What it is worth, per department ----
// Assurance prices the engagement; GCS prices neither, because a GCS engagement is worth its purchase order
// times its bill rate.
const showEngagementFee = computed(() => isAssuranceDepartment(department.value));
const showFeeEstimate = computed(() =>
  !isAssuranceDepartment(department.value) && !isGcsDepartment(department.value));
// Realization takes the row on its own where there is no fee beside it.
const realizationCols = computed(() =>
  (showFeeEstimate.value || showEngagementFee.value ? "col-12 col-sm-6" : "col-12"));

const attestCardTitle = computed(() =>
  (showAssurance.value ? "Assurance — Client Acceptance Form & Fees" : "Audit — Client Acceptance Form"));

// Says where the two due dates came from, on the first of them. Only while a fiscal year end is set: with
// none there is nothing to derive from and both boxes are simply blank.
const dueDateHint = computed(() =>
  (tax.value.fiscalYearEnd ? "From the fiscal year end. Change either date if this return differs." : ""));

const departmentChangedUnsaved = computed(() => core.value.department !== props.engagement.department);

// The director the CURRENTLY selected department maps to — its department head. Resolved locally so the
// field answers "who will own this?" the moment a department is picked, not only after the save returns.
const mappedDirectorName = computed(() => {
  const dept = core.value.department;
  if (!dept) return null;
  const row = props.departmentDirectors.find((d) => d.department === dept);
  return row?.director?.name || null;
});

// While the selection is unsaved, show where it is heading; otherwise show what is actually stored.
const directorName = computed(() => (departmentChangedUnsaved.value
  ? mappedDirectorName.value
  : props.engagement.departmentDirector?.name || null));

// The map travels with the workspace, which a request being created does not have yet. Saying a department
// has no head would be a claim this page cannot make from an empty map — it only knows it cannot answer.
const directorsKnown = computed(() => props.departmentDirectors.length > 0);

const directorHint = computed(() => {
  if (!directorsKnown.value) return "Assigned from the selected department's head when you save.";
  if (departmentChangedUnsaved.value) {
    return mappedDirectorName.value
      ? "From the selected department's head — assigned when you save."
      : "The selected department has no head yet — set one on that user's detail page.";
  }
  if (!directorName.value && core.value.department) {
    return "This department has no head yet — set one on that user's detail page, then save again.";
  }
  return "Auto-assigned from the department's head.";
});

// Draw attention while the choice is unsaved, and whenever a department has nobody to direct it.
const directorHintAlert = computed(() =>
  directorsKnown.value && (departmentChangedUnsaved.value || (!!core.value.department && !directorName.value)));

// ---- The signed client-acceptance form on file ----
// What this component has done to the CAF SINCE the engagement it was handed was read: the row returned by
// an upload.
const cafOverride = ref(undefined);
const cafDetail = computed(() =>
  (cafOverride.value === undefined ? props.engagement.audit : cafOverride.value));

const hasCaf = computed(() => !!cafDetail.value?.clientAcceptanceFormMediaId);
// The CAF already on file, as a stored-file row. The media id is what its bytes are fetched by; the name
// is what the row is read as, and a form uploaded before the workspace carried the name still has one.
const storedCaf = computed(() => ({
  mediaId: cafDetail.value?.clientAcceptanceFormMediaId,
  fileName: cafDetail.value?.fileName || "Client Acceptance Form.pdf"
}));

// Taking it back off. Confirmed and written immediately rather than queued with the rest of the form: it
// is a compliance document the approvers read, and it is what the send-for-approval gate checks.
const removingCaf = ref(false);
const removeCaf = async () => {
  if (!props.engagement.id || removingCaf.value) return;
  const ok = await confirm({
    title: "Remove the signed CAF",
    message: `Take "${storedCaf.value.fileName}" off this engagement? An ${showAssurance.value ? "assurance" : "audit"} ` +
      "engagement cannot be sent for approval without one, so you will need to upload a replacement. This " +
      "does not delete the file itself.",
    confirmLabel: "Remove",
    type: "danger"
  });
  if (!ok) return;
  removingCaf.value = true;
  try {
    const view = await remsApi.removeCaf(props.engagement.id);
    cafOverride.value = view?.audit ?? null;
    notify.success("The signed client-acceptance form was removed.");
  } catch (err) {
    notify.error(getApiErrorMessage(err, "That form could not be removed."));
  } finally {
    removingCaf.value = false;
  }
};
const hasContractDates = computed(() =>
  [gov.value.contractStartDate, gov.value.contractEndDate, gov.value.purchaseOrderStartDate, gov.value.purchaseOrderEndDate]
    .some((d) => !!d));

// The GCS purchase-order document, the same way the CAF above is held and shown — including the override,
// and for the same reason.
const govOverride = ref(undefined);
const govDetail = computed(() =>
  (govOverride.value === undefined ? props.engagement.government : govOverride.value));

const hasPurchaseOrderFile = computed(() => !!govDetail.value?.purchaseOrderMediaId);
const storedPurchaseOrder = computed(() => ({
  mediaId: govDetail.value?.purchaseOrderMediaId,
  fileName: govDetail.value?.purchaseOrderFileName || "Purchase Order"
}));

// Taking it back off, the same way the CAF is: confirmed and written immediately rather than queued with
// the rest of the form, because it is a document the approvers read.
const removingPurchaseOrder = ref(false);
const removePurchaseOrder = async () => {
  if (!props.engagement.id || removingPurchaseOrder.value) return;
  const ok = await confirm({
    title: "Remove the purchase order",
    message: `Take "${storedPurchaseOrder.value.fileName}" off this engagement? The approvers read the ` +
      "order this request is set up against, so it will show none until you upload another. This does " +
      "not delete the file itself.",
    confirmLabel: "Remove",
    type: "danger"
  });
  if (!ok) return;
  removingPurchaseOrder.value = true;
  try {
    const view = await remsApi.removePurchaseOrder(props.engagement.id);
    govOverride.value = view?.government ?? null;
    notify.success("The purchase order was removed.");
  } catch (err) {
    notify.error(getApiErrorMessage(err, "That purchase order could not be removed."));
  } finally {
    removingPurchaseOrder.value = false;
  }
};

// Service Line, Department, the engagement team and % Realization are mandatory (they are also the
// backend's send-for-approval prerequisites), so Save & Next cannot pass with any of them blank.
const requiredRule = (what) => (v) => (v !== null && v !== undefined && v !== "") || `Select ${what}`;

// Money: blank is "not known yet", a negative is wrong however early it is typed. One rule, five boxes.
const amountRule = (v) => v === "" || v === null || v === undefined || Number(v) >= 0 || "Enter a valid amount";
const feeRules = [amountRule];
const engagementFeeRules = [amountRule];
const adminFeesRules = [amountRule];
const purchaseOrderAmountRules = [amountRule];
const billRateRules = [amountRule];
// A purchase order's own reference, and it is a REFERENCE — letters, digits, and the separators a PO
// number is written with. Mirrors the column's 64 characters.
const PO_NUMBER_MAX = 64;
const purchaseOrderNumberRules = [
  (v) => !v || v.length <= PO_NUMBER_MAX || `Keep the purchase order number to ${PO_NUMBER_MAX} characters or fewer.`,
  (v) => !v || /^[A-Za-z0-9][A-Za-z0-9 ./-]*$/.test(v) ||
    "Use letters, numbers and the separators - . / only."
];
// A description of how the client is billed, not a treatise. Mirrors the column and the API validator.
const BILLING_DESCRIPTION_MAX = 1000;
const billingDescriptionRules = [
  (v) => (v ?? "").length <= BILLING_DESCRIPTION_MAX ||
    `Keep the billing process to ${BILLING_DESCRIPTION_MAX} characters or fewer.`
];
const realizationRules = [
  (v) => (v !== "" && v !== null && v !== undefined) || "Enter a % Realization",
  (v) => (Number(v) >= 0 && Number(v) <= 100) || "Enter 0–100"
];

// An empty picker means nobody holds the role; say which role so the fix is obvious.
const seatHint = (options, role) => (options.length
  ? ""
  : `Nobody holds the "${role}" role — assign it on a user's page in Administration → Users.`);
const executiveHint = computed(() => seatHint(props.executiveOptions, "Engagement Executive"));
const billingManagerHint = computed(() => seatHint(props.billingManagerOptions, "Billing Manager"));

const toNum = (v) => (v === "" || v === null || v === undefined ? null : Number(v));

// ---- Save, driven by the page ----
// The whole section in one write: the core first (it is what decides whether the conditional cards apply at
// all), then each card that is on screen, then the signed CAF if one is waiting.
const saveSetup = async (engagementId, remsId = null) => {
  if (!validateFormats()) {
    throw new Error(
      "Check the engagement setup: the fee cannot be negative, realization is 0–100%, and the billing " +
      `process description is at most ${BILLING_DESCRIPTION_MAX} characters.`);
  }

  let view = (await remsApi.updateEngagement(engagementId, {
    department: core.value.department,
    // Empty string rather than null for the clearable one: the endpoint reads null as "leave this field
    // alone" and only an empty value clears.
    serviceLine: core.value.serviceLine ?? "",
    engagementExecutiveId: core.value.engagementExecutiveId,
    billingManagerId: core.value.billingManagerId,
    // One fee question per engagement, and only the one that was asked is written. Omitted — not blanked —
    // on the departments that do not ask it, for the reason spelled out under the billing pair below.
    ...(showFeeEstimate.value ? { firstYearFeeEstimate: toNum(core.value.firstYearFeeEstimate) } : {}),
    ...(showEngagementFee.value ? { engagementFee: toNum(core.value.engagementFee) } : {}),
    realizationPercentage: toNum(core.value.realizationPercentage),
    // The billing pair only where it is asked (CAS).
    ...(showBilling.value
      ? {
        billingPeriod: core.value.billingPeriod,
        // Empty string rather than null, like the clearable code above it: the endpoint reads null as
        // "leave this field alone".
        billingProcessDescription: core.value.billingProcessDescription ?? ""
      }
      : {})
  })).engagement;

  // One row behind two cards, so the whole of `gov` goes either way: sending only the card on screen would
  // blank the other card's half of it.
  if (showGovernment.value || showGcs.value) {
    view = await remsApi.updateGovernment(engagementId, {
      ...gov.value,
      purchaseOrderAmount: toNum(gov.value.purchaseOrderAmount),
      billRatePerHour: toNum(gov.value.billRatePerHour)
    });
  }
  if (showAssurance.value) {
    view = await remsApi.updateAuditDetail(engagementId, {
      clientFiscalYearEnd: audit.value.clientFiscalYearEnd || null,
      adminFeesApply: audit.value.adminFeesApply,
      adminFeesAmount: toNum(audit.value.adminFeesAmount)
    });
  }
  if (showTax.value) {
    // Both dates go with the year end. A blank one is filled in server-side by the same rule the two boxes
    // were pre-filled from, so a caller that never touched them still gets the schedule it always got.
    view = await remsApi.updateTax(engagementId, {
      fiscalYearEnd: tax.value.fiscalYearEnd || null,
      originalDueDate: tax.value.originalDueDate || null,
      firstExtensionDueDate: tax.value.firstExtensionDueDate || null,
      taxFormIds: tax.value.taxFormIds
    });
  }
  if (cafFile.value) {
    const media = await mediaApi.upload(
      cafFile.value, "ClientAcceptance", remsId ? { type: "Rems", id: remsId } : null);
    view = await remsApi.uploadCaf(engagementId, media.id);
    // What the card shows from now on.
    cafOverride.value = view?.audit ?? null;
    syncing = true;
    cafFile.value = null;
    await nextTick();
    syncing = false;
  }
  if (poFile.value) {
    const media = await mediaApi.upload(
      poFile.value, "Attachment", remsId ? { type: "Rems", id: remsId } : null);
    view = await remsApi.uploadPurchaseOrder(engagementId, media.id);
    // Same reason as the CAF above: without this the order just uploaded stays invisible on the card
    // that asked for it, because the page will not re-seed this form after an auto-save.
    govOverride.value = view?.government ?? null;
    syncing = true;
    poFile.value = null;
    await nextTick();
    syncing = false;
  }
  return view;
};

// The range rules only. Quasar validates a whole form or nothing, and its rules here also enforce
// "required", so the ranges are re-stated rather than borrowed.
const validateFormats = () => {
  const blank = (v) => v === "" || v === null || v === undefined;
  const inRange = (v, min, max) =>
    blank(v) || (Number.isFinite(Number(v)) && Number(v) >= min && Number(v) <= max);
  const positive = (v) => inRange(v, 0, Number.MAX_SAFE_INTEGER);
  // Each money box is checked only where it is ASKED — and therefore sent.
  return (!showFeeEstimate.value || positive(core.value.firstYearFeeEstimate)) &&
    (!showEngagementFee.value || positive(core.value.engagementFee)) &&
    inRange(core.value.realizationPercentage, 0, 100) &&
    (!showAssurance.value || positive(audit.value.adminFeesAmount)) &&
    (!showGcs.value || (positive(gov.value.purchaseOrderAmount) && positive(gov.value.billRatePerHour))) &&
    (!showBilling.value || (core.value.billingProcessDescription ?? "").length <= BILLING_DESCRIPTION_MAX);
};

// ---- The uploaded documents ----
// Held rather than uploaded on selection: on a brand-new request there is no engagement to link either to
// yet, and on an existing one they belong with the same Save as everything else the user just typed.
const cafFile = ref(null);
const poFile = ref(null);

// A purchase order arrives as whatever the client's procurement system produced — a PDF, a scan, or the
// Word or Excel document it was raised in.
const PURCHASE_ORDER_ACCEPT = ".pdf,.png,.jpg,.jpeg,.doc,.docx,.xls,.xlsx";

// Declared here rather than beside the re-seed watcher above because the file pickers are part of what the
// page saves, and they are not in scope until now.
watch([core, gov, audit, tax, cafFile, poFile], () => {
  if (!syncing) emit("change");
}, { deep: true });

defineExpose({ saveSetup });
</script>

<style scoped>
/* .rems-inner is the shared record vocabulary — see css/rems.scss. */
.rems-copied {
  display: grid;
  /* auto-fit rather than a fixed pair: four dates in two columns on a phone leaves each of them about
     eighty pixels wide, which is narrower than the date they hold. */
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: 6px 24px;
}
.rems-copied__item {
  font-size: 13px;
  color: #2c3540;
}
.rems-copied__item span {
  display: block;
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.03em;
  text-transform: uppercase;
  color: #7a8699;
}
</style>
