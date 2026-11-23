<template>
  <div>
    <div v-if="failedRows.length" class="row items-center q-mb-sm import-rows__bar">
      <div class="text-body2 text-grey-8">{{ failedRows.length }} {{ failedRows.length === 1 ? "line" : "lines" }} with errors</div>
      <q-space />
      <q-btn outline dense no-caps color="primary" icon="o_content_copy" label="Copy failed rows" @click="copyFailed" />
      <q-btn outline dense no-caps color="primary" icon="o_download" label="Download failed rows" @click="downloadFailed" />
    </div>

    <q-table
      flat bordered dense
      row-key="rowNumber"
      :rows="rows"
      :columns="columns"
      :rows-per-page-options="[0]"
      hide-bottom
      virtual-scroll
      class="import-rows__table"
    >
      <template #body-cell-status="cell">
        <q-td :props="cell">
          <q-badge :color="STATUS[cell.value]?.color || 'grey'">{{ cell.value }}</q-badge>
        </q-td>
      </template>
      <template #body-cell-error="cell">
        <q-td :props="cell" class="import-rows__error">
          <span v-if="cell.value" class="text-negative">{{ cell.value }}</span>
          <template v-else>
            <!-- What an update changes, on a check. -->
            <span v-if="cell.row.note" class="text-grey-8 q-mr-sm">{{ cell.row.note }}</span>
            <router-link
              v-if="cell.row.userId" class="text-primary"
              :to="{ name: 'user_detail', params: { id: cell.row.userId } }"
            >
              Open user
            </router-link>
          </template>
        </q-td>
      </template>
    </q-table>
  </div>
</template>

<script setup>
// The lines of a user import file with what became of each, and the failed ones to take away and fix.
import { computed } from "vue";
import { copyToClipboard } from "quasar";
import { useNotify } from "composables/useNotify";
import { userImportRowsToCsv, downloadCsv } from "utils/userImportCsv";

const props = defineProps({
  rows: { type: Array, default: () => [] },
  // Names the downloaded file of failed rows.
  fileName: { type: String, default: "users.csv" }
});

const notify = useNotify();

const STATUS = {
  Ready: { color: "positive" },
  Created: { color: "positive" },
  Update: { color: "primary" },
  Updated: { color: "primary" },
  Unchanged: { color: "grey" },
  Error: { color: "negative" },
  Failed: { color: "negative" }
};

const columns = [
  { name: "rowNumber", label: "Line", field: "rowNumber", align: "left" },
  { name: "status", label: "Status", field: "status", align: "left" },
  { name: "firstName", label: "First Name", field: "firstName", align: "left" },
  { name: "lastName", label: "Last Name", field: "lastName", align: "left" },
  { name: "email", label: "Email", field: "email", align: "left" },
  { name: "roles", label: "Role", field: "roles", align: "left" },
  { name: "department", label: "Department", field: "department", align: "left" },
  { name: "phone", label: "Phone", field: "phone", align: "left" },
  { name: "maconomyNumber", label: "Maconomy Number", field: "maconomyNumber", align: "left" },
  { name: "accountStatus", label: "Account", field: "accountStatus", align: "left" },
  { name: "error", label: "Result", field: "error", align: "left" }
];

const failedRows = computed(() => props.rows.filter((r) => r.status === "Error" || r.status === "Failed"));

const copyFailed = async () => {
  try {
    await copyToClipboard(userImportRowsToCsv(failedRows.value));
    notify.success("Failed rows copied.");
  } catch {
    notify.error("Could not copy. Download the failed rows instead.");
  }
};

const downloadFailed = () =>
  downloadCsv(`${props.fileName.replace(/\.csv$/i, "")}-failed.csv`, userImportRowsToCsv(failedRows.value));
</script>

<style scoped>
.import-rows__bar {
  gap: 8px;
}
.import-rows__table {
  max-height: 46vh;
}
/* The reasons are sentences: let them wrap rather than widen the table. */
.import-rows__error {
  white-space: normal;
  min-width: 240px;
}
</style>
