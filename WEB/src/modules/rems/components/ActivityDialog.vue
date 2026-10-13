<template>
  <q-dialog v-model="open">
    <q-card class="act-card">
      <q-card-section class="row items-center no-wrap">
        <div>
          <div class="text-h6">Activity</div>
          <div v-if="subtitle" class="text-caption text-grey-7">{{ subtitle }}</div>
        </div>
        <q-space />
        <q-btn flat round dense icon="o_refresh" @click="reload">
          <q-tooltip>Refresh</q-tooltip>
        </q-btn>
        <q-btn flat round dense icon="o_close" @click="open = false" />
      </q-card-section>
      <q-separator />

      <!-- What has HAPPENED to the request, as against the page behind this, which is what it SAYS. The
           tags sit at the top, as they did when this was a section of the page. -->
      <q-card-section class="act-body">
        <entity-tags-panel ref="tagsRef" :entity-type="EntityType.Rems" :entity-id="remsId" class="q-mb-md" />
        <q-separator class="q-mb-md" />
        <entity-activity-timeline ref="timelineRef" :entity-type="EntityType.Rems" :entity-id="remsId" />
      </q-card-section>

      <q-card-actions align="right">
        <q-btn flat no-caps label="Close" @click="open = false" />
      </q-card-actions>
    </q-card>
  </q-dialog>
</template>

<script setup>
// The request's own trail — its activity log and its tags — opened from the header rather than laid out
// on the page: it is about the record, not part of filling it in.
import { ref, computed } from "vue";
import { EntityType } from "services/api";
import EntityTagsPanel from "components/universal/EntityTagsPanel.vue";
import EntityActivityTimeline from "components/universal/EntityActivityTimeline.vue";

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  remsId: { type: String, default: null },
  subtitle: { type: String, default: "" }
});
const emit = defineEmits(["update:modelValue"]);

const open = computed({
  get: () => props.modelValue,
  set: (val) => emit("update:modelValue", val)
});

const tagsRef = ref(null);
const timelineRef = ref(null);

// Both load themselves each time the dialog opens; this is for a reader who has kept it open while
// something happened.
const reload = () => {
  tagsRef.value?.load();
  timelineRef.value?.load();
};
</script>

<style scoped>
.act-card {
  width: 640px;
  max-width: 94vw;
  border-radius: 12px;
}
/* The trail scrolls inside the card rather than making the dialog taller than the screen. */
.act-body {
  max-height: 62vh;
  overflow: auto;
}
</style>
