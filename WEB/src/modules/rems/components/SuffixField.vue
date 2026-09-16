<template>
  <!-- The whole box opens the suggestions, not only the chevron at its far end: a box that answers on its
       icon alone reads as a dropdown that does not work. Typing carries on underneath — the menu takes no
       focus, and a free-text particle is as good an answer as any on the list. -->
  <app-text-field
    :model-value="modelValue" :label="label" :placeholder="placeholder" :readonly="readonly || locked"
    :error="error" :error-message="errorMessage"
    @update:model-value="$emit('update:modelValue', $event)"
    @click="openMenu"
  >
    <template #append>
      <q-icon v-if="locked" name="o_lock" size="18px" color="grey-6" />
      <q-icon
        v-else-if="!readonly" name="o_arrow_drop_down" size="24px" color="grey-7" class="cursor-pointer"
        aria-label="Suffix suggestions"
      />
      <q-menu
        v-if="!readonly && !locked" v-model="menuOpen"
        no-parent-event no-focus no-refocus auto-close anchor="bottom end" self="top end"
      >
        <q-list dense style="min-width: 150px;">
          <q-item
            v-for="opt in SUFFIX_OPTIONS" :key="opt.value"
            clickable :active="modelValue === opt.value" active-class="bg-grey-2 text-primary"
            @click="pick(opt.value)"
          >
            <q-item-section>
              <q-item-label>{{ opt.label }}</q-item-label>
              <q-item-label caption>{{ opt.caption }}</q-item-label>
            </q-item-section>
          </q-item>
          <q-separator />
          <q-item clickable :disable="!modelValue" @click="pick('')">
            <q-item-section class="text-grey-7">No suffix</q-item-section>
          </q-item>
        </q-list>
      </q-menu>
    </template>
  </app-text-field>
</template>

<script setup>
// The generational particle on a name — Jr., Sr., II, III, IV — typed freely or picked off the shortlist.
// One component for every box that asks it, so they all open and suggest the same way.
import { ref } from "vue";
import { CLIENT_NAME_SUFFIXES } from "modules/rems/remsContactRoles";
import AppTextField from "components/common/AppTextField.vue";

defineProps({
  modelValue: { type: String, default: "" },
  label: { type: String, default: "Suffix" },
  placeholder: { type: String, default: "Jr." },
  readonly: { type: Boolean, default: false },
  // Locked reads as readonly with a padlock in the corner saying why.
  locked: { type: Boolean, default: false },
  error: { type: Boolean, default: false },
  errorMessage: { type: String, default: "" }
});
const emit = defineEmits(["update:modelValue"]);

const SUFFIX_OPTIONS = CLIENT_NAME_SUFFIXES;
const menuOpen = ref(false);

const openMenu = () => { menuOpen.value = true; };
const pick = (value) => {
  emit("update:modelValue", value);
  menuOpen.value = false;
};
</script>
