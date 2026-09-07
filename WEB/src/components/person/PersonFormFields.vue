<template>
  <div>
    <!-- Tenant: only platform/super admins get the dropdown; for others it is auto-set and hidden. -->
    <template v-if="tenantOptions.length">
      <div class="section-subhead">Tenant</div>
      <div class="row q-col-gutter-md q-mb-md">
        <app-select
          v-model="form.tenantId" :options="tenantOptions" label="Tenant" class="col-12 col-sm-6"
          :loading="loadingTenants" :disable="disable"
        />
      </div>
    </template>

    <div class="section-subhead">Name</div>
    <div class="row q-col-gutter-md q-mb-md">
      <!-- Held to what a name is — letters, and the hyphen / apostrophe / period that appear inside real
           ones. -->
      <app-text-field
        v-model="form.firstName" label="First Name *" class="col-12 col-sm-6"
        :disable="disable" :rules="nameRules('First name', { required: true })"
      />
      <app-text-field
        v-model="form.middleName" label="Middle Name" class="col-12 col-sm-6"
        :disable="disable" :rules="nameRules('Middle name')"
      />
      <app-text-field
        v-model="form.lastName" label="Last Name *" class="col-8 col-sm-4"
        :disable="disable" :rules="nameRules('Last name', { required: true })"
      />
      <!-- The generational particle on their name — Jr., Sr., III. It is not part of the name the person
           is filed under. -->
      <app-name-suffix-field v-model="form.suffix" class="col-4 col-sm-2" :disable="disable" />
      <app-text-field
        v-model="form.preferredName" label="Preferred Name" class="col-12 col-sm-6"
        :disable="disable" :rules="nameRules('Preferred name')"
      />
    </div>

    <div class="section-subhead">Demographics</div>
    <div class="row q-col-gutter-md q-mb-md">
      <app-select v-model="form.gender" :options="genderOptions" label="Gender" class="col-12 col-sm-6" />
      <app-date-field v-model="form.dateOfBirth" label="Date of Birth" class="col-12 col-sm-6" :disable="disable" />
    </div>

    <div class="section-subhead">Contact</div>
    <div class="row q-col-gutter-md q-mb-md">
      <app-text-field
        v-model="form.primaryEmail" type="email" label="Primary Email *" class="col-12"
        :disable="disable" :rules="emailRules"
      />
      <app-phone-input
        v-model="form.mobileNumber" v-model:country="form.countryCode"
        label="Phone Number" class="col-12" :disable="disable"
      />
    </div>
  </div>
</template>

<script setup>
// The Person create/edit field set, defined once and reused (People list drawer + the quick-add dialog on
// the user form).
import { nameRules } from "utils/personName";
import AppSelect from "components/common/AppSelect.vue";
import AppTextField from "components/common/AppTextField.vue";
import AppNameSuffixField from "components/common/AppNameSuffixField.vue";
import AppDateField from "components/common/AppDateField.vue";
import AppPhoneInput from "components/common/AppPhoneInput.vue";

// The form object is shared via v-model; nested fields write back to the caller's reactive object.
const form = defineModel({ type: Object, required: true });

defineProps({
  disable: { type: Boolean, default: false },
  // Tenant dropdown options. Empty (the default) hides the dropdown — the parent auto-sets the tenant.
  tenantOptions: { type: Array, default: () => [] },
  loadingTenants: { type: Boolean, default: false }
});

const genderOptions = ["Male", "Female", "Other", "Prefer not to say"].map((g) => ({ label: g, value: g }));
const emailRules = [
  (v) => !!v || "Primary email is required",
  (v) => /.+@.+\..+/.test(v) || "Enter a valid email"
];
</script>

<style scoped>
.section-subhead {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--q-primary);
  margin: 4px 0 8px;
}
</style>
