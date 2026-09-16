<template>
  <q-card flat bordered class="app-list-header q-mb-md">
    <!-- Crumbs on the left, the tools for the list on the right. -->
    <div class="app-list-header__bar q-px-md q-py-sm">
      <div class="app-list-header__title">
        <app-breadcrumbs
          v-if="breadcrumbs.length" :items="breadcrumbs" no-margin class="app-list-header__crumbs"
        />
        <!-- What the page is for, on a tooltip beside the title rather than a paragraph under the bar. -->
        <app-info-tip
          v-if="$slots.note" size="18px" color="primary" max-width="460px" anchor="bottom left" self="top left"
        >
          <slot name="note" />
        </app-info-tip>
      </div>

      <div class="app-list-header__tools">
        <q-input
          v-if="showSearch"
          :model-value="search"
          dense
          outlined
          debounce="300"
          :placeholder="searchPlaceholder"
          class="app-list-header__search"
          @update:model-value="$emit('update:search', $event)"
        >
          <template #prepend><q-icon name="o_search" /></template>
        </q-input>

        <q-btn v-if="showFilters" outline no-caps color="primary" icon="o_filter_list" label="Filters" @click="$emit('filters')">
          <q-badge v-if="filterCount" floating color="primary">{{ filterCount }}</q-badge>
        </q-btn>

        <!-- Extra page-specific actions (e.g. a secondary "Add Bulk" button), shown before Add. -->
        <slot name="actions" />

        <q-btn v-if="showAdd" unelevated no-caps color="primary" :icon="addIcon" :label="addLabel" :disable="addDisable" @click="$emit('add')" />

        <q-btn v-if="showBack" flat no-caps color="primary" icon="o_arrow_back" label="Back" @click="$emit('back')" />
      </div>
    </div>
  </q-card>
</template>

<script setup>
import AppBreadcrumbs from "components/common/AppBreadcrumbs.vue";
import AppInfoTip from "components/common/AppInfoTip.vue";

defineProps({
  breadcrumbs: { type: Array, default: () => [] },
  search: { type: String, default: "" },
  searchPlaceholder: { type: String, default: "Search" },
  showSearch: { type: Boolean, default: false },
  showFilters: { type: Boolean, default: false },
  filterCount: { type: Number, default: 0 },
  showAdd: { type: Boolean, default: false },
  addLabel: { type: String, default: "Add" },
  addIcon: { type: String, default: "o_add" },
  addDisable: { type: Boolean, default: false },
  showBack: { type: Boolean, default: false }
});

defineEmits(["update:search", "filters", "add", "back"]);
</script>

<style scoped>
.app-list-header {
  border-radius: 12px;
}
/* gap rather than q-gutter: the utility's negative margins fight the card padding once the row wraps. */
.app-list-header__bar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px 12px;
}
/* min-width:0 so a long trail of crumbs shrinks rather than pushing the tools onto their own line while
   there is still room beside them. The crumbs do not grow, so the note's icon sits by the page title. */
.app-list-header__title {
  display: flex;
  align-items: center;
  flex: 1 1 auto;
  min-width: 0;
}
.app-list-header__crumbs {
  min-width: 0;
}
.app-list-header__tools {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 8px;
  margin-left: auto;
  min-width: 0;
}
.app-list-header__search {
  width: 260px;
  max-width: 100%;
}

/* On a phone the crumbs and the tools each get a line. The search box takes the width the buttons
   leave rather than a fixed 260px, which is wider than some phones. */
@media (max-width: 599px) {
  .app-list-header__tools {
    width: 100%;
    justify-content: flex-start;
  }
  .app-list-header__search {
    flex: 1 1 160px;
    width: auto;
  }
}
</style>
