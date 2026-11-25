import { reactive } from "vue";
import { remsApi } from "services/api";

// The numbers beside the COP menu items: what is waiting on the signed-in user in each list. One shared
// copy, so the menu shows what the last page to refresh it read.
const counts = reactive({ myRequests: 0, copReview: 0, approvals: 0 });
let inFlight = null;

const refresh = () => {
  // One read at a time: a list load and a route change often ask in the same moment.
  inFlight ??= remsApi.menuCounts()
    .then((res) => {
      counts.myRequests = res?.myRequests || 0;
      counts.copReview = res?.copReview || 0;
      counts.approvals = res?.approvals || 0;
    })
    .catch(() => { /* the menu still works without its numbers */ })
    .finally(() => { inFlight = null; });
  return inFlight;
};

export function useRemsMenuCounts () {
  return { counts, refresh };
}
