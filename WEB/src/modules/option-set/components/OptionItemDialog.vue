<template>
  <q-dialog v-model="open">
    <q-card style="min-width: 360px;">
      <q-card-section class="text-h6">{{ item ? "Edit value" : "New value" }}</q-card-section>
      <q-card-section class="q-pt-none column q-gutter-md">
        <app-text-field
          v-model="form.label"
          label="Label *"
          hint="Shown to users, e.g. NET 30"
        />
        <!-- The stored code. -->
        <app-text-field
          v-model="form.value"
          label="Value *"
          :readonly="isSystemItem"
          :hint="isSystemItem
            ? 'Fixed — the application writes this code. Rename the label above instead.'
            : 'Stored code, e.g. net_30'"
          :error="!!valueError"
          :error-message="valueError"
          @update:model-value="valueError = ''"
        >
          <template v-if="isSystemItem" #append>
            <q-icon name="o_lock" size="18px" color="grey-6" />
          </template>
        </app-text-field>

        <!-- Surfaced as this value's tooltip wherever it is offered or displayed, so a list whose labels
             look alike can explain itself at the point of use. -->
        <app-text-field
          v-model="form.description"
          label="Description"
          type="textarea"
          autogrow
          hint="Optional. Shown as this value's tooltip wherever it appears."
        />

        <!-- Cascading lists: tie this value to a parent-list item. -->
        <app-select
          v-if="parentOptions.length"
          v-model="form.parentItemId"
          :options="parentOptions"
          label="Valid under (parent)"
          clearable
        />

        <!-- Display colours shown for this value on the front UI. -->
        <div class="row q-col-gutter-md">
          <app-text-field v-model="form.backgroundColor" class="col" label="Background colour" placeholder="#e3f2fd" clearable>
            <template #append>
              <q-icon name="o_palette" class="cursor-pointer" :style="{ color: form.backgroundColor || '#9e9e9e' }">
                <q-popup-proxy cover transition-show="scale" transition-hide="scale">
                  <q-color v-model="form.backgroundColor" format-model="hex" default-view="palette" />
                </q-popup-proxy>
              </q-icon>
            </template>
          </app-text-field>
          <app-text-field v-model="form.textColor" class="col" label="Text colour" placeholder="#0d47a1" clearable>
            <template #append>
              <q-icon name="o_format_color_text" class="cursor-pointer" :style="{ color: form.textColor || '#9e9e9e' }">
                <q-popup-proxy cover transition-show="scale" transition-hide="scale">
                  <q-color v-model="form.textColor" format-model="hex" default-view="palette" />
                </q-popup-proxy>
              </q-icon>
            </template>
          </app-text-field>
        </div>
        <!-- The icon shown beside this value wherever it is rendered — an approver role, an email event. -->
        <app-text-field
          v-model="form.icon"
          label="Icon"
          placeholder="o_support_agent"
          clearable
          hint="Optional. A Material icon name, e.g. o_support_agent."
        >
          <template #append>
            <q-icon :name="form.icon || 'o_help_outline'" :color="form.icon ? 'primary' : 'grey-5'" />
          </template>
        </app-text-field>

        <div class="row items-center q-gutter-sm">
          <span class="text-caption text-grey-7">Preview:</span>
          <q-chip
            :style="{ backgroundColor: form.backgroundColor || '#e0e0e0', color: form.textColor || '#212121' }"
            :icon="form.icon || undefined"
            :label="form.label || 'Sample'"
          />
        </div>

        <q-toggle v-model="form.isDefault" label="Default selection" />
        <!-- Not offered on a value the application writes: hiding it would leave a stage the workflow still
             reaches with nothing to render, and the API refuses it. -->
        <!-- Hiding a value is refused only on a closed list, where it is a state the application still
             sets. -->
        <q-toggle v-if="item && !(isSystemItem && setIsClosed)" v-model="form.isActive" label="Active" />
      </q-card-section>
      <q-card-actions align="right">
        <q-btn v-close-popup flat no-caps label="Cancel" />
        <q-btn
          unelevated
          no-caps
          color="primary"
          :label="item ? 'Save' : 'Add'"
          :disable="!form.label.trim() || !form.value.trim()"
          :loading="saving"
          @click="submit"
        />
      </q-card-actions>
    </q-card>
  </q-dialog>
</template>

<script setup>
import { ref, reactive, computed, watch } from "vue";
import { optionSetApi, getApiErrorMessage, getApiErrorCode, ApiErrorCodes } from "services/api";
import { useNotify } from "composables/useNotify";
import AppTextField from "components/common/AppTextField.vue";
import AppSelect from "components/common/AppSelect.vue";

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  setId: { type: String, required: true },
  // Null = create a new value; otherwise the value being edited.
  item: { type: Object, default: null },
  // For dependency lists: the parent set's items as { label, value } options.
  parentOptions: { type: Array, default: () => [] },
  // Whether the LIST is closed — the application branches on its values and the set of them is fixed.
  // Decides only whether a seeded value may be hidden; the code lock is per-item (see isSystemItem).
  setIsClosed: { type: Boolean, default: false }
});
const emit = defineEmits(["update:modelValue", "saved"]);

const notify = useNotify();
const saving = ref(false);
const valueError = ref("");

// A value the application itself writes and branches on — an approval status, a form state.
const isSystemItem = computed(() => !!props.item?.isSystem);

const blankForm = () => ({
  label: "",
  value: "",
  description: "",
  parentItemId: null,
  isDefault: false,
  isActive: true,
  backgroundColor: null,
  textColor: null,
  icon: null
});
const form = reactive(blankForm());

const open = computed({
  get: () => props.modelValue,
  set: (v) => emit("update:modelValue", v)
});

watch(
  () => props.modelValue,
  (isOpen) => {
    if (!isOpen) return;
    valueError.value = "";
    if (props.item) {
      Object.assign(form, blankForm(), {
        label: props.item.label,
        value: props.item.value,
        description: props.item.description || "",
        parentItemId: props.item.parentItemId,
        isDefault: props.item.isDefault,
        isActive: props.item.isActive,
        backgroundColor: props.item.backgroundColor,
        textColor: props.item.textColor,
        icon: props.item.icon
      });
    } else {
      Object.assign(form, blankForm());
    }
  }
);

const submit = async () => {
  saving.value = true;
  try {
    const payload = {
      label: form.label.trim(),
      value: form.value.trim(),
      description: form.description.trim() || null,
      parentItemId: form.parentItemId || null,
      isDefault: form.isDefault,
      backgroundColor: form.backgroundColor || null,
      textColor: form.textColor || null,
      icon: form.icon?.trim() || null
    };
    if (props.item) {
      await optionSetApi.updateItem(props.setId, props.item.id, { ...payload, isActive: form.isActive });
      notify.success("Value updated.");
    } else {
      await optionSetApi.createItem(props.setId, payload);
      notify.success("Value added.");
    }
    open.value = false;
    emit("saved");
  } catch (err) {
    if (getApiErrorCode(err) === ApiErrorCodes.DuplicateIdentifier) {
      valueError.value = "This value already exists in the list.";
    } else {
      notify.error(getApiErrorMessage(err));
    }
  } finally {
    saving.value = false;
  }
};
</script>
