<template>
  <!-- Who a user in a dropdown is. Fetched on the first hover, not with the list. -->
  <q-tooltip
    :delay="400" :anchor="anchor" :self="self" max-width="340px" class="text-body2"
    @before-show="load"
  >
    <div v-if="card" class="user-card">
      <div class="user-card__name">{{ card.fullName }}</div>
      <div v-for="r in rows" :key="r.label" class="user-card__row">
        <span class="user-card__label">{{ r.label }}</span>
        <span class="user-card__value">{{ r.value }}</span>
      </div>
    </div>
    <div v-else-if="failed">Details are not available.</div>
    <div v-else>Loading…</div>
  </q-tooltip>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { loadUserCard } from "composables/useUserCard";

const props = defineProps({
  userId: { type: String, default: "" },
  // Beside an option by default, so the card does not cover the list being read.
  anchor: { type: String, default: "center right" },
  self: { type: String, default: "center left" }
});

const card = ref(null);
const failed = ref(false);

const load = async () => {
  const id = props.userId;
  if (!id || card.value?.id === id) return;
  failed.value = false;
  try {
    const loaded = await loadUserCard(id);
    if (props.userId === id) card.value = loaded;
  } catch {
    failed.value = true;
  }
};

watch(() => props.userId, () => {
  card.value = null;
  failed.value = false;
});

const rows = computed(() => [
  { label: "Email", value: card.value.email || "—" },
  { label: "Phone", value: card.value.phone || "—" },
  { label: "Department", value: card.value.department || "—" },
  { label: "Roles", value: (card.value.roles || []).join(", ") || "—" },
  { label: "Maconomy No.", value: card.value.maconomyNumber || "—" }
]);
</script>

<style scoped>
.user-card { min-width: 220px; }
.user-card__name {
  font-weight: 600;
  margin-bottom: 4px;
}
.user-card__row {
  display: flex;
  gap: 8px;
  line-height: 1.5;
}
.user-card__label {
  flex: 0 0 100px;
  opacity: 0.7;
}
.user-card__value {
  min-width: 0;
  overflow-wrap: anywhere;
}
</style>
