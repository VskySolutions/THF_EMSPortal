<template>
  <q-card flat bordered class="auth-card q-pa-lg">
    <div class="q-mb-lg">
      <div class="text-h5 text-weight-bold">Welcome back 👋</div>
      <div class="text-body2 text-grey-7 q-mt-xs">Please sign in to your account to continue.</div>
    </div>

    <q-banner v-if="errorMessage" dense rounded class="bg-red-1 text-negative q-mb-md auth-error">
      <template #avatar>
        <q-icon name="o_error" color="negative" />
      </template>
      {{ errorMessage }}
    </q-banner>

    <q-form greedy @submit.prevent.stop="login">
      <app-text-field
        v-model="model.email"
        type="email"
        label="Email"
        maxlength="128"
        autofocus
        class="q-mb-md"
        :error="v$.email.$error"
        :error-message="v$.email.$errors[0]?.$message"
        @blur="v$.email.$touch"
      >
        <template #prepend>
          <q-icon name="o_mail" />
        </template>
      </app-text-field>

      <app-password-field
        v-model="model.password"
        label="Password"
        maxlength="28"
        autocomplete="off"
        :error="v$.password.$error"
        :error-message="v$.password.$errors[0]?.$message"
        @blur="v$.password.$touch"
      >
        <template #prepend>
          <q-icon name="o_lock" />
        </template>
      </app-password-field>

      <div class="row items-center justify-between q-mt-sm q-mb-lg">
        <q-checkbox v-model="model.isRememberMeChecked" dense label="Remember me" color="primary" />
        <q-btn flat dense no-caps color="primary" label="Forgot password?" :to="{ name: 'forgot_password' }" />
      </div>

      <q-btn label="Login" type="submit" color="primary" unelevated no-caps size="md" class="full-width" :loading="loading" />
    </q-form>

    <div class="row items-center q-my-md">
      <q-separator class="col" />
      <span class="q-px-sm text-caption text-grey-6">or</span>
      <q-separator class="col" />
    </div>

    <!-- A navigation, not a request: the sign-in runs through the API, which holds the Microsoft keys. -->
    <q-btn
      outline no-caps color="grey-8" size="md" class="full-width"
      :loading="microsoftLoading" :disable="loading" @click="loginWithMicrosoft"
    >
      <svg class="microsoft-logo q-mr-sm" viewBox="0 0 21 21" aria-hidden="true">
        <rect x="1" y="1" width="9" height="9" fill="#f25022" />
        <rect x="11" y="1" width="9" height="9" fill="#7fba00" />
        <rect x="1" y="11" width="9" height="9" fill="#00a4ef" />
        <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
      </svg>
      Login with Microsoft
    </q-btn>
  </q-card>
</template>

<script setup>
import { ref } from "vue";
import useVuelidate from "@vuelidate/core";
import { required, helpers, email } from "@vuelidate/validators";
import { useRoute, useRouter } from "vue-router";
import { useAuthStore } from "stores/auth";
import { getApiErrorMessage, getApiErrorCode, ApiErrorCodes } from "services/api";
import { setLocalStorage, getLocalStorage, clearLocalStorage } from "assets/utils";
import { postLoginDestination } from "modules/auth/returnPath";
import AppTextField from "components/common/AppTextField.vue";
import AppPasswordField from "components/common/AppPasswordField.vue";

const route = useRoute();
const router = useRouter();
const authStore = useAuthStore();

const loading = ref(false);
const microsoftLoading = ref(false);
const errorMessage = ref("");

// What the API's Microsoft sign-in can send the browser back here with (?ssoError=…), in words.
const SSO_ERRORS = {
  not_configured: "Microsoft sign-in is not set up on the server yet. Please sign in with your email and password.",
  cancelled: "Microsoft sign-in was cancelled.",
  expired: "The Microsoft sign-in took too long. Please try again.",
  invalid_state: "The Microsoft sign-in could not be verified. Please try again.",
  provider: "Microsoft could not complete the sign-in. Please try again.",
  no_account: "There is no EMS Portal account for that Microsoft account. Ask your administrator to add you, or sign in with your email and password.",
  disabled: "Your account is disabled. Please contact your administrator.",
  failed: "Microsoft sign-in failed. Please try again."
};

if (route.query.ssoError) {
  errorMessage.value = SSO_ERRORS[route.query.ssoError] || SSO_ERRORS.failed;
  // Said once: a refresh should not keep repeating it.
  const query = { ...route.query };
  delete query.ssoError;
  router.replace({ query });
}

// Remember-me persistence (email only; never persist the password).
const localStorageKey = "Login";
const filterLocalStorage = getLocalStorage(localStorageKey);

const model = ref({
  email: filterLocalStorage?.email || "",
  password: "",
  isRememberMeChecked: filterLocalStorage?.isRememberMeChecked || false
});

const rules = {
  email: {
    required: helpers.withMessage("Email is required", required),
    email: helpers.withMessage("Enter a valid email address", email)
  },
  password: { required: helpers.withMessage("Password is required", required) }
};

const v$ = useVuelidate(rules, model, { $lazy: true, $autoDirty: true });

const login = async () => {
  errorMessage.value = "";
  if (!(await v$.value.$validate())) {
    return;
  }

  loading.value = true;
  try {
    await authStore.login({ email: model.value.email, password: model.value.password });

    if (model.value.isRememberMeChecked) {
      setLocalStorage(localStorageKey, { email: model.value.email, isRememberMeChecked: true });
    } else {
      clearLocalStorage(localStorageKey);
    }

    // AC-UI-001.5: first login with a temporary password.
    if (authStore.mustChangePassword) {
      router.push({ name: "change_password" });
      return;
    }
    redirectAfterLogin();
  } catch (err) {
    // AC-UI-001.3 (no field hint) / AC-UI-001.4 (inactive) / AC-UI-001.6 (server error).
    const code = getApiErrorCode(err);
    if (err?.response?.status === 401) {
      errorMessage.value = getApiErrorMessage(err, "Invalid email or password.");
    } else if (code === ApiErrorCodes.Forbidden || err?.response?.status === 403) {
      errorMessage.value = "Your account is disabled. Please contact your administrator.";
    } else {
      errorMessage.value = getApiErrorMessage(err, "Unable to sign in right now. Please try again.");
    }
  } finally {
    loading.value = false;
  }
};

// The browser leaves for the API here; the spinner stays until the page is gone.
const loginWithMicrosoft = () => {
  errorMessage.value = "";
  microsoftLoading.value = true;
  authStore.beginMicrosoftLogin(route.query.redirect);
};

const redirectAfterLogin = () => {
  // `redirect` returns a lapsed session to where it left off.
  const destination = postLoginDestination(route.query.redirect);
  localStorage.setItem("last_route", destination);
  router.push(destination);
};
</script>

<style scoped>
.auth-card {
  width: 100%;
  border-radius: 16px;
}
.microsoft-logo {
  width: 18px;
  height: 18px;
}
.auth-link {
  text-decoration: none;
}
.auth-link:hover {
  text-decoration: underline;
}
</style>
