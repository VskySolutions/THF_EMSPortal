<template>
  <q-page padding>
    <app-detail-header
      :items="[
        { label: 'Home', icon: 'o_home', to: '/' },
        { label: 'Person', to: { name: 'persons' } },
        { label: form.displayName || fullName || 'Person' }
      ]"
      :back-to="{ name: 'persons' }"
    >
      <template #actions>
        <q-chip v-if="personCode" dense color="blue-grey-1" text-color="blue-grey-8">{{ personCode }}</q-chip>
        <q-badge :color="isUser ? 'primary' : 'grey-4'" :text-color="isUser ? 'white' : 'grey-8'">
          {{ isUser ? "Linked user account" : "Not a user" }}
        </q-badge>
      </template>
    </app-detail-header>

    <div v-if="loading" class="row flex-center q-pa-xl"><q-spinner color="primary" size="40px" /></div>

    <div v-else>
      <q-banner v-if="!isUser && canCreateUser" dense class="bg-teal-1 text-blue-9 q-mb-md" rounded>
        <template #avatar><q-icon name="o_person_add" color="primary" /></template>
        This person has no login account yet.
        <template #action>
          <q-btn flat no-caps color="primary" label="Convert to User" @click="convertToUser" />
        </template>
      </q-banner>

      <q-form ref="formRef" greedy>
        <!-- Personal -->
        <q-card flat bordered class="person-card q-mb-md">
          <q-card-section class="text-subtitle1 text-weight-medium">Personal details</q-card-section>
          <q-separator />
          <q-card-section class="row q-col-gutter-md">
            <app-select
              v-if="canChooseTenant" v-model="form.tenantId" :options="tenantOptions" label="Tenant"
              class="col-12" :loading="loadingTenants" :disable="!canWrite"
            />
            <app-text-field v-model="form.firstName" label="First Name" class="col-12 col-sm-6" :disable="!canWrite" :rules="nameRules('First name', { required: true })" />
            <app-text-field v-model="form.middleName" label="Middle Name" class="col-12 col-sm-6" :disable="!canWrite" :rules="nameRules('Middle name')" />
            <app-text-field v-model="form.lastName" label="Last Name" class="col-6 col-sm-4" :disable="!canWrite" :rules="nameRules('Last name', { required: true })" />
            <!-- The generational particle on the name, after the surname where it is read. -->
            <app-name-suffix-field v-model="form.suffix" class="col-6 col-sm-2" :disable="!canWrite" />
            <app-text-field v-model="form.preferredName" label="Preferred Name" class="col-12 col-sm-6" :disable="!canWrite" :rules="nameRules('Preferred name')" />
            <app-text-field v-model="form.displayName" label="Display Name" class="col-12 col-sm-6" :disable="!canWrite" />
            <app-select v-model="form.gender" :options="genderOptions" label="Gender" class="col-12 col-sm-6" />
            <app-date-field v-model="form.dateOfBirth" label="Date of Birth" class="col-12 col-sm-6" :disable="!canWrite" />
          </q-card-section>
        </q-card>

        <!-- Contact -->
        <q-card flat bordered class="person-card q-mb-md">
          <q-card-section class="text-subtitle1 text-weight-medium">Contact details</q-card-section>
          <q-separator />
          <q-card-section class="row q-col-gutter-md">
            <app-text-field v-model="form.primaryEmail" type="email" label="Primary Email" class="col-12 col-sm-6" :disable="!canWrite" :rules="emailRules" />
            <app-text-field v-model="form.secondaryEmail" type="email" label="Alternate Email" class="col-12 col-sm-6" :disable="!canWrite" />
            <app-phone-input v-model="form.mobileNumber" v-model:country="form.countryCode" label="Phone Number" class="col-12 col-sm-6" :disable="!canWrite" />
            <app-text-field v-model="form.alternateMobileNumber" label="Alternate Phone Number" class="col-12 col-sm-6" :disable="!canWrite" />
          </q-card-section>
        </q-card>

        <!-- Professional -->
        <q-card flat bordered class="person-card q-mb-md">
          <q-card-section class="text-subtitle1 text-weight-medium">Professional</q-card-section>
          <q-separator />
          <q-card-section class="row q-col-gutter-md">
            <app-text-field v-model="form.employeeCode" label="Employee Code" class="col-12 col-sm-6" :disable="!canWrite" />
          </q-card-section>
        </q-card>

        <div v-if="canWrite" class="row justify-end q-mb-md">
          <q-btn unelevated no-caps color="primary" label="Save" :loading="saving" @click="save" />
        </div>
      </q-form>

      <app-record-audit :audit="audit" />
    </div>
  </q-page>
