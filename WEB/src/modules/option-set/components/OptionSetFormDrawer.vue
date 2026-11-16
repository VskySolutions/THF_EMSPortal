<template>
  <app-form-drawer
    v-model="open"
    :title="setId ? 'Edit Option List' : 'New Option List'"
    save-label="Save"
    :saving="saving"
    @submit="onSubmit"
    @cancel="resetForm"
  >
    <q-form ref="formRef" greedy class="column q-gutter-md">
      <!-- The key is immutable after creation: it identifies the list. -->
      <app-text-field
        v-if="!setId"
        v-model="form.key"
        label="Key *"
        hint="Stable code, e.g. payment_terms"
        :error="!!keyError"
        :error-message="keyError"
        :rules="[(v) => !!v || 'Key is required']"
        @update:model-value="keyError = ''"
      />

      <app-text-field
        v-model="form.name"
        label="Name *"
        :rules="[(v) => !!v || 'Name is required']"
      />

      <app-select
        v-model="form.itemSortMode"
        :options="sortModeOptions"
        label="Item order"
        :clearable="false"
      />

      <!-- Optional dependency: this list's values are offered by the value picked on a parent list. -->
      <app-select
        v-model="form.parentSetId"
        :options="parentSetOptions"
        label="Depends on (optional)"
        clearable
        :hint="parentChanged ? 'Changing this clears which parent values each value is valid under.' : ''"
      />

      <q-toggle v-if="setId" v-model="form.isActive" label="Active" />
    </q-form>
  </app-form-drawer>
</template>

<script setup>
import { ref, reactive, computed, watch } from "vue";
import { optionSetApi, getApiErrorMessage, getApiErrorCode, ApiErrorCodes, OptionItemSortMode } from "services/api";
import { useNotify } from "composables/useNotify";
import AppFormDrawer from "components/common/AppFormDrawer.vue";
import AppSelect from "components/common/AppSelect.vue";
import AppTextField from "components/common/AppTextField.vue";

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  // Null = create; otherwise the set being edited.
  set: { type: Object, default: null },
  // The already-loaded sets, used to offer parent (dependency) lists.
  sets: { type: Array, default: () => [] }
});
const emit = defineEmits(["update:modelValue", "saved"]);

const notify = useNotify();

const sortModeOptions = [
  { label: "Alphabetical (A → Z)", value: OptionItemSortMode.AlphabeticalAsc },
  { label: "Alphabetical (Z → A)", value: OptionItemSortMode.AlphabeticalDesc },
  { label: "Custom (drag to order)", value: OptionItemSortMode.Custom }
];

const formRef = ref(null);
const saving = ref(false);
const keyError = ref("");

const blankForm = () => ({
  key: "",
  name: "",
  itemSortMode: OptionItemSortMode.AlphabeticalAsc,
  parentSetId: null,
  isActive: true
});
const form = reactive(blankForm());

const setId = computed(() => props.set?.id || null);

const open = computed({
  get: () => props.modelValue,
  set: (v) => emit("update:modelValue", v)
});

// Parent options: every other list (a list can't depend on itself).
const parentSetOptions = computed(() =>
  props.sets
    .filter((s) => s.id !== setId.value)
    .map((s) => ({ label: s.name, value: s.id })));

const parentChanged = computed(() =>
  !!setId.value && !!props.set?.parentSetId && form.parentSetId !== props.set.parentSetId);

const resetForm = () => {
  Object.assign(form, blankForm());
  keyError.value = "";
};

// Hydrate the form whenever the drawer opens for a given set (or for create).
watch(
  () => props.modelValue,
  (isOpen) => {
    if (!isOpen) return;
    if (props.set) {
      Object.assign(form, blankForm(), {
        name: props.set.name,
        itemSortMode: props.set.itemSortMode,
        parentSetId: props.set.parentSetId,
        isActive: props.set.isActive
      });
    } else {
      resetForm();
    }
  }
);

const onSubmit = async () => {
  if (!(await formRef.value?.validate())) return;
  saving.value = true;
  try {
    if (setId.value) {
      await optionSetApi.update(setId.value, {
        name: form.name.trim(),
        itemSortMode: form.itemSortMode,
        isActive: form.isActive,
        parentSetId: form.parentSetId || null
      });
      notify.success("Option list updated.");
    } else {
      await optionSetApi.create({
        key: form.key.trim(),
        name: form.name.trim(),
        parentSetId: form.parentSetId || null,
        itemSortMode: form.itemSortMode
      });
      notify.success("Option list created.");
    }
    resetForm();
    emit("saved");
  } catch (err) {
    if (getApiErrorCode(err) === ApiErrorCodes.DuplicateIdentifier) {
      keyError.value = "A list with this key already exists.";
    } else {
      notify.error(getApiErrorMessage(err));
    }
  } finally {
    saving.value = false;
  }
};
</script>
