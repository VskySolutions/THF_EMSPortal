<template>
  <q-page padding>
    <app-detail-header
      :items="[
        { label: 'Home', icon: 'o_home', to: '/' },
        { label: 'My Account', to: { name: 'account' } },
        { label: 'My Profile' }
      ]"
      :back-to="{ name: 'account' }"
    >
      <!-- Commented out along with the fields it counts. -->
    </app-detail-header>

    <div v-if="loading" class="row flex-center q-pa-xl"><q-spinner color="primary" size="40px" /></div>

    <template v-else>
      <!-- Profile image -->
      <q-card flat bordered class="profile-card q-mb-md">
        <q-card-section class="text-subtitle1 text-weight-medium">Profile picture</q-card-section>
        <q-separator />
        <q-card-section>
          <app-image-upload
            ref="imageUpload"
            v-model="previewUrl"
            :loading="uploading"
            file-name="profile.png"
            @crop="onCropUpload"
            @remove="onImageRemove"
          />
        </q-card-section>
      </q-card>

      <!-- Personal details -->
      <q-card flat bordered class="profile-card q-mb-md">
        <q-card-section class="text-subtitle1 text-weight-medium">Personal details</q-card-section>
        <q-separator />
        <q-card-section class="row q-col-gutter-md">
          <div class="col-12 section-subhead">Name</div>
          <app-text-field v-model="form.firstName" label="First Name" class="col-12 col-sm-6" :rules="nameRules('First name')" />
          <app-text-field v-model="form.lastName" label="Last Name" class="col-8 col-sm-4" :rules="nameRules('Last name')" />
          <!-- The generational particle on your name, after the surname where it is read. -->
          <app-name-suffix-field v-model="form.suffix" class="col-4 col-sm-2" />
        </q-card-section>
      </q-card>

      <!-- Address -->
      <q-card flat bordered class="profile-card q-mb-md">
        <q-card-section class="text-subtitle1 text-weight-medium">Address</q-card-section>
        <q-separator />
        <q-card-section>
          <!-- Not `extended`: the landmark / building / floor / unit boxes are off this page. -->
          <app-address-fields ref="addressRef" v-model="address" />
        </q-card-section>
      </q-card>

      <div class="row justify-end q-mb-lg">
        <q-btn unelevated no-caps color="primary" label="Save profile" :loading="saving" @click="save" />
      </div>
    </template>

    <!-- Tenant assignments -->
    <q-card flat bordered class="profile-card q-mb-md">
      <q-card-section class="text-subtitle1 text-weight-medium">Tenant assignments</q-card-section>
      <q-separator />
      <q-list separator>
        <q-item v-for="t in assignments" :key="t.tenantId">
          <q-item-section>
            <q-item-label>{{ t.name || t.identifier }}</q-item-label>
            <q-item-label caption>{{ t.identifier }}</q-item-label>
          </q-item-section>
          <q-item-section side>
            <div class="row q-gutter-xs justify-end">
              <q-badge v-for="r in (t.roleNames || [])" :key="r" color="primary" class="text-capitalize">{{ r }}</q-badge>
              <span v-if="!(t.roleNames || []).length" class="text-caption text-grey-6">No roles</span>
            </div>
          </q-item-section>
        </q-item>
        <q-item v-if="!assignments.length"><q-item-section class="text-grey-6">No assignments.</q-item-section></q-item>
      </q-list>
    </q-card>

    <!-- Effective permissions (read-only source view) -->
    <q-card flat bordered class="profile-card q-mb-md">
      <q-card-section class="text-subtitle1 text-weight-medium">
        Effective permissions
        <q-badge v-if="effective" color="primary" class="q-ml-sm">{{ effective.effectivePermissions.length }}</q-badge>
        <div class="text-caption text-grey-6">Your permissions in the active tenant, and where each comes from (Role → Permission Group → key). Read-only.</div>
      </q-card-section>
      <q-separator />
      <q-card-section>
        <div v-if="!effective || !effective.roles.length" class="text-grey-6">
          You have no assigned role or effective permissions in the active tenant.
        </div>
        <div v-for="role in (effective?.roles || [])" :key="role.roleId || role.roleName" class="q-mb-md">
          <div class="row items-center q-gutter-xs">
            <q-icon name="o_admin_panel_settings" color="grey-7" />
            <span class="text-weight-medium">{{ role.roleName }}</span>
          </div>
          <div v-if="role.directPermissions.length" class="q-ml-md q-mt-xs">
            <div class="section-subhead q-mb-xs">Direct on role</div>
            <div class="row q-gutter-xs">
              <q-badge v-for="k in role.directPermissions" :key="k" color="teal-1" text-color="primary" class="pg-key">
                {{ humanizeKey(k) }}<q-tooltip>{{ k }}</q-tooltip>
              </q-badge>
            </div>
          </div>
          <div v-for="g in role.permissionGroups" :key="g.groupId" class="q-ml-md q-mt-xs">
            <div class="section-subhead q-mb-xs">Via permission group · {{ g.groupName }}</div>
            <div class="row q-gutter-xs">
              <q-badge v-for="k in g.permissionKeys" :key="k" color="teal-1" text-color="primary" class="pg-key">
                {{ humanizeKey(k) }}<q-tooltip>{{ k }}</q-tooltip>
              </q-badge>
            </div>
          </div>
          <div v-if="!role.directPermissions.length && !role.permissionGroups.length" class="q-ml-md text-caption text-grey-6">
            No permission keys.
          </div>
        </div>
      </q-card-section>
    </q-card>

    <!-- Password change. -->
    <q-card flat bordered class="profile-card">
      <q-card-section class="text-subtitle1 text-weight-medium">Change password</q-card-section>
      <q-separator />
      <change-password-form submit-label="Update password" />
    </q-card>

    <!-- REMS delegation is self-service — the principal names their own delegates — so it belongs on
         their own profile rather than in an admin screen. -->
    <rems-delegates-panel v-if="canUseRems" class="q-mt-md" />

    <app-record-audit :audit="profile?.audit" class="q-mt-md" />
  </q-page>
