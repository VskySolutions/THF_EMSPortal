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
        multiple
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

    <div v-if="modelValue.length" class="column q-gutter-xs q-mt-xs">
      <app-file-preview-item
        v-for="(file, i) in modelValue"
        :key="i"
        :file="file"
        :disable="disable"
        @remove="removeAt(i)"
      />
    </div>
  </div>
</template>

<script setup>
// Standard multi-file upload: a modern click-or-drag-and-drop dropzone with the external top-left label.
// v-model holds the array of chosen Files (appended on each pick/drop).
import { ref } from "vue";
import AppFieldLabel from "components/common/AppFieldLabel.vue";
import AppFilePreviewItem from "components/common/AppFilePreviewItem.vue";
import { useFileDrop, validateFiles } from "composables/useFileDrop";

const props = defineProps({
  modelValue: { type: Array, default: () => [] },
  label: { type: String, default: "" },
  required: { type: Boolean, default: false },
  // Comma-separated extensions, e.g. ".pdf,.png,.jpg".
  accept: { type: String, default: "" },
  maxSizeMb: { type: Number, default: null },
  // Cap on total selected files (null = unlimited).
  maxFiles: { type: Number, default: null },
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

const add = (fileList) => {
  error.value = "";
  const { accepted, error: err } = validateFiles(fileList, { accept: props.accept, maxSizeMb: props.maxSizeMb });
  if (err) { error.value = err; emit("rejected", err); }
  let next = [...props.modelValue, ...accepted];
  if (props.maxFiles && next.length > props.maxFiles) {
    error.value = `You can upload at most ${props.maxFiles} files.`;
    next = next.slice(0, props.maxFiles);
  }
  if (accepted.length) emit("update:modelValue", next);
};

const onChange = (e) => {
  add(e.target.files);
  e.target.value = ""; // allow re-selecting the same file(s)
};

const onDrop = (e) => {
  onDragLeave();
  if (props.disable) return;
  add(e.dataTransfer?.files);
};

const removeAt = (index) => {
  error.value = "";
  emit("update:modelValue", props.modelValue.filter((_, i) => i !== index));
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
