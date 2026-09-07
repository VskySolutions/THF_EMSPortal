<template>
  <span class="uf-marks">
    <!-- `type="a"` on both, as on every other control in an Actions column: the column renders one kind of
         tag throughout, whether the control goes somewhere or does something. -->
    <!-- Pin: a toggle, so one control rather than a menu item. -->
    <q-btn
      type="a"
      flat round dense
      :color="pinned ? 'primary' : 'grey-7'"
      icon="o_push_pin"
      :class="{ 'uf-marks__pin--on': pinned }"
      :loading="busy"
      :disable="!pinned && limitReached"
      @click.stop="$emit('toggle-pin')"
    >
      <q-tooltip>{{ pinTooltip }}</q-tooltip>
    </q-btn>

    <!-- Colour: a palette, so a menu. -->
    <q-btn type="a" flat round dense :color="colour ? undefined : 'grey-7'" icon="o_palette" :disable="busy">
      <q-icon v-if="colour" name="o_palette" class="uf-marks__swatch-icon" :style="{ color: colour }" />
      <q-tooltip>{{ colour ? "Change this row's colour" : "Colour this row (only you see it)" }}</q-tooltip>
      <q-menu anchor="bottom right" self="top right">
        <div class="uf-marks__palette">
          <div class="uf-marks__title">Colour this row</div>
          <div class="uf-marks__swatches">
            <button
              v-for="c in palette"
              :key="c"
              v-close-popup
              type="button"
              class="uf-marks__swatch"
              :class="{ 'uf-marks__swatch--on': c === colour }"
              :style="{ backgroundColor: c }"
              :aria-label="`Colour this row ${c}`"
              @click.stop="$emit('set-colour', c)"
            />
          </div>
          <q-btn
            v-close-popup
            flat dense no-caps size="sm" class="full-width q-mt-xs"
            :disable="!colour" icon="o_format_color_reset" label="Clear"
            @click.stop="$emit('set-colour', null)"
          />
          <div class="uf-marks__note">Private to you — nobody else sees it.</div>
        </div>
      </q-menu>
    </q-btn>
  </span>
</template>

<script setup>
// The two PERSONAL marks on a list row: pin it to the top, or tint it.
import { computed } from "vue";

const props = defineProps({
  pinned: { type: Boolean, default: false },
  // The row's current tint, or null.
  colour: { type: String, default: null },
  palette: { type: Array, default: () => [] },
  // The user is at the server's per-type pin cap. An already-pinned row can always be UNpinned.
  limitReached: { type: Boolean, default: false },
  limit: { type: Number, default: 5 },
  busy: { type: Boolean, default: false }
});

defineEmits(["toggle-pin", "set-colour"]);

// Says what the click will do, and where a disabled one leaves the reader — a greyed pin with no
// explanation reads as a bug.
const pinTooltip = computed(() => {
  if (props.pinned) return "Unpin — stops holding this at the top for you";
  if (props.limitReached) return `You have pinned ${props.limit} already. Unpin one to pin this.`;
  return "Pin to the top of this page (only for you)";
});
</script>

<style scoped>
/* Inline-flex, so the two marks sit beside the row's other actions rather than after a break — and
   middle-aligned, so their icons share a centre line with the buttons either side of them instead of
   hanging off a baseline this box computes from its own first child. */
.uf-marks {
  display: inline-flex;
  align-items: center;
  vertical-align: middle;
}
/* A pinned row's icon leans in, the way a pushed pin does. It is the difference between the two states
   at a glance, on top of the colour. */
.uf-marks__pin--on :deep(.q-icon) {
  transform: rotate(-35deg);
}
/* The coloured icon sits over the grey one so the button keeps its own metrics and ripple. */
.uf-marks__swatch-icon {
  position: absolute;
}
.uf-marks__palette {
  padding: 10px;
  width: 184px;
}
.uf-marks__title {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.3px;
  text-transform: uppercase;
  color: #7b8794;
  margin-bottom: 8px;
}
.uf-marks__swatches {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 6px;
}
.uf-marks__swatch {
  height: 28px;
  border: 0;
  border-radius: 6px;
  padding: 0;
  cursor: pointer;
  outline: 2px solid transparent;
  outline-offset: 2px;
}
.uf-marks__swatch:hover {
  outline-color: rgba(0, 0, 0, 0.15);
}
.uf-marks__swatch--on {
  outline-color: var(--q-primary);
}
/* Said once, in the place the choice is made: a colour looks like shared data until somebody tells you
   it is not. */
.uf-marks__note {
  margin-top: 8px;
  font-size: 10.5px;
  line-height: 14px;
  color: #8895a4;
}
</style>
