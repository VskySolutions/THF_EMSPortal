<template>
  <div class="brand-asset">
    <div class="brand-asset__frame" :class="`brand-asset__frame--${shape}`" :style="{ background: backdrop }">
      <img v-if="url" :src="url" alt="" class="brand-asset__image">
      <q-icon v-else :name="icon" size="26px" color="grey-5" />
    </div>
    <div class="brand-asset__body">
      <div class="text-body2 text-weight-medium">{{ label }}</div>
      <div class="text-caption text-grey-7">{{ hint }}</div>
      <div v-if="error" class="text-caption text-negative">{{ error }}</div>
      <div class="row q-gutter-sm q-mt-xs">
        <q-btn
          outline dense no-caps color="primary" icon="o_upload" :label="url ? 'Replace' : 'Upload'"
          :loading="busy" :disable="disable" @click="inputRef?.click()"
        />
        <q-btn
          v-if="url" flat dense no-caps color="negative" icon="o_delete" label="Remove"
          :disable="disable || busy" @click="$emit('remove')"
        />
      </div>
    </div>
    <input ref="inputRef" type="file" :accept="ACCEPT" class="hidden" @change="onPicked">
  </div>
</template>

<script setup>
// One image of the brand (a logo, the tab icon, the sign-in background): what is there now, and the two
// things that can be done to it.
import { ref } from "vue";
import { validateFiles } from "composables/useFileDrop";

// What the API accepts. No SVG: it is refused server-side, so offering it here would only fail later.
const ACCEPT = ".png,.jpg,.jpeg,.webp,.ico";

const props = defineProps({
  label: { type: String, required: true },
  hint: { type: String, default: "" },
  url: { type: String, default: null },
  icon: { type: String, default: "o_image" },
  // What the image is shown against — a logo for dark surfaces is invisible on a white tile.
  backdrop: { type: String, default: "#ffffff" },
  shape: { type: String, default: "wide" },
  maxSizeMb: { type: Number, default: 2 },
  busy: { type: Boolean, default: false },
  disable: { type: Boolean, default: false }
});
const emit = defineEmits(["upload", "remove"]);

const inputRef = ref(null);
const error = ref("");

const onPicked = (e) => {
  const picked = e.target.files;
  const { accepted, error: refused } = validateFiles(picked, { accept: ACCEPT, maxSizeMb: props.maxSizeMb });
  e.target.value = ""; // allow re-selecting the same file
  error.value = refused || "";
  if (accepted.length) emit("upload", accepted[0]);
};
</script>

<style scoped>
.brand-asset {
  display: flex;
  gap: 14px;
  align-items: center;
}
.brand-asset__frame {
  flex: 0 0 auto;
  display: flex;
  align-items: center;
  justify-content: center;
  height: 72px;
  padding: 8px;
  border: 1px solid var(--line);
  border-radius: 8px;
  overflow: hidden;
}
.brand-asset__frame--wide { width: 132px; }
.brand-asset__frame--square { width: 72px; }
.brand-asset__image {
  max-width: 100%;
  max-height: 100%;
  object-fit: contain;
}
.brand-asset__body { min-width: 0; }
</style>
