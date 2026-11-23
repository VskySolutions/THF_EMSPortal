// Sync job now, the same way from every list: read what a press would send, ask, press, say what happened.
// `run` is the endpoint the page presses (the lists use the request's own, the settings page its own);
// `afterwards` is the page's reload, called only once something was pressed.
import { ref } from "vue";
import { Dialog } from "quasar";
import { remsApi, getApiErrorMessage } from "services/api";
import MaconomyTemplatePickDialog from "modules/rems/components/MaconomyTemplatePickDialog.vue";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";

const escapeHtml = (value) => String(value ?? "")
  .replace(/&/g, "&amp;")
  .replace(/</g, "&lt;")
  .replace(/>/g, "&gt;")
  .replace(/"/g, "&quot;");

export function useMaconomyJobSync ({
  run = (remsId, templateJobNumber) => remsApi.maconomyJobSyncNow(remsId, templateJobNumber), afterwards
} = {}) {
  const notify = useNotify();
  const { confirm } = useConfirm();
  // The request being posted right now, so only its button spins.
  const syncingJobRems = ref(null);

  // What Maconomy will be sent, as a short list: the press creates a record in another system.
  const describe = (p) => [
    ["Request", `${p.remsNumber} — ${p.clientName}`],
    ["Customer", p.customerNumber],
    ["Template job", p.templateJobNumber],
    ["Job type (specification 2)", p.jobTypeValue],
    // The people go as the Maconomy Number on their user record; one without a number is left out.
    ["Project manager (Engagement Executive)", p.projectManagerNumber],
    ["Account manager (CSE)", p.accountManagerNumber],
    ["Employee 6 (Billing Manager)", p.billingManagerNumber],
    ["Commission (Employee 8–10, Amount 1–3)", (p.commission || []).join("; ")],
    ["Left out — no Maconomy Number", (p.missingNumbers || []).join(", ")],
    ["Job name", p.jobName],
    // The department's Maconomy number; without one the template job's location stands.
    ["Location", p.locationNumber
      ? `${p.locationNumber} (${p.department})`
      : (p.department ? `none for ${p.department} — the template job's stands` : null)],
    ["Purchase order", p.purchaseOrderNumber],
    ["Purchase order dates", p.purchaseOrderStartDate || p.purchaseOrderEndDate
      ? `${p.purchaseOrderStartDate || "—"} to ${p.purchaseOrderEndDate || "—"}`
      : null],
    ["Marketing methods", p.marketingMethods],
    // The fiscal year end as its month (Popup 2) and its year.
    ["Fiscal year end", p.fiscalYear ? [p.fiscalMonth, p.fiscalYear].filter(Boolean).join(" ") : null],
    // Goes on the job's budget after the create: the FEE line's billing price.
    ["First-year fee estimate (budget, task FEE)", p.feeEstimate == null ? null : Number(p.feeEstimate).toFixed(2)],
    ["Documents", p.jobDocuments ? `${p.jobDocuments} file(s), uploaded to the job once it is created` : null]
  ].map(([label, value]) => `<b>${label}:</b> ${escapeHtml(value || "—")}`).join("<br>");

  // Resolves to the picked template job's number, or null when the dialog is cancelled.
  const pickTemplate = (serviceLine, templates) => new Promise((resolve) => {
    Dialog.create({ component: MaconomyTemplatePickDialog, componentProps: { serviceLine, templates } })
      .onOk((number) => resolve(number))
      .onCancel(() => resolve(null))
      .onDismiss(() => resolve(null));
  });

  const syncJobNow = async (row) => {
    const remsId = row.remsId || row.id;
    syncingJobRems.value = remsId;
    let pressed = false;
    try {
      // Read afresh rather than trusting the row: the settings or the client may have moved since the list loaded.
      const preview = await remsApi.maconomyJobSyncPreview(remsId);
      if (!preview?.sync?.canSyncNow) {
        notify.warning(preview?.sync?.blockedReason || "This request cannot be posted to Maconomy as a job right now.");
        return;
      }
      // No template job could be found for the Service Line: ask which of Maconomy's to use.
      let templateJobNumber = null;
      if (preview.needsTemplate) {
        const templates = await remsApi.maconomyJobTemplates(remsId);
        templateJobNumber = await pickTemplate(preview.serviceLine, templates || []);
        if (!templateJobNumber) return;
        preview.templateJobNumber = templateJobNumber;
      }
      // The job is already there: only its budget's fee line is still owed.
      const ok = await confirm(preview.budgetOnly
        ? {
          title: "Finish the Maconomy engagement",
          message: `Job <b>${escapeHtml(preview.sync.jobNumber)}</b> is already in Maconomy. Its budget's FEE line is ` +
              `set to the First-Year Fee Estimate, <b>${escapeHtml(Number(preview.feeEstimate ?? 0).toFixed(2))}</b>. ` +
              "No second job is created.",
          confirmLabel: "Set budget",
          html: true
        }
        : {
          title: "Create the Maconomy engagement",
          message: "A job is created in Maconomy from the template job, under the client's customer; Maconomy " +
              `assigns the job number.<br><br>${describe(preview)}`,
          confirmLabel: "Create job",
          html: true
        });
      if (!ok) return;
      pressed = true;
      // A failed post comes back as a result with a reason, not an error; only a refusal throws.
      const result = await run(remsId, templateJobNumber);
      if (result?.status === "failed") {
        notify.error(`${preview.remsNumber}: Maconomy engagement sync failed. ${result.failureReason || ""}`.trim());
      } else {
        notify.success(preview.budgetOnly
          ? `${preview.remsNumber}: fee estimate set on Maconomy engagement ${result?.jobNumber}.`
          : `${preview.remsNumber}: Maconomy engagement ${result?.jobNumber} created.`);
      }
    } catch (err) {
      pressed = true;
      notify.error(getApiErrorMessage(err));
    } finally {
      syncingJobRems.value = null;
      if (pressed) await afterwards?.();
    }
  };

  return { syncingJobRems, syncJobNow };
}
