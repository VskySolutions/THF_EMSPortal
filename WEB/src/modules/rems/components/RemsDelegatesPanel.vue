<template>
  <q-card flat bordered class="mdp">
    <q-card-section class="row items-center">
      <div class="col">
        <!-- The explanation lives on the info icon: it is background somebody reads once, and as a
             paragraph under the title it was three lines of the card spent on it every time after. -->
        <div class="text-subtitle1 text-primary">
          <q-icon name="o_switch_account" size="20px" class="q-mr-xs" />{{ heading }}
          <app-info-tip :text="blurb" />
        </div>
      </div>
      <q-btn unelevated no-caps color="primary" icon="o_person_add" label="Add delegate" @click="openNew" />
    </q-card-section>
    <q-separator />

    <q-list v-if="rows.length" separator>
      <q-item v-for="d in rows" :key="d.id">
        <q-item-section>
          <q-item-label>
            {{ d.delegateName }}
            <q-badge v-if="!d.isActive" color="grey-6" class="q-ml-sm">Not active</q-badge>
          </q-item-label>
          <q-item-label caption>
            {{ d.canSend ? "Prepare and send to the client" : "Prepare only" }}
            <template v-if="d.startsOn || d.endsOn">
              · {{ d.startsOn || "any time" }} → {{ d.endsOn || "open-ended" }}
            </template>
          </q-item-label>
        </q-item-section>
        <q-item-section side>
          <div class="row q-gutter-xs">
            <q-btn flat round dense color="primary" icon="o_edit" @click="openEdit(d)">
              <q-tooltip>Edit</q-tooltip>
            </q-btn>
            <q-btn flat round dense color="negative" icon="o_delete" @click="remove(d)">
              <q-tooltip>Withdraw</q-tooltip>
            </q-btn>
          </div>
        </q-item-section>
      </q-item>
    </q-list>
    <q-card-section v-else class="text-grey-6">
      {{ emptyText }}
    </q-card-section>

    <q-dialog v-model="editOpen">
      <q-card class="mdp__dialog">
        <q-card-section class="text-subtitle1 text-primary">
          {{ form.id ? "Edit delegate" : "Add delegate" }}
        </q-card-section>
        <q-separator />
        <q-card-section class="column q-gutter-md">
          <app-select
            v-model="form.delegateUserId" :options="userOptions" label="Delegate" required
            :readonly="!!form.id" :clearable="false"
          />
          <div>
            <q-toggle v-model="form.canPrepare" :label="prepareLabel" color="primary" />
            <div class="text-caption text-grey-7 q-ml-lg">Create and fill them in. Commits nothing.</div>
          </div>
          <div>
            <q-toggle v-model="form.canSend" label="Can send the intake form to the client" color="primary" />
            <!-- The reason the two are separate: leaving this off means nothing reaches a client until the
                 principal has looked at it. -->
            <div class="text-caption text-grey-7 q-ml-lg">
              {{ sendHint }}
            </div>
          </div>
          <div class="row q-col-gutter-md">
            <app-date-field v-model="form.startsOn" label="From" class="col-12 col-sm-6" />
            <app-date-field v-model="form.endsOn" label="Until" class="col-12 col-sm-6" />
          </div>
          <div class="text-caption text-grey-7">Leave the dates empty for an open-ended delegation.</div>
        </q-card-section>
        <q-separator />
        <q-card-actions align="right">
          <q-btn flat no-caps color="grey-8" label="Cancel" @click="editOpen = false" />
          <q-btn
            unelevated no-caps color="primary" label="Save" :loading="saving"
            :disable="!form.delegateUserId" @click="save"
          />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </q-card>
</template>

