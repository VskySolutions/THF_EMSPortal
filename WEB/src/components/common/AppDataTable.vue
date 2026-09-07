<template>
  <q-table
    v-model:pagination="innerPagination"
    v-model:selected="innerSelected"
    :rows="displayedRows"
    :columns="effectiveColumns"
    :visible-columns="effectiveVisibleColumns"
    :row-key="rowKey"
    :loading="loading"
    :selection="selectable ? 'multiple' : 'none'"
    :rows-per-page-options="rowsPerPageOptions"
    :sort-method="clientSort ? sortMethod : undefined"
    :table-row-class-fn="rowClassFn"
    :table-row-style-fn="rowStyleFn"
    flat
    bordered
    :class="['app-data-table', { 'with-selection': selectable, 'with-actions': hasActions }]"
    @request="onRequest"
  >
    <!-- Top bar: title, custom actions, column menu, refresh -->
    <template #top>
      <div class="row full-width items-center q-gutter-sm">
        <div v-if="title" class="text-subtitle1 text-weight-medium">{{ title }}</div>
        <q-space />
        <slot name="actions" />

        <q-btn flat round dense icon="o_view_column">
          <q-tooltip>Columns</q-tooltip>
          <q-menu>
            <q-list dense style="min-width: 230px;">
              <q-item-label header class="text-grey-7">Show columns — drag to reorder</q-item-label>
              <q-item
                v-for="(col, index) in orderedToggleableColumns"
                :key="col.name"
                draggable="true"
                :class="['app-col-item', { 'app-col-item--over': overIndex === index, 'app-col-item--drag': dragIndex === index }]"
                @dragstart="onColDragStart(index, $event)"
                @dragover.prevent="onColDragOver(index)"
                @drop.prevent="onColDrop(index)"
                @dragend="onColDragEnd"
              >
                <q-item-section side class="app-col-item__handle">
                  <q-icon name="o_drag_indicator" class="text-grey-6" />
                </q-item-section>
                <q-item-section side>
                  <q-checkbox v-model="visibleColumnNames" :val="col.name" dense @click.stop />
                </q-item-section>
                <q-item-section>{{ col.label }}</q-item-section>
              </q-item>
              <q-separator />
              <q-item clickable @click="resetColumns">
                <q-item-section class="text-primary">Reset columns</q-item-section>
              </q-item>
            </q-list>
          </q-menu>
        </q-btn>

        <q-btn flat round dense icon="o_refresh" :loading="loading" @click="$emit('refresh')">
          <q-tooltip>Refresh</q-tooltip>
        </q-btn>
      </div>
      <div v-if="selectable && innerSelected.length" class="row full-width items-center q-mt-sm">
        <q-chip dense color="primary" text-color="white">{{ innerSelected.length }} selected</q-chip>
        <slot name="bulk-actions" :selected="innerSelected" />
      </div>
    </template>

    <template #no-data>
      <div class="full-width column flex-center q-pa-lg text-grey-6">
        <q-icon name="o_inbox" size="32px" class="q-mb-sm" />
        No Data Available
      </div>
    </template>

    <!-- Resizable header cells (sorting still works via q-th :props). -->
    <template #header-cell="props">
      <q-th :props="props" :style="columnStyle(props.col.name)" class="app-th">
        {{ props.col.label }}
        <div
          v-if="props.col.name !== 'actions'"
          class="app-th__resize"
          @mousedown.stop.prevent="startResize($event, props.col.name)"
          @click.stop
        />
      </q-th>
    </template>

    <!-- Forward all parent-provided body slots (e.g. body-cell-xxx) to QTable. -->
    <template v-for="(_, name) in forwardedSlots" #[name]="slotProps" :key="name">
      <slot :name="name" v-bind="slotProps" />
    </template>
  </q-table>
</template>

<script setup>
import { computed, ref, watch, useSlots, toRef } from "vue";
import { usePreferences } from "composables/usePreferences";
import useColumnResize from "composables/dataTable/useColumnResize.js";
import useColumnOrder from "composables/dataTable/useColumnOrder.js";

