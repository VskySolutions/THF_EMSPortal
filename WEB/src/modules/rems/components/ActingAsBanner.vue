<template>
  <!-- Nothing to choose for almost everyone, so the control is absent rather than disabled. -->
  <div v-if="hasDelegations" class="aab" :class="{ 'aab--on': !!current }">
    <q-icon :name="current ? 'o_switch_account' : 'o_person'" size="18px" />
    <span class="aab__label">
      <template v-if="current">Working as <strong>{{ current.principalName }}</strong></template>
      <template v-else>Working as yourself</template>
    </span>
    <q-space />
    <q-btn
      flat dense no-caps size="sm" :color="current ? 'white' : 'primary'" icon-right="o_expand_more"
      :label="current ? 'Switch' : 'Act for someone'"
    >
      <q-menu>
        <q-list separator style="min-width: 220px;">
          <q-item clickable :active="!actingForId" @click="choose(null)">
            <q-item-section>
              <q-item-label>Myself</q-item-label>
              <q-item-label caption>Everything filed under your own name</q-item-label>
            </q-item-section>
          </q-item>
          <q-item
            v-for="o in options" :key="o.principalUserId"
            clickable :active="actingForId === o.principalUserId" @click="choose(o.principalUserId)"
          >
            <q-item-section>
              <q-item-label>{{ o.principalName }}</q-item-label>
              <!-- The two rights are genuinely different: preparing commits nothing, sending puts the firm
                   in front of a client. -->
              <q-item-label caption>
                {{ o.canSend ? "Prepare and send to the client" : "Prepare only — they send it" }}
              </q-item-label>
            </q-item-section>
          </q-item>
        </q-list>
      </q-menu>
    </q-btn>
  </div>
</template>

<script setup>
// The acting-as switch.
import { onMounted } from "vue";
import { useRemsActingAs } from "modules/rems/useRemsActingAs";

const { options, current, actingForId, hasDelegations, load, setActingFor } = useRemsActingAs();

// A page loaded in one seat is showing that seat's data, so switching reloads rather than leaving a list
// on screen that quietly belongs to the person they just stopped being.
const choose = (principalUserId) => {
  if (principalUserId === actingForId.value) return;
  setActingFor(principalUserId);
  window.location.reload();
};

onMounted(load);
</script>

<style scoped>
.aab {
  display: flex;
  align-items: center;
  /* Wraps rather than squeezing: a long principal name and the switch button do not both fit on one
     line of a phone, and this banner is the one thing on the page that must stay readable. */
  flex-wrap: wrap;
  gap: 8px;
  padding: 6px 8px 6px 12px;
  margin-bottom: 14px;
  border: 1px solid var(--line);
  border-radius: 8px;
  background: var(--white);
  font-size: 13px;
  color: var(--ink-700);
}
/* Loud on purpose while acting for somebody else: this is the one state where what you do lands under
   another person's name, and it should be impossible to forget you are in it. */
.aab--on {
  background: var(--teal-900);
  border-color: var(--teal-900);
  color: var(--white);
}
.aab__label { line-height: 1.2; }
</style>
