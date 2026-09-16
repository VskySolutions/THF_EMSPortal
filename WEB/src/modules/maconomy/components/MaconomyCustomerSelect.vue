<template>
  <div class="app-field">
    <app-field-label :label="label" :info="info" />
    <!-- Built on QSelect directly rather than AppSelect: that one narrows a list it already holds, and
         this one has to ask Maconomy for the list every time the reader types. -->
    <q-select
      v-model="model"
      :options="options"
      option-label="text"
      option-value="value"
      emit-value
      map-options
      use-input
      fill-input
      hide-selected
      :input-debounce="300"
      :loading="loading"
      :readonly="readonly"
      :disable="disable"
      :hint="hint"
      :placeholder="placeholder"
      :aria-label="label"
      outlined
      dense
      clearable
      hide-bottom-space
      class="maconomy-customer-select"
      @filter="onFilter"
      @update:model-value="onPick"
    >
      <template #option="scope">
        <q-item v-bind="scope.itemProps">
          <q-item-section>
            <q-item-label>{{ scope.opt.text }}</q-item-label>
          </q-item-section>
        </q-item>
      </template>
      <!-- The dropdown says why it is empty: too few characters, no match, or what went wrong. -->
      <template #no-option>
        <q-item>
          <q-item-section :class="failed ? 'text-negative' : 'text-grey-6'">{{ emptyText }}</q-item-section>
        </q-item>
      </template>
    </q-select>
  </div>
</template>

<script setup>
// A customer picker fed by the Maconomy lookup: every two or more characters typed become a search, and
// the chosen customer stays in the list however the next search narrows it.
import { ref, computed } from "vue";
import { maconomyApi, getApiErrorMessage } from "services/api";
import AppFieldLabel from "components/common/AppFieldLabel.vue";

const MIN_CHARS = 2;
const TOO_SHORT = `Type at least ${MIN_CHARS} characters of a customer number or name.`;

const props = defineProps({
  // The customer number, which is what the API returns as `value`.
  modelValue: { type: String, default: null },
  label: { type: String, default: "Customer" },
  info: { type: String, default: "" },
  hint: { type: String, default: "" },
  placeholder: { type: String, default: "Type a customer number or name" },
  // The Super Admin's scope override; everyone else is pinned to their own tenant server-side.
  tenantId: { type: String, default: null },
  // Rows per search; null takes the tenant's default.
  limit: { type: Number, default: null },
  readonly: { type: Boolean, default: false },
  disable: { type: Boolean, default: false }
});
const emit = defineEmits(["update:modelValue", "selected"]);

const model = computed({
  get: () => props.modelValue,
  set: (val) => emit("update:modelValue", val)
});

const options = ref([]);
const selected = ref(null);
const loading = ref(false);
const failed = ref(false);
const emptyText = ref(TOO_SHORT);

// The chosen customer is kept at the top of every list, or QSelect would show its bare number as soon as
// a search came back without it.
const withSelected = (rows) => {
  const list = rows.map((r) => ({ text: r.text, value: r.value }));
  if (selected.value && !list.some((o) => o.value === selected.value.value)) list.unshift(selected.value);
  return list;
};

const onFilter = (val, update) => {
  const term = (val || "").trim();
  failed.value = false;
  if (term.length < MIN_CHARS) {
    emptyText.value = TOO_SHORT;
    update(() => { options.value = withSelected([]); });
    return;
  }
  loading.value = true;
  maconomyApi.searchCustomers(term, props.limit || undefined, props.tenantId || undefined)
    .then((rows) => {
      emptyText.value = "No customers match.";
      update(() => { options.value = withSelected(rows || []); });
    })
    .catch((err) => {
      // Said inside the dropdown rather than as a toast: a toast per keystroke is a wall of toasts.
      failed.value = true;
      emptyText.value = getApiErrorMessage(err, "The lookup failed.");
      update(() => { options.value = withSelected([]); });
    })
    .finally(() => { loading.value = false; });
};

const onPick = (value) => {
  selected.value = value ? options.value.find((o) => o.value === value) || null : null;
  emit("selected", selected.value);
};
</script>

<style scoped>
/* The same control height as every other dense field (see AppSelect). */
.maconomy-customer-select :deep(.q-field__control) {
  min-height: 40px !important;
}
</style>
