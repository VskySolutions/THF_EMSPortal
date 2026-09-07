import { ref, reactive, computed } from "vue";
import { useRouter } from "vue-router";
import { copyToClipboard } from "quasar";
import { ufReminderApi, ufColourApi, ufPdfApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { usePins } from "composables/uf/usePins";
import { useEntityMeta } from "composables/uf/useEntityMeta";
import { ROW_COLOUR_PALETTE } from "composables/uf/useColourCodes";

// Shared Universal Features per-record actions — pin, colour, reminder, copy permalink.
export function useEntityActions (entityType, entityId, label = "record", initialPinned = false) {
  const router = useRouter();
  const notify = useNotify();
  const { confirm } = useConfirm();
  const { typeSlug } = useEntityMeta();
  const { pinned, busy, refresh: refreshPin, toggle: togglePin } = usePins(entityType, entityId, initialPinned);

  // The platform's one palette — shared with the list-row marks (useColourCodes) so a colour picked on a
  // record and a colour picked on its row are the same eight colours.
  const palette = ROW_COLOUR_PALETTE;
  const currentColour = ref(null);

  const reminder = ref(null);
  const reminderOpen = ref(false);
  const savingReminder = ref(false);
  const reminderForm = reactive({ dueAt: "", note: "" });
  const reminderOverdue = computed(() => !!reminder.value?.isOverdue);

  const pdfOpen = ref(false);
  const includeConversation = ref(true);
  const exporting = ref(false);

  const loadColour = async () => {
    try {
      const map = await ufColourApi.batch(entityType, [entityId]);
      currentColour.value = map?.[entityId] || null;
    } catch { /* ignore */ }
  };

  const setColour = async (colour) => {
    try {
      await ufColourApi.upsert({ entityType, entityId, colour });
      currentColour.value = colour;
      notify.success(colour ? "Colour updated." : "Colour cleared.");
    } catch (err) {
      notify.error(getApiErrorMessage(err));
    }
  };

  const loadReminder = async () => {
    try {
      const res = await ufReminderApi.list({ page: 1, limit: 100 });
      reminder.value = (res?.data || []).find(
        (r) => Number(r.entityType) === Number(entityType) && r.entityId === entityId && !r.isDispatched
      ) || null;
    } catch { /* ignore */ }
  };

  const openReminder = () => {
    reminderForm.dueAt = reminder.value?.dueAtUtc ? toLocalInput(reminder.value.dueAtUtc) : "";
    reminderForm.note = reminder.value?.note || "";
    reminderOpen.value = true;
  };

  const saveReminder = async () => {
    if (!reminderForm.dueAt) {
      notify.warning("Please choose a date and time.");
      return;
    }
    savingReminder.value = true;
    try {
      const payload = { dueAtUtc: new Date(reminderForm.dueAt).toISOString(), note: reminderForm.note || null };
      if (reminder.value) {
        await ufReminderApi.update(reminder.value.id, payload);
      } else {
        await ufReminderApi.create({ entityType, entityId, ...payload });
      }
      reminderOpen.value = false;
      notify.success("Reminder saved.");
      await loadReminder();
    } catch (err) {
      notify.error(getApiErrorMessage(err));
    } finally {
      savingReminder.value = false;
    }
  };

  const cancelReminder = async () => {
    if (!reminder.value) return;
    const ok = await confirm({ title: "Cancel reminder", message: "Remove this reminder?", confirmLabel: "Remove", type: "danger" });
    if (!ok) return;
    try {
      await ufReminderApi.remove(reminder.value.id);
      reminder.value = null;
      reminderOpen.value = false;
      notify.success("Reminder cancelled.");
    } catch (err) {
      notify.error(getApiErrorMessage(err));
    }
  };

  // Copy an absolute link to this record via the universal /entity/:type/:id permalink, so the link
  // resolves to the right detail page regardless of the entity type.
  const copyLink = async () => {
    try {
      const resolved = router.resolve({ name: "uf_permalink", params: { type: typeSlug(entityType), id: entityId } });
      await copyToClipboard(window.location.origin + resolved.href);
      notify.success("Link copied.");
    } catch {
      notify.error("Could not copy link.");
    }
  };

  const exportPdf = async () => {
    exporting.value = true;
    try {
      const blob = await ufPdfApi.export({ entityType, entityId, includeConversation: includeConversation.value });
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = `${label}.pdf`;
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(url);
      pdfOpen.value = false;
      notify.success("Exported to PDF.");
    } catch (err) {
      notify.error(getApiErrorMessage(err));
    } finally {
      exporting.value = false;
    }
  };

  // Convert a UTC ISO string to a value the datetime-local input accepts (local wall-clock).
  const toLocalInput = (utc) => {
    const d = new Date(utc);
    const pad = (n) => String(n).padStart(2, "0");
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  };

  const loadAll = () => Promise.all([refreshPin(), loadColour(), loadReminder()]);

  return {
    palette,
    // pin
    pinned,
    busy,
    togglePin,
    // colour
    currentColour,
    setColour,
    // reminder
    reminder,
    reminderOverdue,
    reminderOpen,
    savingReminder,
    reminderForm,
    openReminder,
    saveReminder,
    cancelReminder,
    // pdf
    pdfOpen,
    includeConversation,
    exporting,
    exportPdf,
    // misc
    copyLink,
    loadAll
  };
}
