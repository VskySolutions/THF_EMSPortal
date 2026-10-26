<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'Tenant Settings' }, { label: 'Maconomy' }]"
      show-back
      @back="$router.back()"
    />

    <div class="row q-col-gutter-md">
      <!-- The connection: where Maconomy is and who the portal signs in as. One per tenant. -->
      <div class="col-12 col-md-7">
        <q-card flat bordered>
          <q-card-section class="q-py-sm text-subtitle2 text-primary">
            <q-icon name="o_hub" size="18px" class="q-mr-xs" />Connection
          </q-card-section>
          <q-separator />
          <q-card-section>
            <q-form ref="formRef" greedy>
              <app-text-field
                v-model="form.baseUrl" label="Base URL *" class="q-mb-md"
                placeholder="https://host/maconomy-api" :rules="[requiredRule, httpsRule]"
                info="The API root, up to and including /maconomy-api. HTTPS only: the credentials travel in the login request."
              />
              <div class="row q-col-gutter-md q-mb-md">
                <app-text-field
                  v-model="form.instanceCode" label="Instance Code *" class="col-12 col-sm-6" :rules="[requiredRule]"
                  info="The short name in /auth/{code}/login and /containers/{code}/…"
                />
                <app-text-field v-model="form.userName" label="User Name *" class="col-12 col-sm-6" :rules="[requiredRule]" />
              </div>
              <app-password-field
                v-model="form.password" :label="exists ? 'Password' : 'Password *'" class="q-mb-md"
                :hint="exists ? 'Leave blank to keep the stored password.' : ''" :rules="exists ? [] : [requiredRule]"
              />
              <div class="row q-col-gutter-md q-mb-md">
                <app-text-field
                  v-model="form.containerId" label="Container Id" class="col-12 col-sm-6"
                  info="Kept for a later use; nothing reads it yet. The customer lookup names its own container."
                />
                <app-text-field
                  v-model.number="form.defaultLimit" label="Default Limit *" type="number" class="col-12 col-sm-6"
                  :rules="[limitRule]" info="Rows a customer search returns when the caller does not say (1 to 100)."
                />
              </div>
              <q-toggle v-model="form.isEnabled" label="Customer lookup enabled" />
            </q-form>

            <!-- What the stored credentials have achieved so far. -->
            <div v-if="exists" class="q-mt-md">
              <q-banner v-if="connection.lastLoginError" dense class="bg-red-1 text-negative rounded-borders q-mb-sm">
                <template #avatar><q-icon name="o_error" color="negative" /></template>
                Last login failed{{ connection.lastLoginErrorUtc ? ` at ${fmt.formatDateTime(connection.lastLoginErrorUtc)}` : "" }}:
                {{ connection.lastLoginError }}
              </q-banner>
              <div class="row items-center text-body2 text-grey-8">
                <q-icon
                  :name="token.present ? 'o_verified' : 'o_link_off'" :color="token.present ? 'positive' : 'grey-6'"
                  size="18px" class="q-mr-xs"
                />
                <span v-if="token.present">
                  Token issued {{ fmt.formatDateTime(token.issuedOnUtc) }}; a new one is fetched from
                  {{ fmt.formatDateTime(token.expiresOnUtc) }}.
                </span>
                <span v-else>No token held yet. Test the connection, or run a search.</span>
              </div>
            </div>
          </q-card-section>
          <q-separator />
          <q-card-actions class="q-gutter-sm bg-grey-1">
            <q-btn v-if="exists" flat no-caps color="negative" icon="o_delete" label="Delete" :disable="busy" @click="remove" />
            <q-space />
            <q-btn
              v-if="exists && token.present" flat no-caps color="grey-8" label="Forget token"
              :loading="forgetting" :disable="busy" @click="forgetToken"
            />
            <q-btn
              v-if="exists" outline no-caps color="primary" icon="o_sync_alt" label="Test connection"
              :loading="testing" :disable="busy || dirty" @click="testConnection"
            >
              <q-tooltip v-if="dirty">Save first: the test signs in with the stored credentials.</q-tooltip>
            </q-btn>
            <q-btn unelevated no-caps color="primary" label="Save" :loading="saving" :disable="busy" @click="save" />
          </q-card-actions>
        </q-card>
      </div>

      <!-- The lookup the connection is for, here so an admin can see it work before it is used anywhere. -->
      <div class="col-12 col-md-5">
        <q-card flat bordered>
          <q-card-section class="q-py-sm text-subtitle2 text-primary">
            <q-icon name="o_person_search" size="18px" class="q-mr-xs" />Customer lookup
          </q-card-section>
          <q-separator />
          <q-card-section>
            <p class="text-body2 text-grey-8">
              Type two or more characters of a customer number or name. The search signs in by itself when it
              has to, so this is also a test of the whole route.
            </p>
            <maconomy-customer-select
              v-model="customer" :tenant-id="scopeTenantId()" :disable="!exists"
              :hint="exists ? '' : 'Save a connection first.'" @selected="picked = $event"
            />
            <div v-if="picked" class="q-mt-md">
              <div class="text-caption text-grey-7">Selected</div>
              <div class="text-body1 text-weight-medium">{{ picked.text }}</div>
              <div class="text-caption text-grey-7">value <code>{{ picked.value }}</code></div>
              <div class="text-caption text-grey-7">
                <template v-if="picked.emailAddress">email <code>{{ picked.emailAddress }}</code></template>
                <template v-else>no email</template>
              </div>
              <div class="text-caption text-grey-7">
                <template v-if="picked.phoneNumber">phone <code>{{ picked.phoneNumber }}</code></template>
                <template v-else>no phone</template>
              </div>
            </div>
          </q-card-section>
        </q-card>
      </div>
    </div>

    <!-- The customer sync: a fully approved Brand New Client request posts its client to Maconomy. It is
         standalone from approval, so what failed, or was approved while it was off, waits here for a hand.
         Always the caller's OWN tenant: the scope control above moves the connection, not this. -->
    <q-card flat bordered class="q-mt-md">
      <q-card-section class="q-py-sm text-subtitle2 text-primary row items-center">
        <q-icon name="o_cloud_upload" size="18px" class="q-mr-xs" />Customer sync
        <span v-if="canChooseTenant" class="text-caption text-grey-7 q-ml-sm">— your own tenant</span>
        <q-space />
        <q-badge :color="sync.enabled ? 'positive' : 'grey-6'">{{ sync.enabled ? "On" : "Off" }}</q-badge>
      </q-card-section>
      <q-separator />
      <q-card-section>
        <p class="text-body2 text-grey-8 q-mb-xs">
          When on, a Brand New Client request that is fully approved posts its client to Maconomy as a new
          customer, with the number left to Maconomy's series and written back onto the client. Starting it
          posts nothing by itself: requests approved while it was off, and attempts that failed, are listed
          below for syncing by hand.
        </p>
        <div v-if="sync.changedOnUtc" class="text-caption text-grey-7 q-mb-md">
          {{ sync.enabled ? "Started" : "Stopped" }} {{ fmt.formatDateTime(sync.changedOnUtc) }}{{ sync.changedByName ? ` by ${sync.changedByName}` : "" }}.
        </div>

        <div class="row q-col-gutter-md q-mb-md">
          <div v-for="tile in syncTiles" :key="tile.label" class="col-6 col-sm-3">
            <div class="text-h6">{{ tile.value }}</div>
            <div class="text-caption text-grey-7">{{ tile.label }}</div>
          </div>
        </div>

        <div class="text-subtitle2 q-mb-xs">Waiting for a hand</div>
        <q-table
          flat dense bordered :rows="unsynced" :columns="unsyncedColumns" row-key="remsId" :loading="syncLoading"
          :pagination="{ rowsPerPage: 10 }" no-data-label="Nothing is waiting: every approved new client is in Maconomy."
        >
          <template #body-cell-remsNumber="cell">
            <q-td :props="cell">
              <router-link :to="{ name: 'rems_request', params: { id: cell.row.remsId } }" class="text-primary">
                {{ cell.row.remsNumber }}
              </router-link>
            </q-td>
          </template>
          <template #body-cell-status="cell">
            <q-td :props="cell"><maconomy-sync-chip :sync="cell.row.status" /></q-td>
          </template>
          <template #body-cell-failureReason="cell">
            <q-td :props="cell" class="ms-reason">{{ cell.row.failureReason || "—" }}</q-td>
          </template>
          <template #body-cell-lastAttemptOnUtc="cell">
            <q-td :props="cell">{{ cell.row.lastAttemptOnUtc ? fmt.formatDateTime(cell.row.lastAttemptOnUtc) : "—" }}</q-td>
          </template>
          <template #body-cell-actions="cell">
            <q-td :props="cell">
              <q-btn
                dense outline no-caps color="primary" icon="o_cloud_upload" label="Sync now"
                :loading="syncingRems === cell.row.remsId" :disable="!sync.enabled || !!syncingRems" @click="syncNow(cell.row)"
              >
                <q-tooltip v-if="!sync.enabled">Start the customer sync first.</q-tooltip>
              </q-btn>
            </q-td>
          </template>
        </q-table>
      </q-card-section>
      <q-separator />
      <q-card-actions class="q-gutter-sm bg-grey-1">
        <q-space />
        <q-btn outline no-caps color="grey-8" icon="o_refresh" label="Refresh" :loading="syncLoading" @click="loadSync" />
        <q-btn
          v-if="sync.enabled" outline no-caps color="negative" icon="o_pause" label="Stop"
          :loading="syncToggling" :disable="busy" @click="setSync(false)"
        />
        <q-btn
          v-else unelevated no-caps color="primary" icon="o_play_arrow" label="Start"
          :loading="syncToggling" :disable="busy || !exists || !connection?.isEnabled" @click="setSync(true)"
        >
          <q-tooltip v-if="!exists">Save a connection first.</q-tooltip>
          <q-tooltip v-else-if="!connection?.isEnabled">Enable the connection first.</q-tooltip>
        </q-btn>
      </q-card-actions>
    </q-card>
  </q-page>