</template>

<script setup>
import { ref, reactive, computed, onMounted } from "vue";
import { useRoute, useRouter } from "vue-router";
import { personApi, getApiErrorMessage } from "services/api";
import { usePermissions, Permissions } from "composables/usePermissions";
import { useNotify } from "composables/useNotify";
import { useTenantOptions } from "composables/useTenantOptions";
import { nameRules } from "utils/personName";
import AppDetailHeader from "components/common/AppDetailHeader.vue";
import AppRecordAudit from "components/common/AppRecordAudit.vue";
import AppSelect from "components/common/AppSelect.vue";
import AppTextField from "components/common/AppTextField.vue";
import AppNameSuffixField from "components/common/AppNameSuffixField.vue";
import AppDateField from "components/common/AppDateField.vue";
import AppPhoneInput from "components/common/AppPhoneInput.vue";

const route = useRoute();
const router = useRouter();
const notify = useNotify();
const { has } = usePermissions();
const { canChooseTenant, tenantOptions, loadingTenants, loadTenants } = useTenantOptions();
const canWrite = computed(() => has(Permissions.PersonsWrite));
const canCreateUser = computed(() => has(Permissions.UsersWrite));

const personId = route.params.id;
const loading = ref(true);
const saving = ref(false);
const formRef = ref(null);
const isUser = ref(false);
const personCode = ref("");
// Who made this record and who last touched it, for the block at the foot of the page.
const audit = ref(null);

const genderOptions = ["Male", "Female", "Other", "Prefer not to say"].map((g) => ({ label: g, value: g }));
const emailRules = [(v) => !v || /.+@.+\..+/.test(v) || "Enter a valid email"];

const form = reactive({
  tenantId: null,
  suffix: "",
  firstName: "",
  middleName: "",
  lastName: "",
  preferredName: "",
  displayName: "",
  gender: null,
  dateOfBirth: "",
  primaryEmail: "",
  secondaryEmail: "",
  mobileNumber: "",
  countryCode: null,
  alternateMobileNumber: "",
  employeeCode: ""
});

const fullName = computed(() =>
  [form.firstName, form.middleName, form.lastName].filter(Boolean).join(" ") || form.displayName);

const load = async () => {
  loading.value = true;
  try {
    const detail = await personApi.get(personId);
    const p = detail.profile;
    isUser.value = detail.isUser;
    personCode.value = p.personCode || "";
    audit.value = p.audit || null;
    form.tenantId = p.tenantId || null;
    form.suffix = p.suffix || "";
    form.firstName = p.firstName || "";
    form.middleName = p.middleName || "";
    form.lastName = p.lastName || "";
    form.preferredName = p.preferredName || "";
    form.displayName = p.displayName || "";
    form.gender = p.gender || null;
    form.dateOfBirth = p.dateOfBirth ? p.dateOfBirth.substring(0, 10) : "";
    form.primaryEmail = p.primaryEmail || "";
    form.secondaryEmail = p.secondaryEmail || "";
    form.mobileNumber = p.mobileNumber || "";
    form.countryCode = p.countryCode || null;
    form.alternateMobileNumber = p.alternateMobileNumber || "";
    form.employeeCode = p.employeeCode || "";
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loading.value = false;
  }
};

const save = async () => {
  if (!(await formRef.value?.validate())) return;
  saving.value = true;
  try {
    await personApi.update(personId, { ...form, dateOfBirth: form.dateOfBirth || null });
    notify.success("Person updated.");
    load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    saving.value = false;
  }
};

const convertToUser = () => router.push({ name: "users", query: { personId } });

onMounted(async () => {
  if (canChooseTenant.value) await loadTenants();
  await load();
});
</script>

<style scoped>
.person-card {
  border-radius: 12px;
}
</style>
