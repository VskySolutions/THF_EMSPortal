<template>
  <!-- The generational particle on a name — Jr., Sr., II, III, IV — in a box of its own. -->
  <app-text-field
    v-model="model"
    :label="label"
    :placeholder="placeholder"
    :readonly="readonly"
    :disable="disable"
    :error="tooLong"
    :error-message="`A ${label.toLowerCase()} is at most ${NAME_SUFFIX_MAX_LENGTH} characters.`"
    @blur="emit('blur', $event)"
  >
    <template #append>
      <q-icon v-if="readonly" name="o_lock" size="18px" color="grey-6" />
      <q-btn
        v-else-if="!disable" flat dense round size="sm" icon="o_arrow_drop_down" color="grey-7"
        :aria-label="`${label} suggestions`"
      >
        <q-menu anchor="bottom end" self="top end" auto-close>
          <q-list dense style="min-width: 150px;">
            <q-item
              v-for="opt in NAME_SUFFIXES" :key="opt.value"
              clickable :active="model === opt.value" active-class="bg-grey-2 text-primary"
              @click="pick(opt.value)"
            >
              <q-item-section>
                <q-item-label>{{ opt.label }}</q-item-label>
                <q-item-label caption>{{ opt.caption }}</q-item-label>
              </q-item-section>
            </q-item>
            <q-separator />
            <q-item clickable :disable="!model" @click="pick('')">
              <q-item-section class="text-grey-7">No {{ label.toLowerCase() }}</q-item-section>
            </q-item>
          </q-list>
        </q-menu>
      </q-btn>
    </template>
  </app-text-field>
</template>

<script setup>
// The name SUFFIX field — Jr., Sr., II, III, IV — asked beside a name.
import { computed, nextTick } from "vue";
import AppTextField from "components/common/AppTextField.vue";
import { NAME_SUFFIXES, NAME_SUFFIX_MAX_LENGTH } from "utils/personName";

const model = defineModel({ type: String, default: "" });

// Forwarded from the box, and raised for a suggestion picked off the menu too — a screen that saves as
// you leave a field has to hear about a suffix chosen from the list as well as one typed.
const emit = defineEmits(["blur"]);

defineProps({
  // "Suffix" everywhere today. A prop because the box is small and the word above it is all the
  // explanation it gets, so a screen that calls it something else can.
  label: { type: String, default: "Suffix" },
  placeholder: { type: String, default: "Jr." },
  readonly: { type: Boolean, default: false },
  disable: { type: Boolean, default: false }
});

const tooLong = computed(() => (model.value?.trim().length || 0) > NAME_SUFFIX_MAX_LENGTH);

// A tick, so the new value is on the model before anything listening to `blur` reads it back.
const pick = async (value) => {
  model.value = value;
  await nextTick();
  emit("blur");
};
</script>
