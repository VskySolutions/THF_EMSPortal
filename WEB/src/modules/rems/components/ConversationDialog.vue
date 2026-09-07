<template>
  <q-dialog v-model="open">
    <q-card class="conv-dialog">
      <q-card-section class="row items-center no-wrap q-py-sm">
        <q-avatar size="34px" color="primary" text-color="white" icon="o_forum" class="q-mr-sm" />
        <div class="col">
          <div class="text-subtitle1 text-weight-medium">Conversation</div>
          <div v-if="subtitle" class="text-caption text-grey-7 ellipsis">{{ subtitle }}</div>
        </div>
        <q-btn flat round dense icon="o_close" @click="open = false" />
      </q-card-section>
      <q-separator />
      <!-- No overflow of its own: the panel is a chat, and a chat scrolls its THREAD while its composer
           stays put. -->
      <q-card-section class="conv-dialog__body">
        <!-- Reuses the Universal Features conversation thread keyed on the REMS request. -->
        <entity-conversation-panel
          v-if="requestId" :entity-type="EntityType.Rems" :entity-id="requestId" height="100%"
        />
      </q-card-section>
    </q-card>
  </q-dialog>
</template>

<script setup>
import { computed } from "vue";
import { EntityType } from "services/api";
// Must be imported explicitly: <script setup> resolves components from this scope, and only ZwDate /
// ZwCurrency / ZwNumeric are registered globally (boot/components.js).
import EntityConversationPanel from "components/universal/EntityConversationPanel.vue";

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  requestId: { type: String, default: null },
  subtitle: { type: String, default: "" }
});
const emit = defineEmits(["update:modelValue"]);

const open = computed({
  get: () => props.modelValue,
  set: (val) => emit("update:modelValue", val)
});
</script>

<style scoped>
/* A chat window rather than a card that happens to hold messages: tall enough that a thread reads as a
   thread, and capped so it never runs past the viewport on a laptop. */
.conv-dialog {
  display: flex;
  flex-direction: column;
  width: 640px;
  max-width: 94vw;
  height: 76vh;
  max-height: 720px;
}
.conv-dialog__body {
  flex: 1 1 auto;
  min-height: 0;
  padding: 12px;
}
</style>
