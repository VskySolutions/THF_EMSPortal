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
            </div>
          </q-card-section>
        </q-card>
      </div>
    </div>
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
onMounted(() => { load(); window.addEventListener("tenant-switched", load); });
onUnmounted(() => window.removeEventListener("tenant-switched", load));

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
</script>
