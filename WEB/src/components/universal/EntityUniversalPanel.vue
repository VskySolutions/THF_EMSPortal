<template>
  <q-card flat bordered>
    <!-- Optional section title. -->
    <div v-if="title" class="q-px-md q-pt-sm">
      <div class="text-subtitle1">{{ title }}</div>
    </div>

    <!-- Inline tags chips. -->
    <template v-if="showTags">
      <div class="q-px-md q-pt-sm">
        <entity-tags-panel :entity-type="entityType" :entity-id="entityId" />
      </div>
      <q-separator class="q-mt-sm" />
    </template>

    <q-tabs
      v-model="tab"
      dense
      align="left"
      active-color="primary"
      indicator-color="primary"
      class="text-grey-7"
    >
      <q-tab v-for="t in visibleTabs" :key="t.key" :name="t.key" :icon="t.icon" :label="t.label" no-caps />
    </q-tabs>
    <q-separator />

    <q-tab-panels v-model="tab" keep-alive animated>
      <q-tab-panel v-for="t in visibleTabs" :key="t.key" :name="t.key" class="q-pt-sm">
        <component
          :is="t.component"
          v-if="opened[t.key]"
          :entity-type="entityType"
          :entity-id="entityId"
        />
      </q-tab-panel>
    </q-tab-panels>
  </q-card>
</template>

<script setup>
import { ref, reactive, computed, watch } from "vue";
import EntityTagsPanel from "./EntityTagsPanel.vue";
import EntityConversationPanel from "./EntityConversationPanel.vue";
import EntityActivityTimeline from "./EntityActivityTimeline.vue";
import EntityChecklistsPanel from "./EntityChecklistsPanel.vue";
import EntityAttachmentsPanel from "./EntityAttachmentsPanel.vue";

// The reusable Universal Features collaboration panel: inline tags plus a tabbed conversation, activity,
// checklists and attachments for any (entityType, entityId).
const TAB_REGISTRY = {
  conversation: { label: "Conversation", icon: "o_chat", component: EntityConversationPanel },
  activity: { label: "Activity", icon: "o_history", component: EntityActivityTimeline },
  checklists: { label: "Checklists", icon: "o_checklist", component: EntityChecklistsPanel },
  attachments: { label: "Attachments", icon: "o_attach_file", component: EntityAttachmentsPanel }
};

const props = defineProps({
  entityType: { type: Number, required: true },
  entityId: { type: String, required: true },
  // Which sections to show, in order. Unknown keys are ignored.
  tabs: { type: Array, default: () => ["conversation", "activity", "checklists", "attachments"] },
  // Show the inline tags row above the tabs.
  showTags: { type: Boolean, default: true },
  // The tab open on first render; defaults to the first visible tab.
  initialTab: { type: String, default: "" },
  // Optional heading rendered above the panel.
  title: { type: String, default: "" }
});

const visibleTabs = computed(() =>
  props.tabs.filter((key) => TAB_REGISTRY[key]).map((key) => ({ key, ...TAB_REGISTRY[key] })));

const firstTabKey = computed(() => visibleTabs.value[0]?.key || "");
const tab = ref(props.initialTab && visibleTabs.value.some((t) => t.key === props.initialTab)
  ? props.initialTab
  : firstTabKey.value);

// Lazy-load each tab's content only after it is first opened.
const opened = reactive({});
const markOpened = (name) => { if (name) opened[name] = true; };
markOpened(tab.value);
watch(tab, (value) => markOpened(value));
</script>
