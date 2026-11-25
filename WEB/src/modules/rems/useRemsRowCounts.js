import { reactive } from "vue";
import { remsApi } from "services/api";
import { useRemsMenuCounts } from "composables/useRemsMenuCounts";

// The counts on a request list's Conversation and Email log row actions. One read for the whole page
// after every load, never per row.
export function useRemsRowCounts () {
  const counts = reactive({});
  const { refresh: refreshMenuCounts } = useRemsMenuCounts();
  let pageIds = [];

  // With no ids it re-reads the page it last read: a dialog that just closed may have added to one.
  const sync = async (ids = pageIds) => {
    // Every list load follows something that may have moved a request: the menu's numbers follow it too.
    refreshMenuCounts();
    pageIds = [...new Set((ids || []).filter(Boolean))];
    if (!pageIds.length) return;
    try {
      const rows = await remsApi.activityCounts(pageIds);
      const found = Object.fromEntries((rows || []).map((r) => [r.remsId, r]));
      pageIds.forEach((id) => {
        counts[id] = { conversation: found[id]?.conversation || 0, emails: found[id]?.emails || 0 };
      });
    } catch {
      // The icons still work without their numbers.
    }
  };

  const conversationCount = (id) => counts[id]?.conversation || 0;
  const emailCount = (id) => counts[id]?.emails || 0;
  const say = (name, n, one, many) => (n ? `${name} — ${n} ${n === 1 ? one : many}` : name);
  const conversationTip = (id) => say("Conversation", conversationCount(id), "message", "messages");
  const emailTip = (id) => say("Email log", emailCount(id), "entry", "entries");

  return { sync, conversationCount, emailCount, conversationTip, emailTip };
}
