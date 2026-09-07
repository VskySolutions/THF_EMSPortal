<template>
  <q-dialog v-model="open" persistent>
    <q-card class="person-dialog column no-wrap">
      <q-card-section class="row items-center bg-primary text-white">
        <div class="text-h6">Add Person</div>
        <q-space />
        <q-btn flat round dense icon="o_close" @click="close" />
      </q-card-section>
      <q-separator />

      <q-card-section class="col scroll">
        <q-form ref="formRef" greedy>
          <person-form-fields
            v-model="form"
            :tenant-options="showTenantPicker ? tenantOptions : []"
            :loading-tenants="loadingTenants"
          />
        </q-form>
      </q-card-section>

      <q-separator />
      <q-card-actions align="right" class="bg-grey-1">
        <q-btn flat no-caps color="grey-8" label="Cancel" @click="close" />
        <q-btn unelevated no-caps color="primary" label="Create Person" :loading="saving" @click="submit" />
      </q-card-actions>
    </q-card>
  </q-dialog>
</template>

<script setup>
// Quick-add Person dialog used inline by the user-create form (the "+" next to the Person dropdown).
import { ref, reactive, computed, watch } from "vue";
import { personApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { blankPersonForm } from "composables/personForm";
import { useTenantOptions } from "composables/useTenantOptions";
import PersonFormFields from "components/person/PersonFormFields.vue";

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  // The tenant the person belongs to, when the caller already knows it — the tenant-management screen,
  // which is looking at one tenant and adding somebody to it.
  tenantId: { type: String, default: null }
});
const emit = defineEmits(["update:modelValue", "created"]);

const notify = useNotify();
const formRef = ref(null);
const saving = ref(false);
const form = reactive(blankPersonForm());
const { canChooseTenant, activeTenantId, tenantOptions, loadingTenants, loadTenants } = useTenantOptions();

const open = computed({
  get: () => props.modelValue,
  set: (v) => emit("update:modelValue", v)
});

// A fixed tenant settles the question the dropdown exists to ask, so the dropdown is not offered.
const showTenantPicker = computed(() => canChooseTenant.value && !props.tenantId);

// Reset to a clean form each time the dialog opens, applying the tenant-selection rule.
watch(open, async (isOpen) => {
  if (!isOpen) return;
  Object.assign(form, blankPersonForm());
  if (props.tenantId) {
    form.tenantId = props.tenantId;
  } else if (canChooseTenant.value) {
    await loadTenants();
  } else {
    form.tenantId = activeTenantId.value;
  }
});

const close = () => { open.value = false; };

const submit = async () => {
  if (!(await formRef.value?.validate())) return;
  saving.value = true;
  try {
    const detail = await personApi.create({ ...form, dateOfBirth: form.dateOfBirth || null });
    notify.success("Person created.");
    emit("created", detail);
    open.value = false;
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    saving.value = false;
  }
};
</script>

<style scoped>
.person-dialog {
  width: 640px;
  max-width: 95vw;
  max-height: 90vh;
}
</style>
