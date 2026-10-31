<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'Tenant Settings' }, { label: 'Maconomy' }]"
      show-back
      @back="$router.back()"
    />

    <!-- Three tabs: the credentials, then what they power. Customer and Job open only once a connection is
         saved and enabled, since everything on them signs in with it; why they are greyed is said once, at
         the end of the strip, as on the request form. -->
    <q-card flat bordered class="q-mb-md">
      <div class="ms-tabbar">
        <q-tabs
          v-model="tab" dense no-caps align="left" active-color="primary" indicator-color="primary"
          class="text-grey-8 col"
        >
          <q-tab name="authentication" icon="o_key" label="Authentication" />
          <q-tab name="customer" icon="o_person_search" label="Customer" :disable="locked" />
          <q-tab name="job" icon="o_work" label="Job" :disable="locked" />
        </q-tabs>
        <div v-if="locked" class="ms-tabs__end">
          <q-icon name="o_info" size="20px" color="primary" class="cursor-pointer">
            <q-tooltip anchor="bottom right" self="top right" max-width="320px" :delay="200">{{ lockedReason }}</q-tooltip>
          </q-icon>
        </div>
      </div>
    </q-card>

    <!-- ==================== Authentication ==================== -->
    <!-- The connection: where Maconomy is and who the portal signs in as. One per tenant. -->
    <div v-show="tab === 'authentication'" class="row q-col-gutter-md">
      <div class="col-12 col-lg-8">
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
                  info="Kept for a later use; nothing reads it yet. The customer lookup and the job sync name their own containers."
                />
                <app-text-field
                  v-model.number="form.defaultLimit" label="Default Limit *" type="number" class="col-12 col-sm-6"
                  :rules="[limitRule]" info="Rows a customer search returns when the caller does not say (1 to 100)."
                />
              </div>
              <q-toggle v-model="form.isEnabled" label="Connection enabled">
                <q-tooltip max-width="320px">
                  Off keeps the credentials but stops the customer lookup, the customer sync and the job sync;
                  the Customer and Job tabs close until it is on again.
                </q-tooltip>
              </q-toggle>
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
    </div>

    <!-- ==================== Customer ==================== -->
    <div v-show="tab === 'customer'">
      <q-banner v-if="exists && !token.present" dense class="bg-amber-1 text-grey-9 rounded-borders q-mb-md">
        <template #avatar><q-icon name="o_info" color="amber-8" /></template>
        No token held yet. Test the connection on the Authentication tab, or run a search here: both sign in.
      </q-banner>

      <div class="row q-col-gutter-md">
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
           Always the caller's OWN tenant: the scope control moves the connection, not this. -->
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

          <app-data-table
            page-key="maconomy-customer-sync" row-key="remsId" title="Waiting for a hand"
            :rows="unsynced" :columns="unsyncedColumns" :loading="syncLoading"
            default-sort-by="remsNumber" client-sort @refresh="loadSync"
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
                <q-btn flat round dense color="primary" icon="o_visibility" :to="{ name: 'rems_request', params: { id: cell.row.remsId } }">
                  <q-tooltip>Open request</q-tooltip>
                </q-btn>
                <q-btn
                  type="a" flat round dense color="primary" icon="o_cloud_upload"
                  :loading="syncingRems === cell.row.remsId" :disable="!sync.enabled || !!syncingRems" @click="syncNow(cell.row)"
                >
                  <q-tooltip>{{ sync.enabled ? "Sync now — post this client to Maconomy" : "Start the customer sync first." }}</q-tooltip>
                </q-btn>
              </q-td>
            </template>
            <template #no-data>
              <div class="full-width column flex-center q-pa-lg text-grey-6">
                <q-icon name="o_cloud_done" size="32px" class="q-mb-sm" />
                Nothing is waiting: every approved new client is in Maconomy.
              </div>
            </template>
          </app-data-table>
        </q-card-section>
        <q-separator />
        <q-card-actions class="q-gutter-sm bg-grey-1">
          <q-space />
          <q-btn
            v-if="sync.enabled" outline no-caps color="negative" icon="o_pause" label="Stop"
            :loading="syncToggling" :disable="busy" @click="setSync(false)"
          />
          <q-btn
            v-else unelevated no-caps color="primary" icon="o_play_arrow" label="Start"
            :loading="syncToggling" :disable="busy || !configured" @click="setSync(true)"
          >
            <q-tooltip v-if="!configured">{{ lockedReason }}</q-tooltip>
          </q-btn>
        </q-card-actions>
      </q-card>
    </div>

    <!-- ==================== Job ==================== -->
    <!-- The job sync: an approved engagement posted to Maconomy as a job, by hand, from a list's Sync job
         now. Nothing posts a job by itself. Always the caller's OWN tenant, like the customer sync. The
         project manager and the default template job are server configuration; the template job numbers
         sit on the REMS Job Template values under Option Sets — so there is nothing to type here. -->
    <div v-show="tab === 'job'">
      <q-banner v-if="exists && !token.present" dense class="bg-amber-1 text-grey-9 rounded-borders q-mb-md">
        <template #avatar><q-icon name="o_info" color="amber-8" /></template>
        No token held yet. Test the connection on the Authentication tab first.
      </q-banner>

      <q-card flat bordered>
        <q-card-section class="q-py-sm text-subtitle2 text-primary row items-center">
          <q-icon name="o_work" size="18px" class="q-mr-xs" />Job sync
          <span v-if="canChooseTenant" class="text-caption text-grey-7 q-ml-sm">— your own tenant</span>
          <q-space />
          <q-badge :color="job.enabled ? 'positive' : 'grey-6'">{{ job.enabled ? "On" : "Off" }}</q-badge>
        </q-card-section>
        <q-separator />
        <q-card-section>
          <p class="text-body2 text-grey-8 q-mb-xs">
            When on, Sync job now is offered on approved requests — in the Approvals list, EMS Review and the
            table below — and creates a job in Maconomy from the template job for the request's Job Template,
            under the client's customer, with the job number left to Maconomy's series and written back onto
            the request. Nothing is posted by itself. While off, the action is hidden everywhere.
          </p>
          <div v-if="job.changedOnUtc" class="text-caption text-grey-7 q-mb-md">
            {{ job.enabled ? "Started" : "Stopped" }} {{ fmt.formatDateTime(job.changedOnUtc) }}{{ job.changedByName ? ` by ${job.changedByName}` : "" }}.
          </div>

          <!-- What every job is created with, and where each value is kept. -->
          <div class="ms-facts text-body2 text-grey-8 q-mb-md">
            <div>
              <q-icon name="o_badge" size="16px" class="q-mr-xs" />
              Project manager:
              <template v-if="job.projectManagerNumber">employee <b>{{ job.projectManagerNumber }}</b></template>
              <span v-else class="text-negative">not configured — every sync is refused until it is</span>
              <span class="text-caption text-grey-7">(server configuration, Maconomy:JobCard)</span>
            </div>
            <div v-if="job.defaultTemplateNumber">
              <q-icon name="o_description" size="16px" class="q-mr-xs" />
              Default template job: <b>{{ job.defaultTemplateNumber }}</b>, used when a request's Job Template carries
              no Maconomy number <span class="text-caption text-grey-7">(server configuration)</span>
            </div>
            <div>
              <q-icon name="o_account_tree" size="16px" class="q-mr-xs" />
              {{ numberedTemplates }} of {{ job.templates.length }} job templates carry a Maconomy number
              <template v-if="numberedTemplates < job.templates.length">
                — set the rest under
                <router-link :to="{ name: 'option_sets' }" class="text-primary">Option Sets → REMS Job Template</router-link>
              </template>
            </div>
          </div>

          <div class="row q-col-gutter-md q-mb-md">
            <div v-for="tile in jobTiles" :key="tile.label" class="col-6 col-sm-3">
              <div class="text-h6">{{ tile.value }}</div>
              <div class="text-caption text-grey-7">{{ tile.label }}</div>
            </div>
          </div>

          <app-data-table
            page-key="maconomy-job-sync" row-key="remsId" title="Approved requests"
            :rows="jobRows" :columns="jobColumns" :loading="jobLoading"
            default-sort-by="remsNumber" client-sort @refresh="loadJob"
          >
            <template #body-cell-remsNumber="cell">
              <q-td :props="cell">
                <router-link :to="{ name: 'rems_request', params: { id: cell.row.remsId } }" class="text-primary">
                  {{ cell.row.remsNumber }}
                </router-link>
              </q-td>
            </template>
            <template #body-cell-customerNumber="cell">
              <q-td :props="cell">{{ cell.row.customerNumber || "—" }}</q-td>
            </template>
            <template #body-cell-sync="cell">
              <q-td :props="cell"><maconomy-sync-chip kind="job" :sync="cell.row.sync" /></q-td>
            </template>
            <template #body-cell-syncedOn="cell">
              <q-td :props="cell">
                <template v-if="cell.row.sync?.syncedOnUtc">
                  {{ fmt.formatDateTime(cell.row.sync.syncedOnUtc) }}{{ cell.row.sync.syncedByName ? ` by ${cell.row.sync.syncedByName}` : "" }}
                </template>
                <template v-else>—</template>
              </q-td>
            </template>
            <template #body-cell-reason="cell">
              <q-td :props="cell" class="ms-reason">{{ jobReason(cell.row) || "—" }}</q-td>
            </template>
            <template #body-cell-actions="cell">
              <q-td :props="cell">
                <q-btn flat round dense color="primary" icon="o_visibility" :to="{ name: 'rems_request', params: { id: cell.row.remsId } }">
                  <q-tooltip>Open request</q-tooltip>
                </q-btn>
                <!-- Hidden while the sync is off (so is the whole action, everywhere); disabled with the
                     reason while the request is not yet eligible. -->
                <q-btn
                  v-if="cell.row.sync?.canSyncNow || cell.row.sync?.blockedReason" type="a"
                  flat round dense color="primary" icon="o_work"
                  :loading="syncingJobRems === cell.row.remsId" :disable="!cell.row.sync?.canSyncNow || !!syncingJobRems"
                  @click="syncJobNow(cell.row)"
                >
                  <q-tooltip max-width="320px">
                    {{ cell.row.sync?.canSyncNow ? "Sync job now — create this engagement's job in Maconomy" : cell.row.sync?.blockedReason }}
                  </q-tooltip>
                </q-btn>
              </q-td>
            </template>
            <template #no-data>
              <div class="full-width column flex-center q-pa-lg text-grey-6">
                <q-icon name="o_work" size="32px" class="q-mb-sm" />
                No request has been approved yet.
              </div>
            </template>
          </app-data-table>
        </q-card-section>
        <q-separator />
        <q-card-actions class="q-gutter-sm bg-grey-1">
          <q-space />
          <q-btn
            v-if="job.enabled" outline no-caps color="negative" icon="o_pause" label="Stop"
            :loading="jobToggling" :disable="busy" @click="setJob(false)"
          />
          <q-btn
            v-else unelevated no-caps color="primary" icon="o_play_arrow" label="Start"
            :loading="jobToggling" :disable="busy || !configured" @click="setJob(true)"
          >
            <q-tooltip v-if="!configured">{{ lockedReason }}</q-tooltip>
          </q-btn>
        </q-card-actions>
      </q-card>
    </div>
  </q-page>
