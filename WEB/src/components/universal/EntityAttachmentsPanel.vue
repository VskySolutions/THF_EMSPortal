<template>
  <div class="column q-gutter-sm">
    <!-- The accepted types, the size and the count are the shared ones (see useFileDrop): this panel and
         the REMS request's attachments are the same question asked in two places. -->
    <app-multi-file-upload
      v-model="picked"
      label="Upload files"
      :hint="ATTACHMENT_HINT"
      :max-files="MAX_ATTACHMENT_FILES"
      :max-size-mb="MAX_UPLOAD_MB"
      :loading="uploading"
      :accept="ATTACHMENT_ACCEPT"
    />
    <div v-if="picked.length" class="row justify-end">
      <q-btn
        unelevated no-caps color="primary" icon="o_upload"
        :label="`Upload ${picked.length} file${picked.length > 1 ? 's' : ''}`"
        :loading="uploading"
        @click="uploadAll"
      />
    </div>

    <q-inner-loading :showing="loading && !attachments.length" />
    <div v-if="!loading && !attachments.length" class="text-grey-6 q-pa-md text-center">No attachments yet.</div>

    <!-- Same preview row every other list of saved files uses: the icon for the type, a click that opens it
         in a new tab, and an ✕ that takes it off the record. -->
    <div class="column q-gutter-xs">
      <div v-for="a in attachments" :key="a.id">
        <app-stored-file-item
          :file="a" removable :fetch-blob="fetchAttachment"
          @remove="remove(a)"
        />
        <div class="text-caption text-grey-6 q-pl-sm q-pt-xs">
          {{ a.uploadedByName || "Unknown" }} · {{ formatDate(a.createdOnUtc) }}
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from "vue";
import { ufAttachmentsApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useDateFormat } from "composables/useDateFormat";
import {
  ATTACHMENT_ACCEPT, ATTACHMENT_HINT, MAX_ATTACHMENT_FILES, MAX_UPLOAD_MB
} from "composables/useFileDrop";
import AppMultiFileUpload from "components/common/AppMultiFileUpload.vue";
import AppStoredFileItem from "components/common/AppStoredFileItem.vue";

const props = defineProps({
  entityType: { type: Number, required: true },
  entityId: { type: String, required: true }
});

const notify = useNotify();
const { confirm } = useConfirm();
const { formatDate } = useDateFormat();

const attachments = ref([]);
const loading = ref(false);
const uploading = ref(false);
const picked = ref([]);

const load = async () => {
  loading.value = true;
  try {
    attachments.value = (await ufAttachmentsApi.list(props.entityType, props.entityId)) || [];
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loading.value = false;
  }
};

const uploadAll = async () => {
  if (!picked.value.length) return;
  uploading.value = true;
  let uploaded = 0;
  try {
    // Upload each staged file; report per-file failures but keep going.
    for (const file of picked.value) {
      try {
        await ufAttachmentsApi.upload(props.entityType, props.entityId, file);
        uploaded += 1;
      } catch (err) {
        notify.error(`${file.name}: ${getApiErrorMessage(err)}`);
      }
    }
    picked.value = [];
    if (uploaded) {
      notify.success(`${uploaded} attachment${uploaded > 1 ? "s" : ""} uploaded.`);
      await load();
    }
  } finally {
    uploading.value = false;
  }
};

// UF attachments live behind an endpoint of their own rather than in the media store, so the preview
// row is handed the fetch that reaches them.
const fetchAttachment = (a) => ufAttachmentsApi.download(a.id);

const remove = async (a) => {
  const ok = await confirm({
    title: "Delete attachment",
    message: `Permanently delete "${a.fileName}"? This cannot be undone.`,
    confirmLabel: "Delete",
    type: "danger"
  });
  if (!ok) return;
  try {
    await ufAttachmentsApi.remove(a.id);
    notify.success("Attachment deleted.");
    await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

onMounted(load);
defineExpose({ load });
</script>
