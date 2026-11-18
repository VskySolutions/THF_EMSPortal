<template>
  <!-- An email address as read-only text, with a copy button that shows while the address is hovered. -->
  <span v-if="isEmail" class="app-email">
    <span class="app-email__text">{{ email }}</span>
    <q-btn
      flat round dense size="xs" icon="o_content_copy" class="app-email__copy"
      aria-label="Copy email address" @click.stop.prevent="copy"
    >
      <q-tooltip>Copy email</q-tooltip>
    </q-btn>
  </span>
  <span v-else>{{ email || empty }}</span>
</template>

<script setup>
import { computed } from "vue";
import { useNotify } from "composables/useNotify";

const props = defineProps({
  email: { type: String, default: "" },
  // What stands in for no address.
  empty: { type: String, default: "—" }
});

const notify = useNotify();
// Anything else (a dash, "no email") is shown as it is, with nothing to copy.
const isEmail = computed(() => (props.email || "").includes("@"));

const copy = async () => {
  try {
    await navigator.clipboard.writeText(props.email.trim());
    notify.success("Email copied.");
  } catch {
    notify.warning("Could not copy the email. Your browser blocked clipboard access.");
  }
};
</script>

<style scoped>
.app-email {
  display: inline-flex;
  align-items: center;
  max-width: 100%;
  gap: 2px;
}
.app-email__text {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}
.app-email__copy {
  flex: 0 0 auto;
  opacity: 0;
  transition: opacity 0.12s;
}
.app-email:hover .app-email__copy,
.app-email__copy:focus-visible {
  opacity: 1;
}
</style>
