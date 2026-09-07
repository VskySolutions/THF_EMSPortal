<template>
  <q-form greedy @submit.prevent.stop="onSubmit">
    <q-card-section>
      <!-- The standard password box (label above the field, one eye toggle), so these three read like every
           other field in the application rather than carrying their labels inside the box. -->
      <app-password-field
        v-model="model.oldPassword" label="Current Password" required maxlength="20"
        :autofocus="autofocus" class="q-mb-md" autocomplete="current-password"
        :error="v$.oldPassword.$error" :error-message="v$.oldPassword.$errors[0]?.$message"
        @blur="v$.oldPassword.$touch"
      >
        <template #prepend><q-icon name="o_lock" /></template>
      </app-password-field>

      <app-password-field
        v-model="model.newPassword" label="New Password" required maxlength="20" class="q-mb-md"
        :error="v$.newPassword.$error" :error-message="v$.newPassword.$errors[0]?.$message"
        @blur="v$.newPassword.$touch"
      >
        <template #prepend><q-icon name="o_lock_reset" /></template>
      </app-password-field>

      <app-password-field
        v-model="model.confirmPassword" label="Confirm Password" required maxlength="20"
        :error="v$.confirmPassword.$error" :error-message="v$.confirmPassword.$errors[0]?.$message"
        @blur="v$.confirmPassword.$touch"
      >
        <template #prepend><q-icon name="o_lock_reset" /></template>
      </app-password-field>

      <q-banner dense class="bg-grey-2 text-grey-8 q-mt-md account-hint">
        <template #avatar><q-icon name="o_info" color="primary" /></template>
        <div class="text-weight-medium q-mb-xs">Password requirements</div>
        <ul class="q-my-none q-pl-md">
          <li>Minimum 8 characters long — the more, the better</li>
          <li>At least one lowercase character</li>
          <li>At least one uppercase character</li>
          <li>At least one number and one special character</li>
        </ul>
      </q-banner>
    </q-card-section>

    <q-separator />
    <q-card-actions align="right" class="q-pa-md">
      <q-btn v-if="cancelTo" flat color="grey-8" label="Cancel" type="button" no-caps :to="cancelTo" />
      <q-btn unelevated color="primary" :label="submitLabel" type="submit" no-caps :loading="loading" />
    </q-card-actions>
  </q-form>
</template>

<script setup>
// THE change-password form.
import { ref } from "vue";
import useVuelidate from "@vuelidate/core";
import { required, helpers, minLength } from "@vuelidate/validators";
import { useRouter } from "vue-router";
import { authApi, getApiErrorMessage } from "services/api";
import { useAuthStore } from "stores/auth";
import { useNotify } from "composables/useNotify";
import AppPasswordField from "components/common/AppPasswordField.vue";

defineProps({
  // Renders a Cancel button pointing at this route; omit for an embedded card that has nothing to leave.
  cancelTo: { type: [String, Object], default: null },
  submitLabel: { type: String, default: "Set New Password" },
  autofocus: { type: Boolean, default: false }
});

// Raised BEFORE the redirect, so a host with a leave-guard (the forced-change screen) can record that the
// change went through and let the navigation happen.
const emit = defineEmits(["changed"]);

const router = useRouter();
const authStore = useAuthStore();
const { notifySuccess, notifyError } = useNotify();

const loading = ref(false);

const model = ref({ oldPassword: "", newPassword: "", confirmPassword: "" });

const rules = {
  oldPassword: { required: helpers.withMessage("Current password is required", required) },
  newPassword: {
    required: helpers.withMessage("New password is required", required),
    minLength: helpers.withMessage("The password must be at least 8 characters", minLength(8)),
    containsLowerCase: helpers.withMessage("The password must contain a lowercase character", (v) => /[a-z]/.test(v)),
    containsUppercase: helpers.withMessage("The password must contain an uppercase character", (v) => /[A-Z]/.test(v)),
    containsNumber: helpers.withMessage("The password must contain a number", (v) => /[0-9]/.test(v)),
    containsSpecialCharacter: helpers.withMessage("The password must contain a special character", (v) => /[#?!@$%^&*-]/.test(v))
  },
  confirmPassword: {
    required: helpers.withMessage("Confirm password is required", required),
    // A rule rather than a post-validate check, so the mismatch is reported on the field itself.
    sameAsNew: helpers.withMessage(
      "New password and confirmation do not match",
      (value) => value === model.value.newPassword)
  }
};

const v$ = useVuelidate(rules, model, { $lazy: true, $autoDirty: true });

const onSubmit = async () => {
  if (!(await v$.value.$validate())) return;

  loading.value = true;
  try {
    await authApi.changePassword(model.value.oldPassword, model.value.newPassword);
    authStore.mustChangePassword = false;
    emit("changed");
    notifySuccess("Your password has been changed. Please sign in again.");
    // The API bumps TokenVersion, so every session is already dead server-side — re-authenticate.
    authStore.clearSession();
    router.replace({ name: "login" });
  } catch (err) {
    notifyError(getApiErrorMessage(err, "Could not change password. Please try again."));
  } finally {
    loading.value = false;
  }
};
</script>

<style scoped>
.account-hint {
  border-radius: 8px;
}
.account-hint ul li {
  margin: 2px 0;
}
</style>
