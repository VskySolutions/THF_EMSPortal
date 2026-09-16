<template>
  <!-- Card, matching the sign-in form: the auth layout centres it and sets the width. -->
  <q-card flat bordered class="auth-card q-pa-lg">
    <div v-if="!errorMessage" class="column items-center q-py-md">
      <q-spinner-dots color="primary" size="40px" />
      <div class="text-body1 text-grey-8 q-mt-md">Signing you in with Microsoft…</div>
    </div>

    <template v-else>
      <div class="text-h5 text-weight-bold q-mb-md">Microsoft sign-in</div>
      <q-banner dense rounded class="bg-red-1 text-negative q-mb-md">
        <template #avatar>
          <q-icon name="o_error" color="negative" />
        </template>
        {{ errorMessage }}
      </q-banner>
      <q-btn unelevated no-caps color="primary" label="Back to sign in" class="full-width" :to="{ name: 'login' }" />
    </template>
  </q-card>
</template>

<script setup>
// The last leg of "Login with Microsoft": the API has verified the Microsoft account and sent the browser here
// with a one-time code, which is traded for a normal session. On success this page is replaced by wherever
// the person was going.
import { onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { useAuthStore } from "stores/auth";
import { getApiErrorMessage } from "services/api";
import { postLoginDestination } from "modules/auth/returnPath";

const route = useRoute();
const router = useRouter();
const authStore = useAuthStore();

const errorMessage = ref("");

onMounted(async () => {
  try {
    const returnTo = await authStore.completeMicrosoftLogin({ code: route.query.code, state: route.query.state });

    // Never true for a Microsoft sign-in today, but the gate is the same one the password form honours.
    if (authStore.mustChangePassword) {
      router.replace({ name: "change_password" });
      return;
    }
    const destination = postLoginDestination(returnTo);
    localStorage.setItem("last_route", destination);
    router.replace(destination);
  } catch (err) {
    const fallback = "Microsoft sign-in failed. Please try again.";
    errorMessage.value = err?.response ? getApiErrorMessage(err, fallback) : (err?.message || fallback);
  }
});
</script>

<style scoped>
.auth-card {
  width: 100%;
  border-radius: 16px;
}
</style>
