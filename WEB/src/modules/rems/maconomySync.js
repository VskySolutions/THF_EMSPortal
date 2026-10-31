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

// The same for the ENGAGEMENT posted to Maconomy as a job. No pending or linked: the job sync is pressed by
// hand and runs inline, and a job is never linked to an existing one.
export const MACONOMY_JOB_SYNC_LOOKS = Object.freeze({
  notSynced: { label: "No Maconomy job", color: "grey-5", textColor: "white", icon: "o_work_off" },
  syncing: { label: "Posting job to Maconomy", color: "blue-6", textColor: "white", icon: "o_sync" },
  synced: { label: "Maconomy job", color: "positive", textColor: "white", icon: "o_work" },
  failed: { label: "Job sync failed", color: "negative", textColor: "white", icon: "o_work_off" }
});

// The label for a list column's field. A request that is not approved carries no job view and reads as a dash.
export const maconomyJobSyncLabel = (status) => (status ? (MACONOMY_JOB_SYNC_LOOKS[status]?.label || status) : "—");
