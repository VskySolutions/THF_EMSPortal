<template>
  <q-dialog v-model="open" persistent :maximized="$q.screen.lt.md">
    <q-card class="import-dialog">
      <q-card-section class="row items-center bg-primary text-white">
        <q-icon name="o_upload_file" size="22px" />
        <div class="text-h6 q-ml-sm">Import Users</div>
        <q-space />
        <q-btn flat round dense color="white" icon="o_close" :disable="busy" @click="open = false" />
      </q-card-section>
      <q-separator />

      <q-card-section class="import-dialog__body">
        <!-- Step 1: the file and the password. -->
        <template v-if="step === 'pick'">
          <div class="text-body2 text-grey-8 q-mb-md">
            Load users from the Excel template or a CSV file. FirstName, LastName, Email and Role are required; MaconomyNumber, Phone
            and Department are optional. Several roles go in one cell, separated by a semicolon or a comma;
            the template's second sheet lists the roles and departments that can be used.
            No email is sent.
          </div>
          <q-btn
            outline no-caps color="primary" icon="o_download" label="Download template" class="q-mb-md"
            @click="downloadTemplate"
          />
          <div class="row q-col-gutter-md">
            <div class="col-12 col-sm-6">
              <app-single-file-upload
                v-model="file" label="File" required accept=".xlsx,.csv" :max-size-mb="1"
                hint="Up to 500 users." :error-message="fileError"
                @rejected="fileError = 'Choose an .xlsx or .csv file of 1 MB or less.'"
              />
            </div>
            <div class="col-12 col-sm-6">
              <app-password-field
                v-model="password" label="Temporary password" required
                hint="Given to every user in the file. Each must change it at first sign-in."
                :error="!!passwordError" :error-message="passwordError"
              />
            </div>
          </div>
        </template>

        <!-- Step 2: what the import would do. Step 3: what it did. -->
        <template v-else>
          <q-banner rounded :class="bannerClass" class="q-mb-md">
            <template #avatar><q-icon :name="bannerIcon" /></template>
            {{ bannerText }}
          </q-banner>
          <user-import-rows :rows="rows" :file-name="file?.name || 'users.csv'" />
        </template>

        <q-inner-loading :showing="busy" />
      </q-card-section>

      <q-separator />
      <q-card-actions align="right" class="bg-grey-1">
        <template v-if="step === 'pick'">
          <q-btn flat no-caps color="grey-8" label="Cancel" @click="open = false" />
          <q-btn unelevated no-caps color="primary" label="Check file" :loading="busy" @click="check" />
        </template>
        <template v-else-if="step === 'review'">
          <q-btn flat no-caps color="grey-8" label="Choose another file" :disable="busy" @click="step = 'pick'" />
          <q-btn
            unelevated no-caps color="primary" :label="`Import ${readyCount} ${readyCount === 1 ? 'user' : 'users'}`"
            :disable="!readyCount" :loading="busy" @click="runImport"
          />
        </template>
        <template v-else>
          <q-btn outline no-caps color="primary" label="Import another file" @click="reset" />
          <q-btn unelevated no-caps color="primary" label="Close" @click="open = false" />
        </template>
      </q-card-actions>
    </q-card>
  </q-dialog>
</template>

<script setup>
// Users in bulk from a CSV file: pick it, see what the server makes of every line, then import the
// lines that pass. The server does all the reading, so the preview and the import cannot disagree.
import { ref, computed, watch } from "vue";
import { useQuasar } from "quasar";
import { userApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { downloadUserImportTemplate, userImportFileToCsv } from "utils/userImportCsv";

import AppSingleFileUpload from "components/common/AppSingleFileUpload.vue";
import AppPasswordField from "components/common/AppPasswordField.vue";
import UserImportRows from "components/user/UserImportRows.vue";

const open = defineModel({ type: Boolean, default: false });
const emit = defineEmits(["imported"]);

const $q = useQuasar();
const notify = useNotify();

const step = ref("pick"); // pick → review → done
const file = ref(null);
const password = ref("");
const fileError = ref("");
const passwordError = ref("");
const busy = ref(false);
const rows = ref([]);
const readyCount = ref(0);
const result = ref(null);

const reset = () => {
  step.value = "pick";
  file.value = null;
  password.value = "";
  fileError.value = "";
  passwordError.value = "";
  rows.value = [];
  readyCount.value = 0;
  result.value = null;
};

// A fresh start each time it opens: the password in particular is not left lying in the form.
watch(open, (isOpen) => { if (isOpen) reset(); });
watch(file, () => { fileError.value = ""; });
watch(password, () => { passwordError.value = ""; });

// The second sheet is this tenant's roles and departments as they stand now.
const downloadTemplate = async () => {
  try {
    await downloadUserImportTemplate(await userApi.importOptions());
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// The same rule the server applies, said here before a round trip.
const passwordProblem = () =>
  password.value.length >= 8 && /[A-Z]/.test(password.value) && /[0-9]/.test(password.value)
    ? ""
    : "At least 8 characters, with an uppercase letter and a digit.";

const payload = async () => ({
  fileName: file.value.name,
  content: await userImportFileToCsv(file.value),
  temporaryPassword: password.value
});

const check = async () => {
  fileError.value = file.value ? "" : "Choose the file to import.";
  passwordError.value = passwordProblem();
  if (fileError.value || passwordError.value) return;

  busy.value = true;
  try {
    const checked = await userApi.checkImport(await payload());
    rows.value = checked?.rows || [];
    readyCount.value = checked?.readyCount || 0;
    step.value = "review";
  } catch (err) {
    fileError.value = getApiErrorMessage(err);
  } finally {
    busy.value = false;
  }
};

const runImport = async () => {
  busy.value = true;
  try {
    const detail = await userApi.import(await payload());
    result.value = detail?.import || null;
    rows.value = detail?.rows || [];
    step.value = "done";
    notify.success("Import finished.");
    emit("imported");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    busy.value = false;
  }
};

const errorCount = computed(() => rows.value.length - readyCount.value);

const bannerText = computed(() => {
  if (step.value === "done") {
    const { createdCount = 0, failedCount = 0 } = result.value || {};
    return failedCount
      ? `${createdCount} created, ${failedCount} not imported. Copy or download the failed rows, correct them and import them again.`
      : `${createdCount} created.`;
  }
  if (!readyCount.value) return "No line can be imported. Correct the file and choose it again.";
  return errorCount.value
    ? `${readyCount.value} ready to import, ${errorCount.value} with errors. The lines with errors will be left out.`
    : `${readyCount.value} ready to import.`;
});

const hasProblems = computed(() => (step.value === "done" ? !!result.value?.failedCount : errorCount.value > 0));
const bannerClass = computed(() => (hasProblems.value ? "bg-orange-1 text-orange-10" : "bg-green-1 text-green-10"));
const bannerIcon = computed(() => (hasProblems.value ? "o_warning" : "o_check_circle"));
</script>

<style scoped>
.import-dialog {
  width: 1100px;
  max-width: 96vw;
}
.import-dialog__body {
  position: relative;
  min-height: 220px;
}
</style>
