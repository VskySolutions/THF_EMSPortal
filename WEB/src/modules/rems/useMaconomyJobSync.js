// Sync job now, the same way from every list: read what a press would send, ask, press, say what happened.
// `run` is the endpoint the page presses (the lists use the request's own, the settings page its own);
// `afterwards` is the page's reload, called only once something was pressed.
import { ref } from "vue";
import { remsApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";

const escapeHtml = (value) => String(value ?? "")
  .replace(/&/g, "&amp;")
  .replace(/</g, "&lt;")
  .replace(/>/g, "&gt;")
  .replace(/"/g, "&quot;");

export function useMaconomyJobSync ({ run = (remsId) => remsApi.maconomyJobSyncNow(remsId), afterwards } = {}) {
  const notify = useNotify();
  const { confirm } = useConfirm();
  // The request being posted right now, so only its button spins.
  const syncingJobRems = ref(null);

  // What Maconomy will be sent, as a short list: the press creates a record in another system.
  const describe = (p) => [
    ["Request", `${p.remsNumber} — ${p.clientName}`],
    ["Customer", p.customerNumber],
    ["Template job", p.templateJobNumber],
    ["Project manager", p.projectManagerNumber],
    ["Job name", p.jobName]
  ].map(([label, value]) => `<b>${label}:</b> ${escapeHtml(value || "—")}`).join("<br>");

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
      const ok = await confirm({
        title: "Create the Maconomy job",
        message: "A job is created in Maconomy from the template job, under the client's customer; Maconomy " +
          `assigns the job number.<br><br>${describe(preview)}`,
        confirmLabel: "Create job",
        html: true
      });
      if (!ok) return;
      pressed = true;
      // A failed post comes back as a result with a reason, not an error; only a refusal throws.
      const result = await run(remsId);
      if (result?.status === "failed") {
        notify.error(`${preview.remsNumber}: Maconomy job sync failed. ${result.failureReason || ""}`.trim());
      } else {
        notify.success(`${preview.remsNumber}: Maconomy job ${result?.jobNumber} created.`);
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
