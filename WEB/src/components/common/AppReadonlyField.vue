<template>
  <div class="app-field">
    <app-field-label :label="label" />
    <div class="app-readonly">
      <div class="col ellipsis"><slot>{{ display }}</slot></div>
      <!-- The hint rides INSIDE the control rather than sitting under it: a caption line would make this
           field taller than the inputs beside it and knock the grid row out of alignment. -->
      <q-icon v-if="hint" :name="hintIcon" :color="hintColor" size="18px" class="q-ml-sm">
        <q-tooltip anchor="top middle" self="bottom middle" max-width="260px">{{ hint }}</q-tooltip>
      </q-icon>
    </div>
  </div>
</template>

<script setup>
// A value the form shows but nobody can type into (server-derived, copied from a submission, …).
import { computed } from "vue";
import AppFieldLabel from "components/common/AppFieldLabel.vue";

const props = defineProps({
  // Use the default slot instead when the value needs markup.
  modelValue: { type: [String, Number], default: "" },
  label: { type: String, default: "" },
  // Shown when the value is empty, so the control is never blank and collapsed.
  placeholder: { type: String, default: "—" },
  // Explanation of where the value comes from, shown on an info icon inside the control (never as an
  // extra line, so the field keeps the exact height of an input).
  hint: { type: String, default: "" },
  // Draws attention to the hint when it reports something the user needs to act on.
  hintAlert: { type: Boolean, default: false }
});

const hintIcon = computed(() => (props.hintAlert ? "o_error_outline" : "o_info"));
const hintColor = computed(() => (props.hintAlert ? "warning" : "grey-6"));

const display = computed(() => {
  const value = props.modelValue;
  return value === null || value === undefined || String(value).trim() === "" ? props.placeholder : value;
});
</script>

<style scoped>
.app-readonly {
  min-height: 40px;
  display: flex;
  align-items: center;
  padding: 6px 12px;
  border: 1px solid #e0e6ed;
  border-radius: 8px;
  background: #f7f9fc;
  color: #2c3540;
  font-size: 14px;
}
</style>