</template>

<script setup>
// The tenant's Maconomy connection — credentials and a test of them — and the two syncs it powers: the
// client as a customer, the engagement as a job.
import { ref, reactive, computed, watch, onMounted, onUnmounted } from "vue";
import { useRoute, useRouter } from "vue-router";
import { maconomyApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useDateFormat } from "composables/useDateFormat";
import { useTenantOptions } from "composables/useTenantOptions";
import { useTenantScope } from "composables/useTenantScope";
import { useMaconomyJobSync } from "modules/rems/useMaconomyJobSync";

import AppListHeader from "components/common/AppListHeader.vue";
import AppDataTable from "components/common/AppDataTable.vue";
import AppTextField from "components/common/AppTextField.vue";
import AppPasswordField from "components/common/AppPasswordField.vue";
import MaconomyCustomerSelect from "modules/maconomy/components/MaconomyCustomerSelect.vue";
import MaconomySyncChip from "modules/rems/components/MaconomySyncChip.vue";

const notify = useNotify();
const { confirm } = useConfirm();
const fmt = useDateFormat();
const { canChooseTenant } = useTenantOptions();
const route = useRoute();
const router = useRouter();

// The tenant in view comes from the toolbar's global scope control, as on the Email Accounts page.
const { selectedTenantId } = useTenantScope();
const scopeTenantId = () => (canChooseTenant.value && selectedTenantId.value ? selectedTenantId.value : undefined);

