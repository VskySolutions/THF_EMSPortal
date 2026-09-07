<template>
  <q-card flat bordered class="ae">
    <q-card-section class="ae__head row items-center">
      <div class="col">
        <div class="text-subtitle2 text-primary">
          <q-icon name="o_apartment" size="18px" class="q-mr-xs" />Other entities
        </div>
        <div class="text-caption text-grey-7">
          The client named these on their intake. Each one needs its own EMS.
        </div>
      </div>
      <q-badge v-if="outstanding" color="orange-8">{{ outstanding }} outstanding</q-badge>
    </q-card-section>
    <q-separator />
    <q-list separator>
      <q-item v-for="row in rows" :key="row.id">
        <q-item-section>
          <!-- Truncated rather than allowed to set the row's width: the Create-EMS button beside it is
               fixed, so a long business name would otherwise push it off a narrow card. -->
          <q-item-label class="ellipsis">{{ row.fullName }}</q-item-label>
          <q-item-label caption class="ellipsis">
            {{ row.emailAddress || "no email" }} · {{ row.phoneNumber || "no phone" }}
          </q-item-label>
        </q-item-section>
        <q-item-section side>
          <!-- Once an EMS exists the row says so and links to it. -->
          <q-btn
            v-if="row.createdRemsId" flat dense no-caps size="sm" color="positive"
            icon="o_check_circle" :label="row.createdRemsNumber || 'EMS created'"
            :to="{ name: 'rems_request', params: { id: row.createdRemsId } }"
          />
          <q-btn
            v-else unelevated dense no-caps size="sm" color="primary" icon="o_add"
            label="Create EMS" @click="$emit('create-ems', row)"
          />
        </q-item-section>
      </q-item>
    </q-list>
  </q-card>
</template>

<script setup>
// The client's other businesses, and whether each has been turned into its own request yet.
import { computed } from "vue";

const props = defineProps({
  rows: { type: Array, default: () => [] }
});
defineEmits(["create-ems"]);

const outstanding = computed(() => props.rows.filter((r) => !r.createdRemsId).length);
</script>

<style scoped>
.ae { border-radius: 10px; }
.ae__head { padding-bottom: 10px; }
</style>
