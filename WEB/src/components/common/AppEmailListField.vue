<template>
  <div class="app-field">
    <app-field-label :label="label" :required="required" :info="info" />
    <!-- A free-typed list: there is nothing to pick from, so the dropdown and its icon are gone, and every
         address becomes a chip the moment it is entered. -->
    <q-select
      ref="selectRef"
      v-model="model"
      :options="[]"
      multiple use-input use-chips hide-dropdown-icon
      input-debounce="0"
      :readonly="readonly"
      :disable="disable"
      :hint="hint"
      :error="!!problem"
      :error-message="problem"
      :aria-label="ariaLabel"
      autocomplete="off"
      outlined dense hide-bottom-space
      class="app-email-list"
      @new-value="onNewValue"
      @input-value="onTyped"
      @blur="flush"
    >
      <template #selected-item="scope">
        <q-chip
          :removable="!readonly && !disable" dense :tabindex="scope.tabindex"
          color="teal-1" text-color="primary" class="app-email-list__chip"
          @remove="scope.removeAtIndex(scope.index)"
        >
          {{ scope.opt }}
        </q-chip>
      </template>
    </q-select>
  </div>
</template>

<script setup>
// A list of email addresses typed in by hand — the To line of an email. Enter, a comma, a semicolon or
// leaving the field ends an address; a pasted list is split the same way. An entry that does not look
// like an address is refused and named rather than silently dropped.
import { ref, computed, toRef } from "vue";
import AppFieldLabel from "components/common/AppFieldLabel.vue";
import { useFieldLabel } from "composables/useFieldLabel";

const props = defineProps({
  modelValue: { type: Array, default: () => [] },
  label: { type: String, default: "" },
  required: { type: Boolean, default: false },
  info: { type: String, default: "" },
  hint: { type: String, default: "" },
  readonly: { type: Boolean, default: false },
  disable: { type: Boolean, default: false }
});
const emit = defineEmits(["update:modelValue"]);

const model = computed({
  get: () => props.modelValue || [],
  set: (val) => emit("update:modelValue", val || [])
});

const selectRef = ref(null);
const problem = ref("");
// Typed and not yet entered, so leaving the field can still take it.
let pending = "";

// Loose on purpose: one @, something either side, no separators. The server checks it properly.
const LOOKS_LIKE_EMAIL = /^[^\s@,;<>]+@[^\s@,;<>]+\.[^\s@,;<>]+$/;

// Adds every address in the text, once each; names the first that was refused.
const add = (text) => {
  const next = [...model.value];
  let refused = "";
  for (const part of String(text || "").split(/[\s,;]+/).filter(Boolean)) {
    if (!LOOKS_LIKE_EMAIL.test(part)) {
      refused = refused || part;
    } else if (!next.some((x) => x.toLowerCase() === part.toLowerCase())) {
      next.push(part);
    }
  }
  problem.value = refused ? `"${refused}" is not an email address.` : "";
  if (next.length !== model.value.length) model.value = next;
};

const clearInput = () => {
  pending = "";
  selectRef.value?.updateInputValue("");
};

// Enter. The addresses went into the model above; done() with nothing just clears the input.
const onNewValue = (text, done) => {
  add(text);
  pending = "";
  done();
};

const onTyped = (text) => {
  pending = text || "";
  if (/[,;]/.test(pending)) {
    add(pending);
    clearInput();
  } else if (problem.value && pending) {
    problem.value = "";
  }
};

const flush = () => {
  if (!pending.trim()) return;
  add(pending);
  clearInput();
};

const { text: ariaLabel } = useFieldLabel(toRef(props, "label"), toRef(props, "required"));
</script>

<style scoped>
/* Same control height as the app's selects; the chips wrap and the control grows with them. */
.app-email-list :deep(.q-field__control) {
  min-height: 40px !important;
}
.app-email-list__chip {
  margin: 2px 4px 2px 0;
}
</style>
