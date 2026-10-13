<template>
  <app-form-drawer
    ref="drawerRef"
    v-model="open"
    :title="accountId ? 'Edit Email Account' : 'Add Email Account'"
    save-label="Save"
    :saving="saving"
    draft-key="smtp-account-form"
    :draft="draftView"
    @submit="onSubmit"
    @cancel="resetForm"
    @restore-draft="restoreDraft"
  >
    <q-form ref="formRef" greedy>
      <!-- No tenant picker: the account belongs to the tenant in view, chosen in the header like every
           other tenant-scoped screen. -->
      <app-text-field
        v-model="form.accountName" label="Account Name *" class="q-mb-md"
        :error="!!accountNameError" :error-message="accountNameError"
        :rules="[(v) => !!v || 'Account name is required']"
        @update:model-value="accountNameError = ''"
      />

      <div class="row q-col-gutter-md q-mb-md">
        <div class="col-12 col-sm-8">
          <app-text-field
            v-model="form.host" label="Host *"
            :rules="[(v) => !!v || 'Host is required']"
          />
        </div>
        <div class="col-12 col-sm-4">
          <app-text-field
            v-model.number="form.port" label="Port *" type="number"
            placeholder="Common: 25, 465, 587, 2525"
            :rules="[
              (v) => (v !== null && v !== '' && v !== undefined) || 'Port is required',
              (v) => (Number(v) >= 1 && Number(v) <= 65535) || 'Port must be between 1 and 65535'
            ]"
          />
        </div>
      </div>

      <div class="row q-col-gutter-md q-mb-md">
        <div class="col-12 col-sm-6">
          <app-select v-model="form.encryptionType" :options="encryptionOptions" label="Encryption Type *" :clearable="false" />
        </div>
        <div class="col-12 col-sm-6">
          <app-select v-model="form.authType" :options="authOptions" label="Auth Type *" :clearable="false" />
        </div>
      </div>

      <app-text-field
        v-if="form.authType !== 'None'"
        v-model="form.username" label="Username" class="q-mb-md"
      />
      <app-password-field
        v-if="form.authType !== 'None'"
        v-model="form.password" label="Password" class="q-mb-md"
        :hint="accountId ? 'Leave blank to keep existing password' : ''"
      />

      <app-text-field
        v-model="form.fromName" label="From Name *" class="q-mb-md"
        :rules="[(v) => !!v || 'From name is required']"
      />
      <app-text-field
        v-model="form.fromEmail" label="From Email *" type="email" class="q-mb-md"
        :rules="[
          (v) => !!v || 'From email is required',
          (v) => /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(v) || 'Enter a valid email address'
        ]"
      />
    </q-form>
  </app-form-drawer>
</template>

<script setup>
import { ref, reactive, computed, watch } from "vue";
import { smtpAccountApi, getApiErrorMessage, getApiErrorCode, ApiErrorCodes } from "services/api";
import { useTenantOptions } from "composables/useTenantOptions";
import { useNotify } from "composables/useNotify";
import { useSmtpOptions } from "composables/useSmtpOptions";

import AppFormDrawer from "components/common/AppFormDrawer.vue";
import AppSelect from "components/common/AppSelect.vue";
import AppTextField from "components/common/AppTextField.vue";
import AppPasswordField from "components/common/AppPasswordField.vue";

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  // The tenant in view (the header's scope control). A new account is created for it, and an existing one
  // is read and written under it; with none chosen the server scopes to the caller's own tenant.
  tenantId: { type: String, default: null },
  // When set, the drawer edits the existing account; otherwise it creates a new one.
  accountId: { type: String, default: null }
});
const emit = defineEmits(["update:modelValue", "saved"]);

const notify = useNotify();
const { canChooseTenant } = useTenantOptions();
const { encryptionOptions, authOptions } = useSmtpOptions();

const open = computed({
  get: () => props.modelValue,
  set: (val) => emit("update:modelValue", val)
});

// Creation defaults: auto-negotiated encryption + Login auth; the port is left blank so its
// "common ports" placeholder is visible.
const blankForm = () => ({
  accountName: "",
  host: "",
  port: null,
  encryptionType: "Auto",
  authType: "Login",
  username: "",
  password: "",
  fromName: "",
  fromEmail: ""
});
const form = reactive(blankForm());
const formRef = ref(null);
const drawerRef = ref(null);
const saving = ref(false);
const accountNameError = ref("");

const resetForm = () => {
  Object.assign(form, blankForm());
  accountNameError.value = "";
};

const restoreDraft = (saved) => {
  Object.assign(form, blankForm(), saved);
};

const loadAccount = async (id) => {
  try {
    const a = await smtpAccountApi.get(id, scopeTenantId());
    form.accountName = a.accountName;
    form.host = a.host;
    form.port = a.port;
    form.encryptionType = a.encryptionType;
    form.authType = a.authType;
    form.username = a.username || "";
    form.password = ""; // never pre-filled — password is write-only (AC-SMTP-007.3)
    form.fromName = a.fromName;
    form.fromEmail = a.fromEmail;
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

// Resolve the tenant to operate on (super admin's chosen tenant, else server auto-scopes).
const scopeTenantId = () => (canChooseTenant.value && props.tenantId ? props.tenantId : undefined);

// Prepare the form whenever the drawer opens.
watch(() => props.modelValue, async (isOpen) => {
  if (!isOpen) return;
  resetForm();
  if (props.accountId) {
    await loadAccount(props.accountId);
  }
}, { immediate: true });

// Draft snapshot never includes the password (write-only, not persisted to local storage).
const draftView = computed(() => {
  const { password, ...rest } = form;
  return rest;
});

const onSubmit = async ({ clearDraft } = {}) => {
  accountNameError.value = "";
  if (!(await formRef.value?.validate())) return;
  saving.value = true;
  try {
    const payload = {
      accountName: form.accountName,
      host: form.host,
      port: Number(form.port),
      encryptionType: form.encryptionType,
      authType: form.authType,
      username: form.authType === "None" ? null : (form.username || null),
      fromName: form.fromName,
      fromEmail: form.fromEmail
    };
    // Only send the password when one was entered; omitting it preserves the existing one on edit.
    if (form.authType !== "None" && form.password) {
      payload.password = form.password;
    }

    if (props.accountId) {
      await smtpAccountApi.update(props.accountId, payload, scopeTenantId());
      notify.success("Email account updated.");
    } else {
      // The tenant in view, where a Super Admin has chosen one; otherwise the server uses the caller's.
      if (scopeTenantId()) payload.tenantId = scopeTenantId();
      await smtpAccountApi.create(payload);
      notify.success("Email account created.");
    }
    clearDraft?.();
    resetForm();
    emit("saved");
  } catch (err) {
    if (getApiErrorCode(err) === ApiErrorCodes.DuplicateIdentifier) {
      accountNameError.value = "An account with this name already exists.";
    } else {
      notify.error(getApiErrorMessage(err));
    }
  } finally {
    saving.value = false;
  }
};

defineExpose({ form });
</script>
