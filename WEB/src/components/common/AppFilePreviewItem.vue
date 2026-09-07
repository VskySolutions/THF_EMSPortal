<template>
  <q-item clickable class="app-file-item" @click="open">
    <q-item-section avatar>
      <q-avatar v-if="previewUrl" rounded size="40px">
        <img :src="previewUrl" alt="">
      </q-avatar>
      <q-icon v-else :name="icon" size="34px" color="primary" />
    </q-item-section>
    <q-item-section>
      <q-item-label class="ellipsis">{{ file.name }}</q-item-label>
      <q-item-label caption>{{ extLabel }}<template v-if="extLabel"> · </template>{{ formatFileSize(file.size) }}</q-item-label>
    </q-item-section>
    <q-item-section side>
      <q-btn flat round dense size="sm" icon="o_close" :disable="disable" @click.stop="$emit('remove')">
        <q-tooltip>Remove</q-tooltip>
      </q-btn>
    </q-item-section>
  </q-item>
</template>

<script setup>
// Shared preview row for a staged File: an image thumbnail for images, a type icon otherwise, with the name
// + extension + size.
import { ref, computed, watch, onBeforeUnmount } from "vue";
import { formatFileSize, iconForFile, isImageFile, extOf } from "composables/useFileDrop";

const props = defineProps({
  file: { type: Object, required: true },
  disable: { type: Boolean, default: false }
});
defineEmits(["remove"]);

const icon = computed(() => iconForFile(props.file));
const extLabel = computed(() => extOf(props.file?.name).replace(".", "").toUpperCase());

// Object URL for the image thumbnail; recreated when the file changes, revoked on unmount.
const previewUrl = ref(null);
const revoke = () => { if (previewUrl.value) { URL.revokeObjectURL(previewUrl.value); previewUrl.value = null; } };
watch(() => props.file, (f) => {
  revoke();
  if (f && isImageFile(f)) previewUrl.value = URL.createObjectURL(f);
}, { immediate: true });
onBeforeUnmount(revoke);

const open = () => {
  // Reuse the thumbnail URL for images; create a short-lived one for other types.
  const url = previewUrl.value || URL.createObjectURL(props.file);
  window.open(url, "_blank", "noopener");
  if (!previewUrl.value) setTimeout(() => URL.revokeObjectURL(url), 30000);
};
</script>

<style scoped>
.app-file-item { border: 1px solid #e2e7ee; border-radius: 8px; }
.app-file-item :deep(img) { object-fit: cover; }
</style>
