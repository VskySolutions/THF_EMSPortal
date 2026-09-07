<template>
  <!-- A single contact role bound to a RemsRolePayload node. -->
  <div class="role-block" :class="{ 'role-block--required': required }">
    <div class="role-block__head">
      <div class="role-block__title">
        {{ label }}
        <!-- What this contact is FOR, where the label alone leaves a real question. -->
        <q-icon v-if="hint" name="o_info" size="15px" color="grey-6" class="role-block__info">
          <q-tooltip anchor="top middle" self="bottom middle" max-width="280px" :delay="200">
            {{ hint }}
          </q-tooltip>
        </q-icon>
      </div>
      <q-badge
        :color="required ? 'red-1' : 'blue-grey-1'"
        :text-color="required ? 'red-8' : 'blue-grey-8'"
        :label="required ? 'Required' : 'Optional'"
      />
    </div>
    <div class="row q-col-gutter-sm">
      <!-- Two boxes, because a contact becomes a Person and a Person is filed under a given name and a
           family name. -->
      <!-- A contact becomes a Person record, so the two name boxes are held to what a name actually is:
           letters, and the hyphen / apostrophe / period that appear inside real ones. -->
      <app-text-field
        v-model="role.firstName" label="First Name" :required="required" class="col-12 col-sm-6"
        :rules="nameRules('First Name')"
        :error="!!err('firstName')" :error-message="err('firstName')"
      />
      <app-text-field
        v-model="role.lastName" label="Last Name" :required="required" class="col-8 col-sm-4"
        :rules="nameRules('Last Name')"
        :error="!!err('lastName')" :error-message="err('lastName')"
      />
      <!-- The generational particle on their name — Jr., Sr., III. Never required: most people have none,
           and one guessed on their behalf is worse than none. -->
      <app-name-suffix-field v-model="role.suffix" class="col-4 col-sm-2" />
      <app-text-field
        v-model="role.email" label="Email" type="email" :required="required" class="col-12 col-sm-6"
        :error="!!err('email')" :error-message="err('email')"
      />
      <div class="col-12 col-sm-6">
        <!-- Optional even on a required contact: roleComplete() asks only for a name and a valid email. -->
        <app-phone-input v-model="role.phone" label="Phone Number" />
        <div v-if="err('phone')" class="text-negative text-caption q-mt-xs">{{ err('phone') }}</div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { nameRules } from "utils/personName";
import AppTextField from "components/common/AppTextField.vue";
import AppNameSuffixField from "components/common/AppNameSuffixField.vue";
import AppPhoneInput from "components/common/AppPhoneInput.vue";

const role = defineModel({ type: Object, required: true });

const props = defineProps({
  label: { type: String, required: true },
  // What this contact is for. Empty on the roles that explain themselves (Self, Spouse).
  hint: { type: String, default: "" },
  required: { type: Boolean, default: false },
  // Dotted payload path (e.g. "roles.self") used to look up per-field server messages.
  prefix: { type: String, default: "" },
  errors: { type: Object, default: () => ({}) }
});

const err = (field) => (props.prefix ? props.errors[`${props.prefix}.${field}`] : "") || "";
</script>

<style scoped>
/* Padding and heading gap trimmed to match the rest of the intake form — several of these stack in one
   card, so what each costs is paid for as many times as the group has contacts. */
.role-block {
  border: 1px solid #e0e6ed;
  border-radius: 10px;
  padding: 10px 12px;
  background: #fff;
}
.role-block--required {
  border-color: #d5deea;
}
.role-block__head {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}
.role-block__title {
  font-size: 13px;
  font-weight: 600;
  color: #2c3540;
}
.role-block__info {
  margin-left: 4px;
  cursor: help;
  vertical-align: text-bottom;
}
</style>