</template>

<script setup>
import { ref, reactive, computed, onMounted } from "vue";
import { authApi, profileApi, mediaApi, getApiErrorMessage } from "services/api";
import { humanizeKey } from "composables/usePermissionCategories";
import { useAuthStore } from "stores/auth";
import { useNotify } from "composables/useNotify";
import { usePermissions, Permissions } from "composables/usePermissions";
import { nameRules } from "utils/personName";
import AppDetailHeader from "components/common/AppDetailHeader.vue";
import AppRecordAudit from "components/common/AppRecordAudit.vue";
import RemsDelegatesPanel from "modules/rems/components/RemsDelegatesPanel.vue";
import AppTextField from "components/common/AppTextField.vue";
import AppNameSuffixField from "components/common/AppNameSuffixField.vue";
import AppAddressFields from "components/common/AppAddressFields.vue";
import AppImageUpload from "components/common/AppImageUpload.vue";
import ChangePasswordForm from "components/account/ChangePasswordForm.vue";

const authStore = useAuthStore();
const notify = useNotify();
const { has } = usePermissions();

// Anyone who can raise a REMS request has work worth delegating; anyone who cannot, does not.
const canUseRems = computed(() => has(Permissions.RemsRequestsCreate));

const assignments = computed(() => authStore.user?.tenants || []);

const addressRef = ref(null);

// ---- Form state ----
// The form carries MORE than the page shows.
const loading = ref(true);
const saving = ref(false);
const profile = ref(null);
const form = reactive({
  suffix: "",
  firstName: "",
  middleName: "",
  lastName: "",
  preferredName: "",
  displayName: "",
  gender: null,
  dateOfBirth: "",
  maritalStatus: null,
  nationality: null,
  primaryEmail: "",
  secondaryEmail: "",
  mobileNumber: "",
  phoneCountryCode: null,
  alternateMobileNumber: "",
  emergencyContactName: "",
  emergencyContactRelationship: "",
  emergencyContactNumber: "",
  profileMediaId: null
});
const address = reactive({
  countryCode: null,
  countryName: null,
  stateCode: null,
  stateName: null,
  cityName: null,
  postalCode: "",
  addressLine1: "",
  addressLine2: "",
  landmark: "",
  buildingName: "",
  floorNumber: "",
  unitNumber: ""
});