// ---- The tabs ----
// The open tab rides in the URL, so a link lands on the right one. Customer and Job lock until the
// connection is saved and enabled — judged only once the connection has been read, so a deep link to
// a locked tab is not thrown back to Authentication while the page is still loading.
const TABS = ["authentication", "customer", "job"];
const tab = ref(TABS.includes(route.query.tab) ? route.query.tab : "authentication");
const loaded = ref(false);
const lockedReason = "Save and enable the connection on the Authentication tab first.";

watch(tab, (t) => {
  const query = { ...route.query };
  if (t === "authentication") delete query.tab;
  else query.tab = t;
  router.replace({ query });
});

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
// Saved and switched on: what the Customer and Job tabs need.
const configured = computed(() => exists.value && !!connection.value?.isEnabled);
const locked = computed(() => loaded.value && !configured.value);
watch(locked, (isLocked) => {
  if (isLocked && tab.value !== "authentication") tab.value = "authentication";
});

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
  } finally {
    loaded.value = true;
  }
};

// The global scope switch announces itself; this page is not a list, so it listens for itself.
const loadAll = () => { load(); loadSync(); loadJob(); };
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
    message: "Remove the connection, its credentials and its token? The customer lookup and both syncs stop " +
      "working for this tenant until a new connection is saved.",
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
  { name: "remsNumber", label: "Request", field: "remsNumber", align: "left", sortable: true, default: true },
  { name: "clientName", label: "Client", field: "clientName", align: "left", sortable: true, default: true },
  { name: "status", label: "Status", field: "status", align: "left", sortable: true, default: true },
  { name: "failureReason", label: "Reason", field: "failureReason", align: "left", sortable: false, default: true },
  { name: "lastAttemptOnUtc", label: "Last attempt", field: "lastAttemptOnUtc", align: "left", sortable: true, default: true },
  { name: "actions", label: "Actions", field: "actions", align: "left" }
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

