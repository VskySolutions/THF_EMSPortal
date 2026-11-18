<template>
  <q-card flat bordered>
    <q-card-section class="q-py-sm text-subtitle2 text-primary row items-center no-wrap">
      <q-icon :name="icon" size="18px" class="q-mr-xs" />{{ title }}
      <q-space />
      <!-- The whole form is the default; this narrows it to what actually goes. -->
      <q-toggle v-model="mappedOnly" dense size="sm" color="primary" label="Mapped only" class="text-caption text-grey-8" />
    </q-card-section>
    <q-separator />
    <q-card-section>
      <p class="text-body2 text-grey-8 q-mb-sm"><slot /></p>
      <template v-if="mapping">
        <div class="text-caption text-grey-7 q-mb-sm">
          Container <code>{{ mapping.container }}</code> <span class="text-grey-6">(server configuration)</span>
          · {{ mappedCount }} of {{ mapping.fields.length }} form fields go to Maconomy
        </div>
        <div class="mm-scroll">
          <q-markup-table flat bordered dense separator="horizontal" class="mm-table">
            <thead>
              <tr>
                <th class="text-left mm-table__form">Form field</th>
                <th class="text-left mm-table__field">Maconomy field</th>
                <th class="text-left">How</th>
              </tr>
            </thead>
            <tbody>
              <template v-for="group in groups" :key="group.section">
                <tr class="mm-table__section">
                  <td colspan="3">{{ group.section }}</td>
                </tr>
                <tr v-for="(f, i) in group.rows" :key="`${group.section}:${i}`" :class="{ 'mm-table__unmapped': !isMapped(f) }">
                  <td class="mm-table__form">{{ f.formField }}</td>
                  <!-- Blank where the field is not sent. -->
                  <td class="mm-table__field">
                    <template v-if="isMapped(f)">
                      <div class="row items-center no-wrap">
                        <q-icon :name="look(f).icon" :color="look(f).color" size="16px" class="q-mr-xs">
                          <q-tooltip>{{ look(f).tip }}</q-tooltip>
                        </q-icon>
                        <span class="text-weight-medium">{{ f.label || f.field }}</span>
                      </div>
                      <code v-if="f.field" class="text-caption text-grey-7">{{ f.field }}</code>
                    </template>
                  </td>
                  <td class="text-caption text-grey-8">{{ f.rule }}</td>
                </tr>
              </template>
            </tbody>
          </q-markup-table>
        </div>
      </template>
      <div v-else class="text-caption text-grey-6">
        <q-spinner v-if="loading" size="16px" color="primary" class="q-mr-xs" />
        {{ loading ? "Reading the mapping…" : "The mapping could not be read." }}
      </div>
    </q-card-section>
  </q-card>
</template>

<script setup>
// How one Maconomy card is filled, as the API describes it from the mapping code itself: every field of
// the forms in the order the forms ask them, under the forms' own headings, with the Maconomy field each
// one fills. A field that is not sent keeps its row and shows nothing beside it.
import { ref, computed } from "vue";

const props = defineProps({
  title: { type: String, required: true },
  icon: { type: String, default: "o_swap_horiz" },
  // { container, fields: [{ section, formField, field, label, rule, readBack, isDocument }] }; null until read.
  mapping: { type: Object, default: null },
  loading: { type: Boolean, default: false }
});

const mappedOnly = ref(false);

// A document row names no card field, but the file does go.
const isMapped = (f) => !!f.field || !!f.isDocument;

const mappedCount = computed(() => (props.mapping?.fields || []).filter(isMapped).length);

// Rows gathered under their section, in the order they arrive: the server sends them in form order.
const groups = computed(() => {
  const out = [];
  for (const f of props.mapping?.fields || []) {
    if (mappedOnly.value && !isMapped(f)) continue;
    const last = out[out.length - 1];
    if (last && last.section === f.section) last.rows.push(f);
    else out.push({ section: f.section, rows: [f] });
  }
  return out;
});

const look = (f) => {
  if (f.readBack) return { icon: "o_call_received", color: "amber-8", tip: "Read back from Maconomy" };
  if (f.isDocument) return { icon: "o_upload_file", color: "primary", tip: "Uploaded to Maconomy as a document" };
  return { icon: "o_call_made", color: "primary", tip: "Sent to Maconomy" };
};
</script>

<style scoped>
/* A whole form is a long table: it scrolls inside the card, under a header that stays put. */
.mm-scroll {
  max-height: 560px;
  overflow: auto;
}
/* Prose cells: the table wraps rather than widening past its card. */
.mm-table th,
.mm-table td {
  padding: 6px 10px;
  vertical-align: top;
  white-space: normal;
}
.mm-table thead th {
  position: sticky;
  top: 0;
  z-index: 1;
  background: #fff;
}
.mm-table th:first-child,
.mm-table td:first-child {
  padding-left: 12px;
}
.mm-table th:last-child,
.mm-table td:last-child {
  padding-right: 12px;
}
.mm-table__form {
  width: 190px;
}
.mm-table__field {
  width: 180px;
}
/* The form's own heading over the fields it asks. */
.mm-table__section td {
  padding-top: 8px;
  padding-bottom: 4px;
  background: var(--teal-050, #f2f8f8);
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--q-primary);
}
.mm-table__unmapped .mm-table__form {
  color: var(--ink-500, #7a8699);
}
</style>