const props = defineProps({
  rows: { type: Array, default: () => [] },
  columns: { type: Array, default: () => [] },
  rowKey: { type: String, default: "id" },
  loading: { type: Boolean, default: false },
  title: { type: String, default: "" },
  pageKey: { type: String, default: "" },
  totalRecords: { type: Number, default: 0 },
  selectable: { type: Boolean, default: false },
  pagination: { type: Object, default: null },
  defaultSortBy: { type: String, default: "updatedOnUtc" },
  defaultDescending: { type: Boolean, default: true },
  // Sort here instead of asking the server.
  clientSort: { type: Boolean, default: false },
  // Row-key values to float to the top of the current page (e.g. pinned records), kept above the
  // rest regardless of the active sort.
  pinnedRowKeys: { type: Array, default: () => [] },
  // Personal row tints as { [rowKey]: "#hex" } — a Universal Features colour code, private to the viewer.
  rowColours: { type: Object, default: () => ({}) }
});

const emit = defineEmits(["request", "refresh", "update:pagination", "update:selected"]);

const rowsPerPageOptions = [10, 20, 50, 100];
const prefs = props.pageKey ? usePreferences(props.pageKey) : null;

// The page owns page, size AND sort — useListTable holds them, sends them to the server and remembers
// them.
const innerPagination = ref({
  page: 1,
  rowsPerPage: prefs?.get("pageSize", 20) ?? 20,
  sortBy: props.defaultSortBy,
  descending: props.defaultDescending,
  rowsNumber: props.totalRecords,
  ...(props.pagination || {})
});

const innerSelected = ref([]);
watch(innerSelected, (val) => emit("update:selected", val));

watch(() => props.totalRecords, (total) => {
  innerPagination.value = { ...innerPagination.value, rowsNumber: total };
});

// The page's sort follows the page: a list that resets to page 1 on a new sort, or restores a remembered
// one, has to be able to say so and see the header arrow move.
watch(() => props.pagination, (next) => {
  if (next) innerPagination.value = { ...innerPagination.value, ...next };
}, { deep: true });

// ---- Sorting ----
// A list is NOT ordered here.
const sortValue = (col, name) => {
  const accessor = col?.sort ?? col?.field ?? name;
  return typeof accessor === "function" ? accessor : (row) => row[accessor];
};

// ISO-8601 is what the API sends every timestamp as; anything else is left to the text comparison, so a
// name that happens to parse as a date is not silently treated as one.
const isIsoInstant = (v) => typeof v === "string" && /^\d{4}-\d{2}-\d{2}[T ]/.test(v);

const compare = (x, y) => {
  if (x == null && y == null) return 0;
  if (x == null) return 1;
  if (y == null) return -1;
  if (typeof x === "boolean" || typeof y === "boolean") return (x === y) ? 0 : (x ? 1 : -1);
  if (typeof x === "number" && typeof y === "number") return x - y;
  if (isIsoInstant(x) && isIsoInstant(y)) return Date.parse(x) - Date.parse(y);
  return String(x).localeCompare(String(y), undefined, { numeric: true });
};

const sortMethod = (rows, sortBy, descending) => {
  if (!sortBy) return rows;
  const get = sortValue(props.columns.find((c) => c.name === sortBy), sortBy);
  const sorted = [...rows].sort((a, b) => compare(get(a), get(b)));
  return descending ? sorted.reverse() : sorted;
};

// Whether this list carries an actions column — it is what the nowrap rule in the stylesheet keys off.
const hasActions = computed(() => props.columns.some((c) => c.name === "actions"));

const pinnedSet = computed(() => new Set(props.pinnedRowKeys));

// The rows as given, with pinned ones floated to the top.
const displayedRows = computed(() => {
  if (!props.pinnedRowKeys.length) return props.rows;
  const isPinned = (row) => pinnedSet.value.has(row[props.rowKey]);
  const pinned = props.rows.filter(isPinned);
  return pinned.length ? [...pinned, ...props.rows.filter((row) => !isPinned(row))] : props.rows;
});