// ---- The job sync ----
const blankJob = () => ({
  connected: false,
  enabled: false,
  changedOnUtc: null,
  changedByName: null,
  projectManagerNumber: null,
  defaultTemplateNumber: null,
  synced: 0,
  failed: 0,
  syncing: 0,
  waiting: 0,
  templates: []
});
const job = ref(blankJob());
const jobRows = ref([]);
const jobLoading = ref(false);
const jobToggling = ref(false);

// How many REMS Job Template values carry a Maconomy number: the rest cannot be posted without the default.
const numberedTemplates = computed(() => (job.value.templates || []).filter((t) => t.templateJobNumber).length);

const jobTiles = computed(() => [
  { label: "Jobs created", value: job.value.synced },
  { label: "Failed", value: job.value.failed },
  { label: "Posting", value: job.value.syncing },
  { label: "Waiting", value: job.value.waiting }
]);

const jobColumns = [
  { name: "remsNumber", label: "Request", field: "remsNumber", align: "left", sortable: true, default: true },
  { name: "clientName", label: "Client", field: "clientName", align: "left", sortable: true, default: true },
  { name: "customerNumber", label: "Customer", field: "customerNumber", align: "left", sortable: true, default: true },
  { name: "serviceLine", label: "Service line", field: (r) => r.serviceLine || "—", align: "left", sortable: true, default: true },
  { name: "jobTemplate", label: "Job Template", field: (r) => r.jobTemplate || "—", align: "left", sortable: true, default: true },
  { name: "sync", label: "Job", field: (r) => r.sync?.jobNumber || r.sync?.status, align: "left", sortable: true, default: true },
  { name: "syncedOn", label: "Synced", field: (r) => r.sync?.syncedOnUtc, align: "left", sortable: true, default: true },
  { name: "reason", label: "Reason", field: (r) => r.sync?.failureReason || r.sync?.blockedReason, align: "left", sortable: false, default: true },
  { name: "actions", label: "Actions", field: "actions", align: "left" }
];

// Why a row is where it is: the last failure, or what still stands in the way of a press.
const jobReason = (row) => row.sync?.failureReason || (row.sync?.status !== "synced" ? row.sync?.blockedReason : null);

const loadJob = async () => {
  jobLoading.value = true;
  try {
    // The caller's OWN tenant, like the customer sync.
    const [overview, rows] = await Promise.all([
      maconomyApi.jobSync(),
      maconomyApi.jobSyncRequests()
    ]);
    job.value = overview || blankJob();
    jobRows.value = rows || [];
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    jobLoading.value = false;
  }
};

const setJob = async (enabled) => {
  jobToggling.value = true;
  try {
    job.value = await (enabled ? maconomyApi.startJobSync() : maconomyApi.stopJobSync());
    notify.success(enabled
      ? "Job sync started. Sync job now is offered on approved requests."
      : "Job sync stopped. Sync job now is hidden everywhere.");
    // Whether a row can be pressed turns on the switch, so the table is re-read.
    await loadJob();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    jobToggling.value = false;
  }
};

// Sync job now from this table: the same confirmation as on the lists, through the settings page's own endpoint.
const { syncingJobRems, syncJobNow } = useMaconomyJobSync({ run: (remsId) => maconomyApi.runJobSync(remsId), afterwards: loadJob });
</script>

<style scoped>
/* A Maconomy error message can be a sentence or three; the column wraps rather than widening the table. */
.ms-reason {
  max-width: 420px;
  white-space: normal;
}

/* The facts above the counts, one per line with a little air between them. */
.ms-facts > div + div {
  margin-top: 4px;
}

/* The tab strip, with the note on the greyed tabs parked at its end. */
.ms-tabbar {
  display: flex;
  align-items: center;
}

.ms-tabs__end {
  flex: 0 0 auto;
  padding: 6px 12px 6px 8px;
}
</style>