</template>

<script setup>
// The tenant's Maconomy connection — credentials, a test of them, and the customer lookup they power.
import { ref, reactive, computed, onMounted, onUnmounted } from "vue";
import { maconomyApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useDateFormat } from "composables/useDateFormat";
import { useTenantOptions } from "composables/useTenantOptions";
import { useTenantScope } from "composables/useTenantScope";

import AppListHeader from "components/common/AppListHeader.vue";
import AppTextField from "components/common/AppTextField.vue";
import AppPasswordField from "components/common/AppPasswordField.vue";
import MaconomyCustomerSelect from "modules/maconomy/components/MaconomyCustomerSelect.vue";
import MaconomySyncChip from "modules/rems/components/MaconomySyncChip.vue";

const notify = useNotify();
const { confirm } = useConfirm();
const fmt = useDateFormat();
const { canChooseTenant } = useTenantOptions();

// The tenant in view comes from the toolbar's global scope control, as on the Email Accounts page.
const { selectedTenantId } = useTenantScope();
const scopeTenantId = () => (canChooseTenant.value && selectedTenantId.value ? selectedTenantId.value : undefined);

// ---- The form ----
const blankForm = () => ({
  baseUrl: "",
  instanceCode: "",
  userName: "",
  password: "",
  containerId: "",
  defaultLimit: 25,
  isEnabled: true
});
const form = reactive(blankForm());
const formRef = ref(null);

