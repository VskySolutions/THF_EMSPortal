<template>
  <div class="app-field">
    <app-field-label :label="label" :required="required" />

    <div class="row items-center q-gutter-md">
      <q-avatar :size="size" color="grey-3" text-color="grey-8">
        <img v-if="modelValue" :src="modelValue" alt="">
        <q-icon v-else :name="placeholderIcon" size="48px" />
      </q-avatar>
      <div class="column q-gutter-sm">
        <div class="row q-gutter-sm">
          <q-btn outline no-caps color="primary" icon="o_upload" :label="buttonLabel" :disable="disable" @click="pick" />
          <q-btn v-if="modelValue" flat no-caps color="negative" icon="o_delete" label="Remove" :disable="disable" @click="remove" />
        </div>
        <div v-if="hintText" class="app-image-upload__hint">{{ hintText }}</div>
        <div v-if="error" class="app-image-upload__error">{{ error }}</div>
      </div>
    </div>

    <input ref="inputRef" type="file" :accept="accept" class="hidden" @change="onSelected">

    <!-- Crop dialog -->
    <q-dialog v-model="cropOpen">
      <q-card style="min-width: 360px; max-width: 90vw;">
        <q-card-section class="text-subtitle1 text-weight-medium">Crop image</q-card-section>
        <q-separator />
        <q-card-section>
          <cropper ref="cropperRef" :src="cropSrc" :stencil-props="{ aspectRatio }" class="app-image-upload__cropper" />
        </q-card-section>
        <q-separator />
        <q-card-actions align="right">
          <q-btn flat no-caps label="Cancel" @click="cropOpen = false" />
          <q-btn unelevated no-caps color="primary" label="Apply" :loading="loading" @click="applyCrop" />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </div>
</template>

<script setup>
// Standard image upload with cropping (vue-advanced-cropper) and the external top-left label.
import { ref, computed } from "vue";
import { Cropper } from "vue-advanced-cropper";
import "vue-advanced-cropper/dist/style.css";
import AppFieldLabel from "components/common/AppFieldLabel.vue";
import { formatFileSize, isImageFile, validateFiles, MAX_UPLOAD_MB } from "composables/useFileDrop";

const props = defineProps({
  // The current image URL shown in the preview avatar.
  modelValue: { type: String, default: null },
  label: { type: String, default: "" },
  required: { type: Boolean, default: false },
  aspectRatio: { type: Number, default: 1 },
  size: { type: String, default: "96px" },
  accept: { type: String, default: "image/*" },
  // The server refuses anything larger, so the picker has to as well — see MAX_UPLOAD_MB. This box used
  // to promise nothing and check nothing: a photograph straight off a phone was accepted here.
  maxSizeMb: { type: Number, default: MAX_UPLOAD_MB },
  buttonLabel: { type: String, default: "Upload" },
  placeholderIcon: { type: String, default: "o_person" },
  hint: { type: String, default: "" },
  // True while the parent is uploading the cropped file.
  loading: { type: Boolean, default: false },
  disable: { type: Boolean, default: false },
  // File name given to the cropped image.
  fileName: { type: String, default: "image.png" }
});
const emit = defineEmits(["update:modelValue", "crop", "remove"]);

const inputRef = ref(null);
const cropperRef = ref(null);
const cropOpen = ref(false);
const cropSrc = ref(null);
// Why the last pick was refused, shown under the buttons. Same shape as the two file-upload components'
// own error line.
const error = ref("");

// Every other upload on the platform states its limit on the control itself. One that does not is one
// where the only way to learn the rule is to break it.
const hintText = computed(() =>
  props.hint || (props.maxSizeMb ? `Any image, up to ${props.maxSizeMb} MB` : ""));

const maxBytes = computed(() => (props.maxSizeMb ? props.maxSizeMb * 1024 * 1024 : null));

const pick = () => { if (!props.disable) inputRef.value?.click(); };

const onSelected = (e) => {
  const file = e.target.files?.[0];
  e.target.value = ""; // allow re-selecting the same file
  if (!file) return;
  // `accept="image/*"` is a filter on the picker, not a rule: every file dialog offers a way past it, and a
  // document chosen through that way used to open the cropper on a broken image and export a blank square.
  if (!isImageFile(file)) {
    error.value = `"${file.name}" is not an image.`;
    return;
  }
  // The same size check, and the same wording, the two file-upload components apply — this one just has
  // no `accept` list to enforce alongside it, because the type was settled a line above.
  const { error: tooBig } = validateFiles([file], { maxSizeMb: props.maxSizeMb });
  if (tooBig) { error.value = tooBig; return; }
  error.value = "";
  const reader = new FileReader();
  reader.onload = () => { cropSrc.value = reader.result; cropOpen.value = true; };
  reader.readAsDataURL(file);
};

const applyCrop = () => {
  const result = cropperRef.value?.getResult();
  if (!result?.canvas) { cropOpen.value = false; return; }
  result.canvas.toBlob((blob) => {
    if (!blob) { cropOpen.value = false; return; }
    // The CROPPED png is what gets uploaded, and it is not the file that was picked: a large photograph
    // re-encodes to a png bigger than the jpeg it came.
    if (maxBytes.value && blob.size > maxBytes.value) {
      error.value = `The cropped image comes to ${formatFileSize(blob.size)}, over the ` +
        `${props.maxSizeMb} MB limit. Crop a smaller area, or start from a smaller picture.`;
      cropOpen.value = false;
      return;
    }
    error.value = "";
    emit("crop", new File([blob], props.fileName, { type: "image/png" }));
  }, "image/png");
};

const remove = () => {
  emit("update:modelValue", null);
  emit("remove");
};

// The parent closes the dialog once its upload completes.
defineExpose({ closeCrop: () => { cropOpen.value = false; } });
</script>

<style scoped>
.app-image-upload__hint { font-size: 12px; color: #8a94a3; }
/* Same red the file-upload components use for a rejected pick. */
.app-image-upload__error { font-size: 12px; color: #e53935; }
.app-image-upload__cropper { max-height: 60vh; }
</style>
