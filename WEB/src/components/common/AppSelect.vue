<template>
  <div class="app-field">
    <app-field-label :label="label" :required="required" :info="info" />
    <q-select
      v-model="model"
      :options="visibleOptions"
      :loading="loading"
      :clearable="clearable"
      :multiple="multiple"
      :option-value="optionValue"
      :option-label="optionLabel"
      :emit-value="emitValue"
      :map-options="mapOptions"
      :dense="dense"
      :use-chips="chips"
      :readonly="readonly"
      :disable="disable"
      :autocomplete="autocomplete"
      :aria-label="ariaLabel"
      :use-input="searchable"
      :input-debounce="inputDebounce"
      :fill-input="typeahead"
      :hide-selected="typeahead"
      :rules="rules"
      :hint="hint"
      outlined
      hide-bottom-space
      :error="error"
      :error-message="errorMessage"
      class="app-select"
      @input-value="search = $event || ''"
      @popup-show="search = ''"
      @popup-hide="search = ''"
    >
      <!-- Multi-select renders each selection as a consistent badge/chip (removable when editable). -->
      <template v-if="chips" #selected-item="scope">
        <q-chip
          :removable="!readonly && !disable"
          dense
          :tabindex="scope.tabindex"
          color="teal-1"
          text-color="primary"
          class="app-select__chip"
          @remove="scope.removeAtIndex(scope.index)"
        >
          {{ scope.opt.label ?? scope.opt }}
        </q-chip>
      </template>

      <!-- Category labels in a grouped list — options carrying `header: true`, as the role picker's
           System / Operational / REMS Seats / Custom do. -->
      <template v-if="hasHeaders" #option="scope">
        <q-item-label v-if="scope.opt.header" header class="app-select__group">
          {{ scope.opt.label }}
        </q-item-label>
        <q-item v-else v-bind="scope.itemProps">
          <q-item-section v-if="multiple" side>
            <q-checkbox
              :model-value="scope.selected" dense
              @update:model-value="scope.toggleOption(scope.opt)"
            />
          </q-item-section>
          <q-item-section>
            <q-item-label>{{ scope.opt.label }}</q-item-label>
          </q-item-section>
        </q-item>
      </template>

      <template v-if="loading" #append>
        <q-spinner size="20px" color="primary" />
      </template>
      <template #no-option>
        <q-item>
          <q-item-section class="text-grey-6">No options</q-item-section>
        </q-item>
      </template>
      <template v-if="$slots.after" #after>
        <slot name="after" />
      </template>
    </q-select>
  </div>
</template>

<script setup>
import { ref, computed, toRef } from "vue";
import AppFieldLabel from "components/common/AppFieldLabel.vue";
import { useFieldLabel } from "composables/useFieldLabel";

const props = defineProps({
  modelValue: { type: [String, Number, Array, Object], default: null },
  options: { type: Array, default: () => [] },
  label: { type: String, default: "" },
  // Force the required marker; a trailing "*" in the label also marks it required.
  required: { type: Boolean, default: false },
  // Explains what this list is scoped to (a user group, a role, an option list). Use it whenever the
  // options are a filtered set — otherwise an empty or short dropdown just looks broken.
  info: { type: String, default: "" },
  // Accessible name for a select with no visible label of its own — the dial-code picker inside
  // AppPhoneInput, which sits under the phone field's single label. Ignored when `label` is set.
  ariaLabel: { type: String, default: "" },
  loading: { type: Boolean, default: false },
  clearable: { type: Boolean, default: true },
  multiple: { type: Boolean, default: false },
  optionValue: { type: String, default: "value" },
  optionLabel: { type: String, default: "label" },
  emitValue: { type: Boolean, default: true },
  mapOptions: { type: Boolean, default: true },
  error: { type: Boolean, default: false },
  errorMessage: { type: String, default: "" },
  // Validation rules run by the surrounding q-form (same contract as AppTextField).
  rules: { type: Array, default: () => [] },
  // Helper line under the control (same contract as AppTextField). `hide-bottom-space` only stops the
  // space being RESERVED when there is nothing to show, so a hint still renders when one is supplied.
  hint: { type: String, default: "" },
  // Dense by default so selects match the dense text inputs (consistent field height).
  dense: { type: Boolean, default: true },
  // Render selections as chips/badges; defaults on for multi-select.
  useChips: { type: Boolean, default: undefined },
  readonly: { type: Boolean, default: false },
  disable: { type: Boolean, default: false },
  // Disable the browser's autofill on the filter input by default (opt back in with "on").
  autocomplete: { type: String, default: "off" },
  // Type-to-search: opens an input inside the control that narrows the options.
  useInput: { type: Boolean, default: undefined },
  // 0 because the lists filtered this way are in-memory (Quasar's own default is 500ms).
  inputDebounce: { type: [Number, String], default: 0 }
});