// ---- Personal row marks ----
// A pinned row is tinted faintly and a coloured row gets a stripe down its left edge.
const rowClassFn = (row) =>
  (pinnedSet.value.has(row?.[props.rowKey]) ? "app-data-table__row--pinned" : "");

const rowStyleFn = (row) => {
  const colour = props.rowColours?.[row?.[props.rowKey]];
  return colour ? `--app-row-colour: ${colour}` : "";
};

const onRequest = (requestProps) => {
  const next = requestProps.pagination;
  innerPagination.value = { ...innerPagination.value, ...next };
  if (prefs) {
    prefs.merge({ pageSize: next.rowsPerPage });
  }

  // Page, size and sort all go back to the server — a new sort is a new question about the whole set, not
  // a rearrangement of the rows already here.
  emit("update:pagination", innerPagination.value);
  if (!props.clientSort) emit("request", innerPagination.value);
};

// ---- Column visibility (persisted) ----
// The menu lists ALL data columns; only those flagged `default: true` are shown initially (falling back to
// all when none are flagged).
const toggleableColumns = computed(() => props.columns.filter((c) => c.name !== "actions"));
const allColumnNames = computed(() => props.columns.map((c) => c.name));

const defaultColumnNames = () => {
  const flagged = props.columns.filter((c) => c.default === true && c.name !== "actions").map((c) => c.name);
  return flagged.length ? flagged : props.columns.filter((c) => c.name !== "actions").map((c) => c.name);
};

const visibleColumnNames = ref(prefs?.get("visibleColumns", null) ?? defaultColumnNames());

// "actions" is always shown; the menu only toggles data columns.
const effectiveVisibleColumns = computed(() => {
  const set = new Set(visibleColumnNames.value);
  if (allColumnNames.value.includes("actions")) set.add("actions");
  return [...set];
});

watch(visibleColumnNames, (val) => prefs?.set("visibleColumns", val), { deep: true });

// ---- Column ordering (drag-to-reorder in the columns menu, persisted) ----
const columnsRefForOrder = toRef(props, "columns");
const { reorder, resetOrder, orderColumns } = useColumnOrder({
  columns: columnsRefForOrder,
  initialOrder: prefs?.get("columnOrder", null),
  saveOrderState: (o) => prefs?.set("columnOrder", o)
});

// Toggleable columns shown in the menu, in the user's chosen order.
const orderedToggleableColumns = computed(() => orderColumns(toggleableColumns.value));

const dragIndex = ref(null);
const overIndex = ref(null);
const onColDragStart = (i, e) => {
  dragIndex.value = i;
  if (e.dataTransfer) e.dataTransfer.effectAllowed = "move";
};
const onColDragOver = (i) => { overIndex.value = i; };
const onColDrop = (i) => {
  if (dragIndex.value !== null) reorder(dragIndex.value, i);
  dragIndex.value = null;
  overIndex.value = null;
};
const onColDragEnd = () => { dragIndex.value = null; overIndex.value = null; };

const resetColumns = () => {
  visibleColumnNames.value = defaultColumnNames();
  resetOrder();
};

// ---- Column resizing (persisted) ----
const columnsRef = toRef(props, "columns");
const resizeWidths = ref(prefs?.get("columnWidths", {}) ?? {});
const { startResize } = useColumnResize({
  columns: columnsRef,
  resizeWidths,
  saveResizableWidthState: (widths) => prefs?.set("columnWidths", widths)
});

const columnStyle = (name) => {
  const w = resizeWidths.value?.[name];
  return w ? { width: `${w}px`, minWidth: `${w}px` } : {};
};

// Apply widths to body cells too, so columns line up with the resized headers, then apply the
// user's chosen column order (the "actions" column always sinks last).
const effectiveColumns = computed(() => {
  const withWidths = props.columns.map((c) => {
    const w = resizeWidths.value?.[c.name];
    return w ? { ...c, style: `width:${w}px;min-width:${w}px;` } : c;
  });
  return orderColumns(withWidths);
});

