import { reactive } from "vue";
import { optionSetApi, EntityType } from "services/api";

// The tenant-configurable REMS option lists, resolved once and shared by every screen.

const SET_KEYS = {
  type: "REMS.Type",
  referralSource: "REMS.ReferralSource",
  status: "REMS.Status",
  // The seven lists below mirror C# enums the workflow branches on.
  formStatus: "REMS.FormStatus",
  clientSubmissionState: "REMS.ClientSubmissionState",
  approverRole: "REMS.ApproverRole",
  approvalStatus: "REMS.ApprovalStatus",
  approvalRoundStatus: "REMS.ApprovalRoundStatus",
  engagementStatus: "REMS.EngagementStatus",
  emailEvent: "REMS.EmailEvent",
  // Three of these read one way in the UI and another here.
  entityType: "REMS.EntityType",
  department: "REMS.Department",
  serviceLine: "REMS.ServiceLine",
  industry: "REMS.Industry",
  billingPeriod: "REMS.BillingPeriod",
  personnelLevel: "REMS.PersonnelLevel",
  // How far a client's RELATED client has got, on the Related Entities list.
  relatedEntityStatus: "REMS.RelatedEntityStatus"
};

// Module-level and shared: these lists are per-tenant and change rarely, so resolving them once beats every
// screen fetching its own copy.
const catalog = reactive(Object.fromEntries(Object.keys(SET_KEYS).map((k) => [k, []])));

let loading = null;

// Which lists actually came back.
const resolved = new Set();
const ALL_KEYS = Object.keys(SET_KEYS);

const resolveOne = async (name) => {
  try {
    const items = await optionSetApi.resolve({ entityType: EntityType.Rems, key: SET_KEYS[name] });
    // An empty set means the tenant emptied the list, not that resolution failed. Assigned either way:
    // an emptied list IS the tenant's answer, and there is no longer a local copy to prefer over it.
    if (items) {
      // Everything a screen needs to RENDER the value, carried on the option itself: what it is called,
      // what it means, what colour its badge is and which icon goes beside it.
      catalog[name] = items.map((i) => ({
        label: i.label,
        value: i.value,
        description: i.description || "",
        backgroundColor: i.backgroundColor || "",
        textColor: i.textColor || "",
        icon: i.icon || ""
      }));
    }
    resolved.add(name);
  } catch (err) {
    // Network or auth failure.
    console.warn(`[REMS] Could not resolve the "${SET_KEYS[name]}" option list; badges will show raw codes until it loads.`, err);
  }
};

// Only the lists still missing: a retry after a partial failure re-asks for those and leaves the ones
// already in hand alone.
const loadAll = async () => {
  await Promise.all(ALL_KEYS.filter((k) => !resolved.has(k)).map(resolveOne));
  // A load that did not resolve every list is not one to remember. Cleared AFTER the await, so callers
  // that arrived mid-flight still share this attempt and only the NEXT one starts again.
  if (resolved.size !== ALL_KEYS.length) {
    loading = null;
  }
};

/** Resolves the lists once per session; safe to call from every screen. */
export function ensureRemsOptionsLoaded () {
  loading ??= loadAll();
  return loading;
}

/** Re-resolves after a tenant switch — the lists are tenant-owned copies, so every one is stale. */
export function reloadRemsOptions () {
  resolved.clear();
  loading = loadAll();
  return loading;
}

if (typeof window !== "undefined") {
  window.addEventListener("tenant-switched", () => { reloadRemsOptions(); });
}

export function useRemsOptionCatalog () {
  ensureRemsOptionsLoaded();
  return catalog;
}
