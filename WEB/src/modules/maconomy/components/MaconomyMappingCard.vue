<template>
  <q-card flat bordered>
    <q-card-section class="q-py-sm text-subtitle2 text-primary">
      <q-icon :name="icon" size="18px" class="q-mr-xs" />{{ title }}
    </q-card-section>
    <q-separator />
    <q-card-section>
      <p class="text-body2 text-grey-8 q-mb-sm"><slot /></p>
      <template v-if="mapping">
        <div class="text-caption text-grey-7 q-mb-sm">
          Container <code>{{ mapping.container }}</code> <span class="text-grey-6">(server configuration)</span>
        </div>
        <q-markup-table flat bordered dense separator="horizontal" class="mm-table">
          <thead>
            <tr>
              <th class="text-left mm-table__field">Maconomy field</th>
              <th class="text-left">Filled from</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="f in mapping.fields" :key="f.field">
              <td class="mm-table__field">
                <div class="text-weight-medium">{{ f.label }}</div>
                <code class="text-caption text-grey-7">{{ f.field }}</code>
              </td>
              <td>
                <div class="row items-center no-wrap">
                  <q-icon
                    :name="f.readBack ? 'o_call_received' : 'o_call_made'" :color="f.readBack ? 'amber-8' : 'primary'"
                    size="16px" class="q-mr-xs"
                  >
                    <q-tooltip>{{ f.readBack ? "Read back from Maconomy" : "Sent to Maconomy" }}</q-tooltip>
                  </q-icon>
                  <span>{{ f.source }}</span>
                </div>
                <div v-if="f.rule" class="text-caption text-grey-7 q-mt-xs">{{ f.rule }}</div>
              </td>
            </tr>
          </tbody>
        </q-markup-table>
      </template>
      <div v-else class="text-caption text-grey-6">
        <q-spinner v-if="loading" size="16px" color="primary" class="q-mr-xs" />
        {{ loading ? "Reading the mapping…" : "The mapping could not be read." }}
      </div>
    </q-card-section>
  </q-card>
</template>

<script setup>
// How one Maconomy card is filled, in words, as the API describes it from the mapping code itself: one
// row per card field, with what the portal fills it from and the rule that decides the value.
defineProps({
  title: { type: String, required: true },
  icon: { type: String, default: "o_swap_horiz" },
  // { container, fields: [{ field, label, source, rule, readBack }] }; null until read.
  mapping: { type: Object, default: null },
  loading: { type: Boolean, default: false }
});
</script>

<style scoped>
/* Prose cells: the table wraps rather than widening past its card. */
.mm-table th,
.mm-table td {
  padding: 6px 10px;
  vertical-align: top;
  white-space: normal;
}
.mm-table th:first-child,
.mm-table td:first-child {
  padding-left: 12px;
}
.mm-table th:last-child,
.mm-table td:last-child {
  padding-right: 12px;
}
.mm-table__field {
  width: 170px;
}
</style>
