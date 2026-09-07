<template>
  <!-- Card, matching the sign-in form: the auth layout centres it and sets the width. -->
  <q-card flat bordered class="auth-card q-pa-lg">
    <div class="text-h5 text-weight-bold q-mb-xs">Forgot your password?</div>
    <div class="text-body2 text-grey-7 q-mb-md">
      Enter the email address you sign in with and we'll send you a link to choose a new password.
    </div>

    <!-- Deliberately shown for ANY submitted address: confirming only known accounts would let anyone test
         which email addresses have logins here. -->
    <q-banner v-if="sent" dense class="bg-green-1 text-green-9 rounded-borders q-mb-md">
      <template #avatar><q-icon name="o_mark_email_read" color="green-9" /></template>
      If that email address has an account, a reset link is on its way. The link expires in 60 minutes.
    </q-banner>

    <q-form v-else greedy @submit.prevent.stop="onSubmit">
      <app-text-field
        v-model="email" label="Email" type="email" required autocomplete="on" class="q-mb-md"
        :rules="[
          (v) => !!v || 'Email is required',
          (v) => /^\S+@\S+\.\S+$/.test(v || '') || 'Enter a valid email address'
        ]"
      />
      <q-btn
        unelevated no-caps color="primary" label="Send reset link" type="submit"
        class="full-width" :loading="loading"
      />
    </q-form>

    <div class="row justify-center q-mt-md">
      <q-btn flat no-caps dense color="primary" icon="o_arrow_back" label="Back to sign in" :to="{ name: 'login' }" />
    </div>
  </q-card>
</template>

<script setup>
// Step 1 of the self-service reset. Anonymous — no auth store involvement.
import { ref } from "vue";
import { authApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import AppTextField from "components/common/AppTextField.vue";

const { notifyError } = useNotify();

const email = ref("");
const loading = ref(false);
const sent = ref(false);

const onSubmit = async () => {
  loading.value = true;
  try {
    await authApi.forgotPassword(email.value.trim());
    // Success regardless of whether the address is known — the API answers identically either way.
    sent.value = true;
  } catch (err) {
    notifyError(getApiErrorMessage(err, "Could not send the reset link. Please try again."));
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
