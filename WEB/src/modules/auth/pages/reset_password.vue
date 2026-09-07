<template>
  <!-- Card, matching the sign-in form: the auth layout centres it and sets the width. -->
  <q-card flat bordered class="auth-card q-pa-lg">
    <div class="text-h5 text-weight-bold q-mb-xs">Choose a new password</div>

    <template v-if="!token">
      <q-banner dense class="bg-red-1 text-red-9 rounded-borders q-my-md">
        <template #avatar><q-icon name="o_error" color="red-9" /></template>
        This reset link is missing its token. Please use the link from your email, or request a new one.
      </q-banner>
      <q-btn unelevated no-caps color="primary" label="Request a new link" class="full-width" :to="{ name: 'forgot_password' }" />
    </template>

    <template v-else>
      <div class="text-body2 text-grey-7 q-mb-md">Pick a password you don't use anywhere else.</div>

      <q-form greedy @submit.prevent.stop="onSubmit">
        <app-text-field
          v-model="newPassword" label="New Password" type="password" required class="q-mb-md" :rules="passwordRules"
        />
        <app-text-field
          v-model="confirmPassword" label="Confirm Password" type="password" required class="q-mb-md"
          :rules="[
            (v) => !!v || 'Confirm password is required',
            (v) => v === newPassword || 'New password and confirmation do not match'
          ]"
        />

        <q-banner dense class="bg-grey-2 text-grey-8 rounded-borders q-mb-md">
          <template #avatar><q-icon name="o_info" color="primary" /></template>
          <div class="text-weight-medium q-mb-xs">Password requirements</div>
          <ul class="q-my-none q-pl-md">
            <li>Minimum 8 characters long — the more, the better</li>
            <li>At least one lowercase character</li>
            <li>At least one uppercase character</li>
            <li>At least one number and one special character</li>
          </ul>
        </q-banner>

        <q-btn
          unelevated no-caps color="primary" label="Set new password" type="submit"
          class="full-width" :loading="loading"
        />
      </q-form>
    </template>

    <div class="row justify-center q-mt-md">
      <q-btn flat no-caps dense color="primary" icon="o_arrow_back" label="Back to sign in" :to="{ name: 'login' }" />
    </div>
  </q-card>
</template>

<script setup>
// Step 2 of the self-service reset: redeem the emailed token.
import { ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { authApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import AppTextField from "components/common/AppTextField.vue";

const route = useRoute();
const router = useRouter();
const { notifySuccess, notifyError } = useNotify();

const token = ref(route.query.token || "");
const newPassword = ref("");
const confirmPassword = ref("");
const loading = ref(false);

const passwordRules = [
  (v) => !!v || "New password is required",
  (v) => (v || "").length >= 8 || "The password must be at least 8 characters",
  (v) => /[a-z]/.test(v || "") || "The password must contain a lowercase character",
  (v) => /[A-Z]/.test(v || "") || "The password must contain an uppercase character",
  (v) => /[0-9]/.test(v || "") || "The password must contain a number",
  (v) => /[#?!@$%^&*-]/.test(v || "") || "The password must contain a special character"
];

const onSubmit = async () => {
  loading.value = true;
  try {
    await authApi.resetPassword(token.value, newPassword.value);
    notifySuccess("Your password has been reset. Please sign in.");
    router.replace({ name: "login" });
  } catch (err) {
    notifyError(getApiErrorMessage(err, "This reset link is invalid or has expired. Please request a new one."));
  } finally {
    loading.value = false;
  }
};
</script>

<style scoped>
.auth-card {
  width: 100%;
  border-radius: 16px;
}
</style>