// The stored connection, secrets masked; null until one is saved.
const connection = ref(null);
const exists = computed(() => !!connection.value);
const token = computed(() => connection.value?.token || { present: false });

const saving = ref(false);
const testing = ref(false);
const forgetting = ref(false);
const deleting = ref(false);
const busy = computed(() => saving.value || testing.value || forgetting.value || deleting.value);

// What was last loaded or saved, so Test connection can refuse to run against edits that are not stored.
const savedSnapshot = ref("");
const snapshot = () => JSON.stringify({ ...form, password: "" });
const dirty = computed(() => snapshot() !== savedSnapshot.value || !!form.password);

const apply = (c) => {
  connection.value = c || null;
  Object.assign(form, blankForm());
  if (c) {
    form.baseUrl = c.baseUrl;
    form.instanceCode = c.instanceCode;
    form.userName = c.userName;
    form.containerId = c.containerId || "";
    form.defaultLimit = c.defaultLimit;
    form.isEnabled = c.isEnabled;
  }
  savedSnapshot.value = snapshot();
};

const load = async () => {
  try {
    apply(await maconomyApi.getConnection(scopeTenantId()));
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// The global scope switch announces itself; this page is not a list, so it listens for itself.
const loadAll = () => { load(); loadSync(); };
onMounted(() => { loadAll(); window.addEventListener("tenant-switched", loadAll); });
onUnmounted(() => window.removeEventListener("tenant-switched", loadAll));

const requiredRule = (v) => (v !== null && v !== undefined && String(v).trim() !== "") || "Required";
const httpsRule = (v) => /^https:\/\/\S+$/i.test(String(v || "").trim()) || "Enter an https:// URL";
const limitRule = (v) => (Number.isInteger(Number(v)) && Number(v) >= 1 && Number(v) <= 100) || "Enter 1–100";

const save = async () => {
  if (!(await formRef.value?.validate())) return;
  saving.value = true;
  try {
    const payload = {
      baseUrl: form.baseUrl.trim(),
      instanceCode: form.instanceCode.trim(),
      userName: form.userName.trim(),
      containerId: form.containerId.trim() || null,
      defaultLimit: Number(form.defaultLimit),
      isEnabled: form.isEnabled
    };
    // Only sent when one was typed; omitting it keeps the stored password.
    if (form.password) payload.password = form.password;
    apply(await maconomyApi.saveConnection(payload, scopeTenantId()));
    notify.success("Maconomy connection saved.");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    saving.value = false;
  }
};

// A fresh login with the stored credentials. Either way the row is re-read afterwards: the token status
// and the last error both live on it.
const testConnection = async () => {
  testing.value = true;
  try {
    const result = await maconomyApi.login(scopeTenantId());
    notify.success(`Connected to Maconomy. Token issued ${fmt.formatDateTime(result.issuedOnUtc)}.`);
  } catch (err) {
    notify.error(getApiErrorMessage(err, "Maconomy could not be reached."));
  } finally {
    testing.value = false;
    await load();
  }
};

const forgetToken = async () => {
  forgetting.value = true;
  try {
    await maconomyApi.forgetToken(scopeTenantId());
    notify.success("Token forgotten. The next call signs in afresh.");
    await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    forgetting.value = false;
  }
};

const remove = async () => {
  const ok = await confirm({
    title: "Delete the Maconomy connection",
    message: "Remove the connection, its credentials and its token? The customer lookup stops working for " +
      "this tenant until a new connection is saved.",
    confirmLabel: "Delete",
    type: "danger"
  });
  if (!ok) return;
  deleting.value = true;
  try {
    await maconomyApi.deleteConnection(scopeTenantId());
    notify.success("Maconomy connection deleted.");
    apply(null);
    customer.value = null;
    picked.value = null;
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    deleting.value = false;
  }
};

// ---- The lookup ----
const customer = ref(null);
const picked = ref(null);

// ---- The customer sync ----
const blankSync = () => ({
  connected: false,
  enabled: false,
  changedOnUtc: null,
  changedByName: null,
  synced: 0,
  linked: 0,
  failed: 0,
  notSynced: 0,
  pending: 0
});
const sync = ref(blankSync());
const unsynced = ref([]);
const syncLoading = ref(false);
const syncToggling = ref(false);
// The request being synced by hand right now, so only its button spins.
const syncingRems = ref(null);

const syncTiles = computed(() => [
  // Linked counts as synced: the client is in Maconomy either way.
  { label: "In Maconomy", value: sync.value.synced + sync.value.linked },
  { label: "Failed", value: sync.value.failed },
  { label: "Not synced", value: sync.value.notSynced },
  { label: "Queued", value: sync.value.pending }
]);

const unsyncedColumns = [
  { name: "remsNumber", label: "Request", field: "remsNumber", align: "left" },
  { name: "clientName", label: "Client", field: "clientName", align: "left" },
  { name: "status", label: "Status", field: "status", align: "left" },
  { name: "failureReason", label: "Reason", field: "failureReason", align: "left" },
  { name: "lastAttemptOnUtc", label: "Last attempt", field: "lastAttemptOnUtc", align: "left" },
  { name: "actions", label: "", field: "syncId", align: "right" }
];

const loadSync = async () => {
  syncLoading.value = true;
  try {
    // The caller's OWN tenant, whatever the scope control above says: these are their REMS requests.
    const [overview, rows] = await Promise.all([
      maconomyApi.customerSync(),
      maconomyApi.customerSyncUnsynced()
    ]);
    sync.value = overview || blankSync();
    unsynced.value = rows || [];
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    syncLoading.value = false;
  }
};

const setSync = async (enabled) => {
  syncToggling.value = true;
  try {
    sync.value = await (enabled ? maconomyApi.startCustomerSync() : maconomyApi.stopCustomerSync());
    notify.success(enabled
      ? "Customer sync started. Approvals from now on post their new client; anything already waiting is synced by hand."
      : "Customer sync stopped.");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    syncToggling.value = false;
  }
};

// One attempt, inline. A failed post comes back as a result, not an error, so it is read off the status.
const syncNow = async (row) => {
  syncingRems.value = row.remsId;
  try {
    const result = await maconomyApi.runCustomerSync(row.remsId);
    if (result?.status === "failed") {
      notify.error(`${row.remsNumber}: ${result.failureReason || "the sync failed."}`);
    } else if (result?.status === "linked") {
      notify.success(`${row.remsNumber}: linked to the existing Maconomy customer ${result.customerNumber}.`);
    } else {
      notify.success(`${row.remsNumber}: Maconomy customer ${result?.customerNumber} created.`);
    }
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    syncingRems.value = null;
    await loadSync();
  }
};
</script>

<style scoped>
/* A Maconomy error message can be a sentence or three; the column wraps rather than widening the table. */
.ms-reason {
  max-width: 420px;
  white-space: normal;
}
</style>
