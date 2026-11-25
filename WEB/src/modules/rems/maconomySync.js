// Where posting a request's client to Maconomy has got to: the words and colours for each status, in one
// place so the chip, the list columns and the settings page agree. The status words are the server's
// (RemsMaconomySyncStatuses).
export const MACONOMY_SYNC_LOOKS = Object.freeze({
  notSynced: { label: "Not in Maconomy", color: "grey-5", textColor: "white", icon: "o_cloud_off" },
  pending: { label: "Maconomy: queued", color: "blue-6", textColor: "white", icon: "o_schedule" },
  syncing: { label: "Posting to Maconomy", color: "blue-6", textColor: "white", icon: "o_sync" },
  synced: { label: "Maconomy", color: "positive", textColor: "white", icon: "o_cloud_done" },
  linked: { label: "Maconomy", color: "positive", textColor: "white", icon: "o_link" },
  failed: { label: "Maconomy sync failed", color: "negative", textColor: "white", icon: "o_cloud_off" }
});

// The label for a list column's field: what the column sorts and exports as. A request whose client is not
// posted at all (an existing client, one picked from Maconomy) has no status and reads as a dash.
export const maconomySyncLabel = (status) => (status ? (MACONOMY_SYNC_LOOKS[status]?.label || status) : "—");

// The same for the ENGAGEMENT posted to Maconomy as a job. No linked: a job is never linked to an existing one.
export const MACONOMY_JOB_SYNC_LOOKS = Object.freeze({
  notSynced: { label: "No Maconomy engagement", color: "grey-5", textColor: "white", icon: "o_work_off" },
  pending: { label: "Maconomy engagement: queued", color: "blue-6", textColor: "white", icon: "o_schedule" },
  syncing: { label: "Posting job to Maconomy", color: "blue-6", textColor: "white", icon: "o_sync" },
  synced: { label: "Maconomy engagement", color: "positive", textColor: "white", icon: "o_work" },
  failed: { label: "Job sync failed", color: "negative", textColor: "white", icon: "o_work_off" }
});

// The label for a list column's field. A request that is not approved carries no job view and reads as a dash.
export const maconomyJobSyncLabel = (status) => (status ? (MACONOMY_JOB_SYNC_LOOKS[status]?.label || status) : "—");

// One of the request's FILES as Maconomy holds it — an attachment under the client's customer, the signed
// CAF or the purchase order under the job — as the tag on the file's row. `doc` is the file's `maconomy`
// view ({ status, archiveNumber, documentName, failureReason }); null gives no tag: there is nothing to say
// about a file on a request that is not approved.
export const maconomyDocumentTag = (doc) => {
  switch (doc?.status) {
    case "synced":
      // Maconomy's own name for where the document sits: "Customer 900004", "Job 10234".
      return {
        label: doc.archiveNumber || "In Maconomy",
        color: "positive",
        icon: "o_cloud_done",
        tooltip: `In Maconomy${doc.documentName ? ` as "${doc.documentName}"` : ""}${doc.archiveNumber ? `, in ${doc.archiveNumber}` : ""}.`
      };
    case "syncing":
      return { label: "Uploading to Maconomy", color: "blue-6", icon: "o_sync" };
    case "failed":
      return { label: "Maconomy upload failed", color: "negative", icon: "o_cloud_off", tooltip: doc.failureReason };
    case "notSynced":
      return {
        label: "Not in Maconomy",
        color: "grey-5",
        icon: "o_cloud_off",
        tooltip: "Not uploaded yet. It follows the customer or the job into Maconomy, or goes with Upload documents under Tenant Settings → Maconomy → Job."
      };
    default:
      return null;
  }
};
