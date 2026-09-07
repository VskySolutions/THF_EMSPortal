<template>
  <q-page padding>
    <app-detail-header :items="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'My Account' }]">
      <!-- The same percentage the profile page shows, commented out with it: it counts fields that page no
           longer asks for, so it reads the same on both screens and can be moved from neither. -->
      <!-- <template #actions> <q-chip v-if="profile" dense color="teal-1" text-color="primary"
           class="text-weight-medium"> {{ completion }}% complete </q-chip> </template> -->
    </app-detail-header>

    <!-- Profile summary -->
    <q-card flat bordered class="account-card q-mb-md">
      <q-card-section class="row items-center q-gutter-md">
        <q-avatar size="92px" color="primary" text-color="white">
          <img v-if="avatarUrl" :src="avatarUrl" alt="Profile">
          <span v-else class="text-h4">{{ initials }}</span>
        </q-avatar>

        <div class="col" style="min-width: 0;">
          <div class="row items-center q-gutter-sm">
            <div class="text-h6 text-weight-bold">{{ displayName }}</div>
            <q-badge v-if="profile?.isProfileVerified" color="positive" class="q-px-sm">
              <q-icon name="o_verified" size="14px" class="q-mr-xs" /> Verified
            </q-badge>
          </div>
          <div class="text-body2 text-grey-7">{{ email }}</div>
          <div v-if="roleChips.length" class="q-mt-sm row q-gutter-xs">
            <q-chip
              v-for="r in roleChips" :key="r" dense color="primary" text-color="white" class="text-capitalize"
            >
              {{ r }}
            </q-chip>
          </div>
        </div>

        <div class="column q-gutter-sm">
          <q-btn unelevated no-caps color="primary" icon="o_edit" label="Edit Profile" :to="{ name: 'profile' }" />
          <q-btn outline no-caps color="primary" icon="o_lock" label="Change Password" :to="{ name: 'change_password' }" />
        </div>
      </q-card-section>

      <!-- <q-separator /> <q-card-section class="q-py-sm row items-center q-gutter-md"> <div
           class="text-caption text-grey-7">Profile completion</div> <q-linear-progress :value="completion
           /. -->
    </q-card>

    <div class="row q-col-gutter-md">
      <!-- Contact -->
      <div class="col-12 col-md-6">
        <q-card flat bordered class="account-card full-height">
          <q-card-section class="row items-center q-gutter-sm">
            <q-icon name="o_contact_mail" color="primary" size="sm" />
            <div class="text-subtitle1 text-weight-medium">Contact</div>
          </q-card-section>
          <q-separator />
          <q-list>
            <q-item v-for="row in contactRows" :key="row.label">
              <q-item-section avatar><q-icon :name="row.icon" color="grey-6" /></q-item-section>
              <q-item-section>
                <q-item-label caption>{{ row.label }}</q-item-label>
                <q-item-label>{{ row.value || "—" }}</q-item-label>
              </q-item-section>
            </q-item>
          </q-list>
        </q-card>
      </div>

      <!-- Tenants & roles -->
      <div class="col-12 col-md-6">
        <q-card flat bordered class="account-card full-height">
          <q-card-section class="row items-center q-gutter-sm">
            <q-icon name="o_apartment" color="primary" size="sm" />
            <div class="text-subtitle1 text-weight-medium">Tenants &amp; roles</div>
            <q-space />
            <q-badge color="teal-1" text-color="primary">{{ assignments.length }}</q-badge>
          </q-card-section>
          <q-separator />
          <q-list separator>
            <q-item v-for="t in assignments" :key="t.tenantId">
              <q-item-section avatar>
                <q-avatar size="34px" color="teal-1" text-color="primary">
                  <q-icon name="o_business" size="18px" />
                </q-avatar>
              </q-item-section>
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
            <q-item v-if="!assignments.length">
              <q-item-section class="text-grey-6">No tenant assignments.</q-item-section>
            </q-item>
          </q-list>
        </q-card>
      </div>
    </div>
  </q-page>
</template>

<script setup>
import { ref, computed, onMounted } from "vue";
import { useAuthStore } from "stores/auth";
import { useTenantStore } from "stores/tenant";
import { profileApi, mediaApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import AppDetailHeader from "components/common/AppDetailHeader.vue";

const authStore = useAuthStore();
const tenantStore = useTenantStore();
const notify = useNotify();

const profile = ref(null);

const load = async () => {
  try {
    profile.value = await profileApi.getMine();
  } catch (err) {
    // Non-fatal: fall back to the basic auth-store info.
    notify.error(getApiErrorMessage(err));
  }
};

const displayName = computed(() =>
  profile.value?.fullName || profile.value?.displayName || authStore.user?.displayName || authStore.user?.email || "—");
const email = computed(() => profile.value?.primaryEmail || authStore.user?.email || "—");
// Kept with the markup that reads it, both commented out — see the note in the template.
// const completion = computed(() => profile.value?.profileCompletionPercentage || 0);

const avatarUrl = computed(() =>
  (profile.value?.profileMediaUrl ? mediaApi.absoluteUrl(profile.value.profileMediaUrl) : null));

const initials = computed(() => {
  const parts = (displayName.value || "").trim().split(/\s+/);
  const a = parts[0]?.charAt(0) || "";
  const b = parts.length > 1 ? parts[parts.length - 1].charAt(0) : "";
  return (a + b).toUpperCase() || "U";
});

// Assignments come from the tenant store (kept in sync with the auth profile).
const assignments = computed(() => tenantStore.assignments || authStore.user?.tenants || []);
// Distinct role names across every tenant assignment (multi-role, WO-123).
const roleChips = computed(() => [...new Set(assignments.value.flatMap((t) => t.roleNames || []))]);

const contactRows = computed(() => [
  { icon: "o_mail", label: "Email", value: email.value },
  { icon: "o_smartphone", label: "Phone Number", value: profile.value?.mobileNumber }
]);

onMounted(load);
</script>

<style scoped>
.account-card {
  border-radius: 16px;
}
</style>