// Slots other than the ones defined here are forwarded straight to QTable.
const slots = useSlots();
const reserved = ["top", "no-data", "actions", "bulk-actions", "header-cell"];
const forwardedSlots = computed(() =>
  Object.fromEntries(Object.entries(slots).filter(([name]) => !reserved.includes(name))));
</script>

<style scoped>
.app-data-table {
  border-radius: 12px;
}
.app-data-table :deep(thead tr th) {
  position: sticky;
  top: 0;
  z-index: 1;
  background: #fff;
}
.app-th {
  position: relative;
}

/* ---- Personal row marks (pin + colour) ----
   Both are PRIVATE to the viewer, so both are drawn quietly: a pinned row is a shade warmer than its
   neighbours, and a coloured one carries a stripe down its left edge. Neither may shout — the row's own
   status badges are what the reader is scanning for, and a full-width tint would drown them. */
.app-data-table :deep(tbody tr.app-data-table__row--pinned) {
  background: #fbfaf5;
}
.app-data-table :deep(tbody tr td:first-child) {
  position: relative;
}
/* On the first CELL rather than the row: a <tr> is not a reliable box to paint a border on. The colour
   arrives as a custom property set per row (see rowStyleFn); with none set this paints nothing. */
.app-data-table :deep(tbody tr td:first-child)::before {
  content: "";
  position: absolute;
  left: 0;
  top: 0;
  bottom: 0;
  width: 4px;
  background: var(--app-row-colour, transparent);
}
.app-th__resize {
  position: absolute;
  top: 0;
  right: 0;
  width: 6px;
  height: 100%;
  cursor: col-resize;
  user-select: none;
}
.app-th__resize:hover {
  background: var(--q-primary);
  opacity: 0.4;
}

/* ---- The actions column is a row of controls, not prose ----
   It never wraps. A list that has grown a sixth and seventh action — the personal pin and colour, an
   Edit that only some rows offer — would otherwise fold onto a second line on a narrow window, and a
   row whose height changes with how many actions it happens to carry is a row that reads as broken.
   The column sizes itself to what it holds instead, and Quasar's own middle section scrolls sideways
   if the whole table outgrows the window.
   Addressed as the LAST cell, which is what the actions column always is — useColumnOrder sinks it
   there whatever order the reader drags the other columns into.

   ONE RULE FOR ANYTHING PUT IN AN ACTIONS CELL: it must be inline-level. `nowrap` governs inline
   content only, so a single block-level box breaks the line whatever this says — which is exactly what
   a <q-separator vertical> did, since QSeparator renders an <hr>. A q-btn is inline-flex and safe; a
   div, an hr or a q-separator is not.

   The children are middle-aligned rather than left on their baselines. Icon buttons and an inline-flex
   group (the personal marks) compute baselines differently, so on a baseline they sit a pixel or two
   apart — visible as a ragged row once there are six or seven of them. */
.app-data-table.with-actions :deep(thead tr th:last-child),
.app-data-table.with-actions :deep(tbody tr td:last-child) {
  white-space: nowrap;
  width: 1%;
}
.app-data-table.with-actions :deep(tbody tr td:last-child > *) {
  vertical-align: middle;
}

/* Left-align the selection (checkbox) column header + body cells. */
.app-data-table.with-selection :deep(thead th:first-child),
.app-data-table.with-selection :deep(tbody td:first-child) {
  text-align: left;
  width: 1%;
  white-space: nowrap;
}

/* Draggable column-reorder rows in the columns menu. */
.app-col-item {
  cursor: grab;
}
.app-col-item__handle {
  min-width: 0;
  padding-right: 4px;
}
.app-col-item--drag {
  opacity: 0.5;
}
.app-col-item--over {
  border-top: 2px solid var(--q-primary);
}
</style>