<script setup>
// Delegate management, in two voices.
import { ref, reactive, computed, onMounted } from "vue";
import { remsApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import AppSelect from "components/common/AppSelect.vue";
import AppDateField from "components/common/AppDateField.vue";
import AppInfoTip from "components/common/AppInfoTip.vue";

const props = defineProps({
  // Whose delegates these are. Omitted (null) = the signed-in user's own, managed self-service.
  principalUserId: { type: String, default: null },
  // How to name them in the copy when an admin is arranging it for somebody.
  principalName: { type: String, default: "this user" }
});

const notify = useNotify();
const { confirm } = useConfirm();

const forSomeoneElse = computed(() => !!props.principalUserId);

const heading = computed(() => (forSomeoneElse.value ? "REMS delegates" : "My REMS delegates"));
const blurb = computed(() => forSomeoneElse.value
  ? `People who may work ${props.principalName}'s REMS requests. Everything they do stays attributed to ` +
    "both of them. Delegation never covers approving."
  : "People who may work your REMS requests for you. Everything they do stays attributed to both of you. " +
    "Delegation never covers approving.");
const emptyText = computed(() => forSomeoneElse.value
  ? `${props.principalName} has nobody named. Add a delegate to let someone prepare their requests.`
  : "You have not named anyone. Add a delegate if you want someone able to prepare your requests.");
const prepareLabel = computed(() =>
  forSomeoneElse.value ? `Can prepare requests as ${props.principalName}` : "Can prepare requests as me");
const sendHint = computed(() => forSomeoneElse.value
  ? "Leave off and every request waits for them before the client sees it."
  : "Leave off and you see every request before the client does.");

const rows = ref([]);
const userOptions = ref([]);
const editOpen = ref(false);
const saving = ref(false);

const blank = () => ({
  id: null, delegateUserId: null, canPrepare: true, canSend: false, startsOn: "", endsOn: ""
});
const form = reactive(blank());

const load = async () => {
  const fetch = forSomeoneElse.value
    ? remsApi.userDelegates(props.principalUserId)
    : remsApi.myDelegates();
  rows.value = (await fetch.catch(() => [])) || [];
};

const loadUsers = async () => {
  // Self-service offers the tenant's admins; an admin arranging cover chooses from the whole firm.
  const fetch = forSomeoneElse.value
    ? remsApi.userDelegateCandidates(props.principalUserId)
    : remsApi.admins();
  const candidates = await fetch.catch(() => []);
  userOptions.value = (candidates || []).map((a) => ({ label: a.name, value: a.id }));
};

const openNew = () => {
  Object.assign(form, blank());
  editOpen.value = true;
};

const openEdit = (d) => {
  Object.assign(form, {
    id: d.id,
    delegateUserId: d.delegateUserId,
    canPrepare: d.canPrepare,
    canSend: d.canSend,
    startsOn: d.startsOn || "",
    endsOn: d.endsOn || ""
  });
  editOpen.value = true;
};

const save = async () => {
  saving.value = true;
  try {
    const payload = {
      delegateUserId: form.delegateUserId,
      canPrepare: form.canPrepare,
      canSend: form.canSend,
      startsOn: form.startsOn || null,
      endsOn: form.endsOn || null
    };
    await (forSomeoneElse.value
      ? remsApi.saveUserDelegate(props.principalUserId, payload)
      : remsApi.saveDelegate(payload));
    notify.success("Delegate saved.");
    editOpen.value = false;
    await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    saving.value = false;
  }
};

const remove = async (d) => {
  const ok = await confirm({
    title: "Withdraw delegation",
    message: `${d.delegateName} will no longer be able to work ${forSomeoneElse.value ? `${props.principalName}'s` : "your"} ` +
      "REMS requests. Anything they already prepared stays as it is.",
    confirmLabel: "Withdraw",
    type: "danger"
  });
  if (!ok) return;
  try {
    await (forSomeoneElse.value
      ? remsApi.removeUserDelegate(props.principalUserId, d.id)
      : remsApi.removeDelegate(d.id));
    notify.success("Delegate withdrawn.");
    await load();
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  }
};

onMounted(() => {
  load();
  loadUsers();
});
</script>

<style scoped>
.mdp { border-radius: 12px; }
.mdp__dialog { width: 480px; max-width: 92vw; border-radius: 12px; }
</style>
