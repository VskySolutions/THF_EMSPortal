<template>
  <div class="app-field">
    <app-field-label :label="label" :required="required" />

    <div
      class="app-file-drop"
      :class="{ 'app-file-drop--over': dragOver, 'app-file-drop--disabled': disable }"
      role="button"
      tabindex="0"
      @click="open"
      @keydown.enter.prevent="open"
      @keydown.space.prevent="open"
      @dragover.prevent="onDragOver"
      @dragleave.prevent="onDragLeave"
      @drop.prevent="onDrop"
    >
      <input
        ref="inputRef"
        type="file"
        class="app-file-drop__input"
        :accept="accept"
        :disabled="disable"
        @change="onChange"
      >
      <q-spinner v-if="loading" color="primary" size="28px" />
      <template v-else>
        <q-icon name="o_cloud_upload" size="30px" class="text-primary" />
        <div class="app-file-drop__text">
          <span class="text-primary text-weight-medium">Click to upload</span> or drag &amp; drop
        </div>
        <div v-if="hint" class="app-file-drop__hint">{{ hint }}</div>
      </template>
    </div>

    <div v-if="error || errorMessage" class="app-file-drop__error">{{ error || errorMessage }}</div>

    <app-file-preview-item
      v-if="modelValue"
      :file="modelValue"
      :disable="disable"
      class="q-mt-xs"
      @remove="clear"
    />
  </div>
</template>

<script setup>
// Standard single-file upload: a modern click-or-drag-and-drop dropzone with the external top-left label.
// v-model holds the chosen File (or null).
import { ref } from "vue";
import AppFieldLabel from "components/common/AppFieldLabel.vue";
import AppFilePreviewItem from "components/common/AppFilePreviewItem.vue";
import { useFileDrop, validateFiles } from "composables/useFileDrop";

const props = defineProps({
  modelValue: { type: Object, default: null },
  label: { type: String, default: "" },
  required: { type: Boolean, default: false },
  // Comma-separated extensions, e.g. ".pdf,.png,.jpg".
  accept: { type: String, default: "" },
  maxSizeMb: { type: Number, default: null },
  hint: { type: String, default: "" },
  loading: { type: Boolean, default: false },
  disable: { type: Boolean, default: false },
  errorMessage: { type: String, default: "" }
});
const emit = defineEmits(["update:modelValue", "rejected"]);

const { dragOver, onDragOver, onDragLeave } = useFileDrop();
const inputRef = ref(null);
const error = ref("");

const open = () => { if (!props.disable) inputRef.value?.click(); };

// The picked or dropped files, checked against `accept` / `max-size-mb`. Deliberately NOT named `accept`. A
// const declared in `<script setup>` shadows the prop of the same name when the template is compiled.
const acceptFiles = (fileList) => {
  error.value = "";
  const { accepted, error: err } = validateFiles(fileList, { accept: props.accept, maxSizeMb: props.maxSizeMb });
  if (err) { error.value = err; emit("rejected", err); }
  if (accepted.length) emit("update:modelValue", accepted[0]);
};

const onChange = (e) => {
  acceptFiles(e.target.files);
  e.target.value = ""; // allow re-selecting the same file
};

const onDrop = (e) => {
  onDragLeave();
  if (props.disable) return;
  acceptFiles(e.dataTransfer?.files);
};

const clear = () => {
  error.value = "";
  emit("update:modelValue", null);
};
</script>

<style scoped>
.app-file-drop {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 4px;
  min-height: 110px;
  padding: 16px;
  border: 1.5px dashed #c2cad6;
  border-radius: 10px;
  background: #fafbfc;
  cursor: pointer;
  text-align: center;
  transition: border-color 0.15s, background 0.15s;
}
.app-file-drop:hover,
.app-file-drop:focus-visible {
  border-color: var(--q-primary);
  background: #f3f8fc;
  outline: none;
}
.app-file-drop--over {
  border-color: var(--q-primary);
  background: #e9f3fb;
}
.app-file-drop--disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
.app-file-drop__input { display: none; }
.app-file-drop__text { font-size: 14px; color: #5a6675; }
.app-file-drop__hint { font-size: 12px; color: #8a94a3; }
.app-file-drop__error { font-size: 12px; color: #e53935; margin-top: 4px; }
</style>
