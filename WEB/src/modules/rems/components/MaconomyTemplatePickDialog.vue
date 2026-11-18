<template>
  <q-dialog ref="dialogRef" persistent @hide="onDialogHide">
    <q-card style="width: 480px; max-width: 92vw;">
      <q-card-section class="text-h6">Pick the Maconomy template job</q-card-section>
      <q-separator />
      <q-card-section>
        <div class="text-body2 text-grey-8 q-mb-md">
          No Maconomy template job is named as the Service Line
          <b>{{ serviceLine || "—" }}</b>. Pick the one this job is created from. It is kept on the request.
        </div>
        <app-select
          v-model="picked" :options="options" label="Template job *" use-input :clearable="false"
          :hint="options.length ? '' : 'Maconomy returned no template jobs.'"
        />
      </q-card-section>
      <q-separator />
      <q-card-actions align="right">
        <q-btn flat no-caps color="grey-8" label="Cancel" @click="onDialogCancel" />
        <q-btn unelevated no-caps color="primary" label="Use this template" :disable="!picked" @click="onDialogOK(picked)" />
      </q-card-actions>
    </q-card>
  </q-dialog>
</template>

<script setup>
// Asked by Sync job now when the request's Service Line matches no template job in Maconomy: the list is
// Maconomy's own, read just before, and the answer is the template job's number.
import { ref, computed } from "vue";
import { useDialogPluginComponent } from "quasar";
import AppSelect from "components/common/AppSelect.vue";

const props = defineProps({
  serviceLine: { type: String, default: "" },
  // Maconomy's template jobs: [{ number, name }].
  templates: { type: Array, default: () => [] }
});
defineEmits([...useDialogPluginComponent.emits]);

const { dialogRef, onDialogHide, onDialogOK, onDialogCancel } = useDialogPluginComponent();
const picked = ref(null);
const options = computed(() => props.templates.map((t) => ({ label: `${t.name} (${t.number})`, value: t.number })));
</script>
