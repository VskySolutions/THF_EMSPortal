<template>
  <q-card flat bordered class="app-detail-header q-mb-md">
    <!-- Crumbs on the left and the actions on the right — on one line where there is width for it, and on
         two once there is not. -->
    <q-card-section class="app-detail-header__bar q-py-sm">
      <app-breadcrumbs :items="items" no-margin class="app-detail-header__crumbs" />
      <!-- The page's own actions and Back travel as one group, so they wrap onto a second line together and
           stay right-aligned there rather than Back dropping away from the buttons it belongs with. -->
      <div class="app-detail-header__actions">
        <slot name="actions" />
        <q-btn outline no-caps color="primary" icon="o_arrow_back" label="Back" @click="goBack" />
      </div>
    </q-card-section>
  </q-card>
</template>

<script setup>
// Standard header for internal view/manage (detail) pages: breadcrumbs on the left, a Back button on the
// right (plus an optional `actions` slot for status badges/controls).
import { useRouter } from "vue-router";
import AppBreadcrumbs from "components/common/AppBreadcrumbs.vue";

const props = defineProps({
  items: { type: Array, default: () => [] },
  // FALLBACK destination only — see goBack. Back returns to wherever the user actually came from; this
  // is where it lands when there is no such page.
  backTo: { type: [String, Object], default: null }
});

const router = useRouter();

// Back means "the page I came from".
const goBack = () => {
  if (router.options.history.state?.back) router.back();
  else if (props.backTo) router.push(props.backTo);
  else router.push("/");
};
</script>

<style scoped>
.app-detail-header {
  border-radius: 12px;
}
/* gap rather than margin utilities on the children: a wrapped second line then sits the same distance
   from the first as the buttons do from each other. */
.app-detail-header__bar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px 12px;
}
/* min-width:0 lets a long trail of crumbs shrink and ellipsize instead of forcing the actions onto a
   line of their own while there is still room beside them. */
.app-detail-header__crumbs {
  flex: 1 1 auto;
  min-width: 0;
}
.app-detail-header__actions {
  /* Two sizes, because this row holds two KINDS of thing: things to press and things to read. Both are
     inherited, so a page putting something of its own in the slot sizes it off the same numbers rather
     than guessing at them. */
  --dh-action-height: 36px;
  --dh-status-height: 24px;
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 8px;
  margin-left: auto;
  min-width: 0;
}

/* One height for everything that DOES something — a button is 36px, a dense round icon button 34 and a
   round one 42, and side by side on one line that reads as a ragged row rather than as a set of actions.
   :deep, because these controls belong to the page and reach this row through the slot. */
.app-detail-header__actions :deep(.q-btn) {
  min-height: var(--dh-action-height);
}
/* A round button stays round: a height on its own would stretch it into an ellipse. */
.app-detail-header__actions :deep(.q-btn--round) {
  width: var(--dh-action-height);
  min-width: var(--dh-action-height);
  height: var(--dh-action-height);
}

/* A deliberately different shape for the things that only SAY something. Held to the buttons' height and
   squared off like them, a status reads as one more thing to press — and it is not: it is the record
   saying where it stands. So it stays short, rounds into a pill and drops a step in letter size, a
   caption beside the buttons rather than another of them, on the same centre line as them.
   A floating badge is exempt: that one is a counter pinned to the corner of a button, not a status
   standing beside it. */
.app-detail-header__actions :deep(.q-chip),
.app-detail-header__actions :deep(.q-badge:not(.q-badge--floating)) {
  display: inline-flex;
  align-items: center;
  min-height: var(--dh-status-height);
  /* A chip brings a height and 4px of margin of its own; the second would double the row's gap. */
  height: auto;
  margin: 0;
  padding: 3px 10px;
  border-radius: 999px;
  font-size: 11.5px;
  font-weight: 600;
  line-height: 1.35;
  letter-spacing: 0.02em;
  box-shadow: none;
}
</style>