const load = async () => {
  loading.value = true;
  try {
    const p = await profileApi.getMine();
    profile.value = p;
    form.suffix = p.suffix || "";
    form.firstName = p.firstName || "";
    form.middleName = p.middleName || "";
    form.lastName = p.lastName || "";
    form.preferredName = p.preferredName || "";
    form.displayName = p.displayName || "";
    form.gender = p.gender || null;
    form.dateOfBirth = p.dateOfBirth ? p.dateOfBirth.substring(0, 10) : "";
    form.maritalStatus = p.maritalStatus || null;
    form.nationality = p.nationality || null;
    form.primaryEmail = p.primaryEmail || "";
    form.secondaryEmail = p.secondaryEmail || "";
    form.mobileNumber = p.mobileNumber || "";
    form.phoneCountryCode = p.countryCode || null;
    form.alternateMobileNumber = p.alternateMobileNumber || "";
    form.emergencyContactName = p.emergencyContactName || "";
    form.emergencyContactRelationship = p.emergencyContactRelationship || "";
    form.emergencyContactNumber = p.emergencyContactNumber || "";
    form.profileMediaId = p.profileMediaId || null;
    if (p.profileMediaUrl) previewUrl.value = mediaApi.absoluteUrl(p.profileMediaUrl);
    if (p.address) {
      address.countryCode = p.address.countryCode || null;
      address.countryName = p.address.countryName || null;
      address.stateCode = p.address.stateCode || null;
      address.stateName = p.address.stateName || null;
      address.cityName = p.address.cityName || null;
      address.postalCode = p.address.postalCode || "";
      address.addressLine1 = p.address.addressLine1 || "";
      address.addressLine2 = p.address.addressLine2 || "";
      address.landmark = p.address.landmark || "";
      address.buildingName = p.address.buildingName || "";
      address.floorNumber = p.address.floorNumber || "";
      address.unitNumber = p.address.unitNumber || "";
      // AppAddressFields reloads its own state/city option lists from the country/state codes.
    }
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loading.value = false;
  }
};

// ---- Effective permissions (read-only source view, WO-120) ----
const effective = ref(null);
const loadEffectivePermissions = async () => {
  try {
    effective.value = await authApi.effectivePermissions();
  } catch {
    /* non-fatal; the card shows an empty state */
  }
};

// ---- Profile image ----
// AppImageUpload handles picking + cropping; we upload the cropped file and reflect the new URL.
const imageUpload = ref(null);
const previewUrl = ref(null);
const uploading = ref(false);

const onCropUpload = async (file) => {
  uploading.value = true;
  try {
    // Filed under the person it pictures, so the server's copy is findable without a database lookup.
    const media = await mediaApi.upload(
      file, "Profile", profile.value?.id ? { type: "Person", id: profile.value.id } : null);
    form.profileMediaId = media.id;
    previewUrl.value = mediaApi.absoluteUrl(media.publicUrl);
    imageUpload.value?.closeCrop();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    uploading.value = false;
  }
};

const onImageRemove = () => {
  // v-model already cleared previewUrl; also drop the saved media id.
  form.profileMediaId = null;
};

// ---- Save ----
const save = async () => {
  if (!addressRef.value?.validate()) {
    notify.error("Please fix the highlighted fields.");
    return;
  }

  // AppPhoneInput already normalises the mobile to E.164 and tracks its dial code.
  const mobile = form.mobileNumber;
  const dialCode = form.phoneCountryCode || null;

  // AppAddressFields keeps countryName/stateName in sync with the selected ISO codes.
  const countryName = address.countryName;
  const stateName = address.stateName;

  const payload = {
    suffix: form.suffix,
    firstName: form.firstName,
    middleName: form.middleName,
    lastName: form.lastName,
    preferredName: form.preferredName,
    displayName: form.displayName,
    gender: form.gender,
    dateOfBirth: form.dateOfBirth || null,
    maritalStatus: form.maritalStatus,
    nationality: form.nationality,
    primaryEmail: form.primaryEmail,
    secondaryEmail: form.secondaryEmail,
    mobileNumber: mobile,
    countryCode: dialCode,
    alternateMobileNumber: form.alternateMobileNumber,
    emergencyContactName: form.emergencyContactName,
    emergencyContactRelationship: form.emergencyContactRelationship,
    emergencyContactNumber: form.emergencyContactNumber,
    profileMediaId: form.profileMediaId,
    removeProfileMedia: !form.profileMediaId,
    address: {
      addressType: "Home",
      countryCode: address.countryCode,
      countryName,
      stateCode: address.stateCode,
      stateName,
      cityName: address.cityName,
      postalCode: address.postalCode,
      addressLine1: address.addressLine1,
      addressLine2: address.addressLine2,
      landmark: address.landmark,
      buildingName: address.buildingName,
      floorNumber: address.floorNumber,
      unitNumber: address.unitNumber
    }
  };

  saving.value = true;
  try {
    const updated = await profileApi.updateMine(payload);
    profile.value = updated;
    // Keep the auth/header display name in sync with the profile.
    if (form.displayName && form.displayName !== authStore.user?.displayName) {
      try {
        await authApi.updateMe(form.displayName);
        authStore.setUserInfo({ displayName: form.displayName });
      } catch { /* non-fatal */ }
    }
    notify.success("Profile saved.");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    saving.value = false;
  }
};

onMounted(() => {
  load();
  loadEffectivePermissions();
});
</script>

<style scoped>
.profile-card {
  border-radius: 12px;
}
.section-subhead {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--q-primary);
  margin-top: 4px;
}
.pg-key {
  font-size: 12px;
  padding: 4px 8px;
}
</style>