const emit = defineEmits(["update:modelValue"]);

const model = computed({
  get: () => props.modelValue,
  set: (val) => emit("update:modelValue", val)
});

// Multi-select shows selections as badges by default; single-select stays inline text.
const chips = computed(() => (props.useChips === undefined ? props.multiple : props.useChips));

// Past this many options a list is quicker to type than to scroll, so search switches on by itself.
const SEARCH_THRESHOLD = 10;
const searchable = computed(() =>
  (props.useInput === undefined ? props.options.length > SEARCH_THRESHOLD : props.useInput));

// A searchable single-select reads as an autocomplete: the selection sits in the search input itself
// instead of beside it. Multi-select keeps its chips and a separate input.
const typeahead = computed(() => searchable.value && !props.multiple);

// Search is handled HERE rather than through QSelect's `filter` event on purpose.
const search = ref("");
const optionText = (opt) =>
  String((opt !== null && typeof opt === "object" ? opt[props.optionLabel] : opt) ?? "");

// The key QSelect matches the model against, for options of either shape — an object, or a bare string.
const optionKey = (opt) => (opt !== null && typeof opt === "object" ? opt[props.optionValue] : opt);

const selectedKeys = computed(() => {
  const val = props.modelValue;
  if (val === null || val === undefined) return [];
  return (Array.isArray(val) ? val : [val]).map(optionKey);
});

// Whatever is selected stays in the list however the search narrows it.
const isSelectedOption = (opt) => {
  const key = optionKey(opt);
  return key !== undefined && selectedKeys.value.includes(key);
};

// True when the list carries category headers (see useRoleOptions). Only then is the option slot taken
// over, so every other select in the app keeps Quasar's default rendering.
const hasHeaders = computed(() => props.options.some((o) => o && o.header === true));

const visibleOptions = computed(() => {
  const needle = searchable.value ? search.value.trim().toLowerCase() : "";
  if (needle === "") return props.options;
  return props.options.filter((opt) =>
    optionText(opt).toLowerCase().includes(needle) || isSelectedOption(opt));
});

// Keep the control accessible now that the visible label is external — and still named when there is no
// visible label at all.
const { text: labelText } = useFieldLabel(toRef(props, "label"), toRef(props, "required"));
const ariaLabel = computed(() => labelText.value || props.ariaLabel);
</script>

<style scoped>
/* Consistent control height for single-selects (matches dense text fields). Multi-selects with
   chips grow as needed but keep the same minimum. !important + higher specificity beats the
   legacy global `.q-field__control { min-height: auto !important }` rule in app.scss. */
.app-select :deep(.q-field__control) {
  min-height: 40px !important;
}
.app-select__chip {
  margin: 2px 4px 2px 0;
}
/* A category heading inside the options list: reads as a label rather than as a greyed-out choice. */
.app-select__group {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--q-primary);
  padding: 10px 16px 2px;
  min-height: 0;
}
</style>
