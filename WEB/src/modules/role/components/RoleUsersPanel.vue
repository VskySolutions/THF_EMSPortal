<template>
  <q-card flat bordered class="role-users-panel">
    <q-card-section class="row items-center q-gutter-sm">
      <q-icon name="o_group" color="primary" size="sm" />
      <div class="text-subtitle1 text-weight-medium">Users</div>
      <app-info-tip
        text="Everyone in this tenant holding the role. Somebody with no role here at all is not on the list: a role is how a person belongs to a tenant, so their first one is granted on the Users page."
      />
      <q-space />
      <q-btn unelevated no-caps dense color="primary" icon="o_person_add" label="Add" @click="openAdd" />
    </q-card-section>
    <q-separator />

    <q-list separator>
      <q-item v-for="member in members" :key="member.userId">
        <q-item-section avatar><q-icon name="o_person" color="grey-7" /></q-item-section>
        <q-item-section>
          <q-item-label>
            {{ member.displayName }}
            <q-badge v-if="!member.isActive" color="grey" class="q-ml-xs">Inactive</q-badge>
          </q-item-label>
          <q-item-label caption>{{ captionFor(member) }}</q-item-label>
        </q-item-section>
        <q-item-section side>
          <!-- Their last role here IS their access to the tenant; ending that is a decision taken on their
               own page, where it says so. -->
          <q-btn
            flat round dense color="negative" icon="o_remove_circle_outline"
            :disable="member.isOnlyRole" @click="removeMember(member)"
          >
            <q-tooltip>
              {{ member.isOnlyRole ? "Their only role here — remove their access from the Users page" : "Remove" }}
            </q-tooltip>
          </q-btn>
        </q-item-section>
      </q-item>
      <q-item v-if="loading">
        <q-item-section class="text-grey-6">Loading…</q-item-section>
      </q-item>
      <q-item v-else-if="!members.length">
        <q-item-section class="text-grey-6">Nobody in this tenant holds this role.</q-item-section>
      </q-item>
    </q-list>

    <!-- Add Users dialog -->
    <q-dialog v-model="addOpen">
      <q-card style="min-width: 420px; max-width: 92vw;">
        <q-card-section class="text-h6">Add users to this role</q-card-section>
        <q-separator />
        <q-card-section>
          <app-select
            v-model="selectedToAdd" :options="candidateOptions" label="Users" multiple
            :loading="loadingCandidates"
            info="Everyone in this tenant who does not hold the role yet. Somebody with no role here at all gets their first one on the Users page."
          />
        </q-card-section>
        <q-separator />
        <q-card-actions align="right">
          <q-btn flat no-caps color="grey-8" label="Cancel" @click="addOpen = false" />
          <q-btn
            unelevated no-caps color="primary" label="Add" :loading="adding"
            :disable="!selectedToAdd.length" @click="confirmAdd"
          />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </q-card>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { roleApi, getApiErrorMessage } from "services/api";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import AppSelect from "components/common/AppSelect.vue";
import AppInfoTip from "components/common/AppInfoTip.vue";

// Who holds this role in the active tenant.
const props = defineProps({
  roleId: { type: String, default: null },
  roleName: { type: String, default: "" }
});

const emit = defineEmits(["loaded"]);

const notify = useNotify();
const { confirm } = useConfirm();

const members = ref([]);
const loading = ref(false);

const load = async () => {
  if (!props.roleId) {
    members.value = [];
    return;
  }
  loading.value = true;
  try {
    members.value = (await roleApi.users(props.roleId)) || [];
    // The page above shows the count in its summary; it comes from here rather than being fetched twice.
    emit("loaded", members.value.length);
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loading.value = false;
  }
};

watch(() => props.roleId, load, { immediate: true });

// Email first, then what else they hold here — the context for deciding whether this role can go.
const captionFor = (member) => {
  const parts = [];
  if (member.email) parts.push(member.email);
  parts.push(member.otherRoles?.length ? `also ${member.otherRoles.join(", ")}` : "only role in this tenant");
  return parts.join(" · ");
};

// ---- Add ----
const addOpen = ref(false);
const adding = ref(false);
const selectedToAdd = ref([]);
const candidates = ref([]);
const loadingCandidates = ref(false);

const candidateOptions = computed(() =>
  candidates.value.map((c) => ({ label: c.email ? `${c.displayName} (${c.email})` : c.displayName, value: c.userId })));

const openAdd = async () => {
  selectedToAdd.value = [];
  addOpen.value = true;
  loadingCandidates.value = true;
  try {
    candidates.value = (await roleApi.userCandidates(props.roleId)) || [];
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loadingCandidates.value = false;
  }
};

const confirmAdd = async () => {
  adding.value = true;
  try {
    const resp = await roleApi.addUsers(props.roleId, [...selectedToAdd.value]);
    addOpen.value = false;
    // The server's own words: it knows how many were granted, and when there was nothing to do.
    notify.success(resp?.message || "Role granted.");
  } catch (err) {
    // A capacity limit can stop part of a batch, and the message says which part — so reload either way.
    notify.error(getApiErrorMessage(err));
  } finally {
    adding.value = false;
    load();
  }
};

// ---- Remove ----
const removeMember = async (member) => {
  const ok = await confirm({
    title: "Remove role",
    message: `Take "${props.roleName}" away from ${member.displayName}? They keep their other roles here.`,
    confirmLabel: "Remove",
    type: "danger"
  });
  if (!ok) return;
  try {
    await roleApi.removeUser(props.roleId, member.userId);
    notify.success("Role removed.");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    load();
  }
};

defineExpose({ load, members, captionFor, openAdd, confirmAdd, removeMember, selectedToAdd });
</script>

<style scoped>
/* Matches the definition card beside it, so the column reads as one set of cards. */
.role-users-panel {
  border-radius: 12px;
}
</style>
