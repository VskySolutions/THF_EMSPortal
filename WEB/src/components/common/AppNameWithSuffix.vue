<template>
  <!-- A name as it reads: the name, then the generational particle after it, in bold. -->
  <span class="app-name">{{ head }}<span
    v-if="particle" class="app-name__suffix"
  >{{ particle }}</span></span>
</template>

<script setup>
// THE way a name with a generational suffix is drawn, anywhere in the app.
import { computed } from "vue";

const props = defineProps({
  // The name WITHOUT the particle.
  name: { type: String, default: "" },
  // The generational particle — Jr., Sr., III.
  suffix: { type: String, default: "" },
  // What to show when there is no name at all. A record with no name is information, so it reads as a
  // dash rather than as nothing.
  empty: { type: String, default: "—" }
});

const raw = computed(() => String(props.name ?? "").trim());

// A particle with no name in front of it is not a name — that renders as the empty placeholder instead.
const particle = computed(() => (raw.value ? String(props.suffix ?? "").trim() : ""));

// The name WITHOUT its particle, whether or not the caller had already joined the two.
const label = computed(() => {
  if (!particle.value) return raw.value;
  const tail = ` ${particle.value}`;
  return raw.value.toLowerCase().endsWith(tail.toLowerCase())
    ? raw.value.slice(0, -tail.length).trim()
    : raw.value;
});

// The name, with the space that separates it from the particle carried on the end. In the text node
// rather than as a span of its own so it survives a copy-paste and needs no whitespace-preserving CSS.
const head = computed(() => {
  if (!label.value) return particle.value ? "" : props.empty;
  return particle.value ? `${label.value} ` : label.value;
});
</script>

<style scoped>
/* Heavier than the name beside it whatever weight that name is set in: this renders inside plain cells
   and inside already-medium headings, and in both it has to be the part that catches the eye. */
.app-name__suffix {
  font-weight: 700;
}
</style>
