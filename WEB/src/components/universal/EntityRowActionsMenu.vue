<template>
  <span class="uf-row-actions">
    <q-btn flat round dense icon="o_more_vert" color="grey-8">
      <q-tooltip>More actions</q-tooltip>
      <q-menu anchor="bottom right" self="top right" @show="ensureLoaded">
        <q-list style="min-width: 210px;">
          <!-- Page-specific entity actions (View / Edit / Delete …) injected by the host. -->
          <slot />
          <q-separator v-if="$slots.default" />

          <q-item-label header class="text-grey-6 q-py-xs">Universal actions</q-item-label>

          <!-- Pin (disabled once the per-type pin limit is reached) -->
          <q-item v-close-popup clickable :disable="busy || (pinDisabled && !pinned)" @click="onTogglePin">
            <q-item-section avatar>
              <q-icon name="o_push_pin" :color="pinned ? 'primary' : undefined" />
            </q-item-section>
            <q-item-section>{{ pinned ? "Unpin" : "Pin" }}</q-item-section>
            <q-item-section v-if="pinDisabled && !pinned" side>
              <span class="text-grey-6 fs-12">Max {{ pinLimit }}</span>
            </q-item-section>
          </q-item>

          <!-- Colour (nested swatch picker) -->
          <q-item clickable>
            <q-item-section avatar><q-icon name="o_palette" :style="{ color: currentColour || undefined }" /></q-item-section>
            <q-item-section>Colour</q-item-section>
            <q-item-section side><q-icon name="o_chevron_right" size="18px" color="grey-6" /></q-item-section>
            <q-menu anchor="top end" self="top start">
              <div class="q-pa-sm row q-gutter-xs" style="max-width: 168px;">
                <div
                  v-for="c in palette"
                  :key="c"
                  v-close-popup
                  class="uf-swatch cursor-pointer"
                  :style="{ backgroundColor: c, outline: c === currentColour ? '2px solid #1976d2' : 'none' }"
                  @click="pickColour(c)"
                />
                <q-btn v-close-popup flat dense no-caps size="sm" label="None" class="full-width q-mt-xs" @click="pickColour(null)" />
              </div>
            </q-menu>
          </q-item>

          <!-- Reminder -->
          <q-item v-close-popup clickable @click="openReminder">
            <q-item-section avatar><q-icon name="o_alarm" :color="reminder ? 'orange-8' : undefined" /></q-item-section>
            <q-item-section>{{ reminder ? "Edit reminder" : "Set reminder" }}</q-item-section>
            <q-item-section v-if="reminderOverdue" side><q-badge color="negative" label="Overdue" /></q-item-section>
          </q-item>

          <!-- Copy link -->
          <q-item v-close-popup clickable @click="copyLink">
            <q-item-section avatar><q-icon name="o_link" /></q-item-section>
            <q-item-section>Copy link</q-item-section>
          </q-item>

          <!-- Export PDF -->
          <q-item v-close-popup clickable @click="pdfOpen = true">
            <q-item-section avatar><q-icon name="o_print" /></q-item-section>
            <q-item-section>Export PDF</q-item-section>
          </q-item>
        </q-list>
      </q-menu>
    </q-btn>

    <!-- Dialogs live as siblings of the menu (not inside it) so they stay mounted when the menu closes. -->
    <app-form-drawer
      v-model="reminderOpen"
      title="Set reminder"
      :saving="savingReminder"
      @submit="saveReminder"
      @cancel="reminderOpen = false"
    >
      <q-form>
        <app-text-field v-model="reminderForm.dueAt" type="datetime-local" label="Remind me at" required />
        <app-text-field v-model="reminderForm.note" label="Note" type="textarea" autogrow class="q-mt-sm" />
      </q-form>
      <template #footer-actions>
        <q-btn v-if="reminder" flat no-caps color="negative" label="Cancel reminder" @click="cancelReminder" />
      </template>
    </app-form-drawer>

    <q-dialog v-model="pdfOpen">
      <q-card style="min-width: 320px;">
        <q-card-section class="text-h6">Export to PDF</q-card-section>
        <q-card-section class="q-pt-none">
          <q-toggle v-model="includeConversation" label="Include conversation" />
        </q-card-section>
        <q-card-actions align="right">
          <q-btn v-close-popup flat no-caps label="Cancel" />
          <q-btn unelevated no-caps color="primary" label="Download" :loading="exporting" @click="exportPdf" />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </span>
</template>

<script setup>
import { ref } from "vue";
import { useEntityActions } from "composables/uf/useEntityActions";
import AppFormDrawer from "components/common/AppFormDrawer.vue";
import AppTextField from "components/common/AppTextField.vue";

// The full Universal Features action set as menu items, for use inside a list row's "more" menu.
const props = defineProps({
  entityType: { type: Number, required: true },
  entityId: { type: String, required: true },
  label: { type: String, default: "record" },
  // Seed the pinned state so the menu shows "Unpin" immediately for an already-pinned row.
  initialPinned: { type: Boolean, default: false },
  // Disable pinning new records once the host's pin limit is reached (already-pinned rows can unpin).
  pinDisabled: { type: Boolean, default: false },
  pinLimit: { type: Number, default: 5 }
});

// Notifies the host (e.g. a list) so it can reflect the new colour / pin state on the row immediately.
const emit = defineEmits(["colour-change", "pin-change"]);

const {
  palette,
  pinned, busy, togglePin,
  currentColour, setColour,
  reminder, reminderOverdue, reminderOpen, savingReminder, reminderForm, openReminder, saveReminder, cancelReminder,
  pdfOpen, includeConversation, exporting, exportPdf,
  copyLink, loadAll
} = useEntityActions(props.entityType, props.entityId, props.label, props.initialPinned);

const onTogglePin = async () => {
  await togglePin();
  emit("pin-change", pinned.value);
};

const pickColour = async (colour) => {
  await setColour(colour);
  emit("colour-change", colour);
};

// Lazily load this row's pin/colour/reminder state the first time its menu is opened — avoids
// firing N×3 requests for every row when the list renders.
const loadedOnce = ref(false);
const ensureLoaded = () => {
  if (loadedOnce.value) return;
  loadedOnce.value = true;
  loadAll();
};
</script>

<style scoped>
.uf-swatch { width: 28px; height: 28px; border-radius: 6px; }
</style>
