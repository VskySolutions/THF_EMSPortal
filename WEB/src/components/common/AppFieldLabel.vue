<template>
  <div v-if="text" class="app-field-label">
    {{ text }}<span v-if="isRequired" class="app-field-label__star" aria-hidden="true">*</span>
    <!-- Explains a rule the control itself cannot show — which group or role a picker is scoped to, where
         its values come from. -->
    <q-icon v-if="info" name="o_info" size="14px" color="grey-6" class="app-field-label__info">
      <q-tooltip anchor="top middle" self="bottom middle" max-width="280px">{{ info }}</q-tooltip>
    </q-icon>
  </div>
</template>

<script setup>
// Standard external field label: sits at the top-left above the input (not inside it).
import { toRef } from "vue";
import { useFieldLabel } from "composables/useFieldLabel";

const props = defineProps({
  label: { type: String, default: "" },
  required: { type: Boolean, default: false },
  // Why this field offers what it offers (group/role scoping, where the values are maintained). Shown on
  // an info icon beside the label rather than as a caption, so the field keeps its height.
  info: { type: String, default: "" }
});

const { text, isRequired } = useFieldLabel(toRef(props, "label"), toRef(props, "required"));
</script>

<style scoped>
.app-field-label {
  font-size: 12px;
  font-weight: 500;
  color: #423939;
  line-height: 1.2;
  margin-bottom: 4px;
}
.app-field-label__info {
  margin-left: 4px;
  cursor: help;
  vertical-align: text-bottom;
}
/* Mandatory marker: bigger than the label text and red, so required fields stand out.

   line-height:0 is load-bearing, not tidying. An inline child contributes its own leading box to the
   line, and an 18px box in a 12px/1.2 line grew the label from 14.39px to 18.39px — so a required
   field's input sat four pixels BELOW the optional field beside it, on every row in the app that mixes
   the two. "Suffix" next to "First Name *" was the pair where it showed. Zero leading gives the marker
   no box of its own; the glyph still paints at full size, and baseline alignment puts it exactly where
   text-bottom did. */
.app-field-label__star {
  margin-left: 3px;
  color: #e53935;
  font-size: 18px;
  font-weight: 700;
  line-height: 0;
  vertical-align: baseline;
}
</style>
