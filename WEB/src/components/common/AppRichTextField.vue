<template>
  <div class="app-field">
    <app-field-label :label="label" :required="required" />
    <div v-if="!readonly && !disable" class="app-rich-text">
      <ckeditor v-model="model" :editor="ClassicEditor" :config="config" @ready="onReady" />
    </div>
    <!-- Read-only renders the stored markup rather than a disabled editor: a greyed-out toolbar over
         content nobody can change is just noise. -->
    <!-- eslint-disable-next-line vue/no-v-html -->
    <div v-else-if="rendered" class="app-rich-text__readonly ck-content" v-html="rendered" />
    <div v-else class="app-rich-text__readonly text-grey-6">{{ placeholder || "—" }}</div>
    <div v-if="hint" class="app-field__hint text-caption text-grey-7">{{ hint }}</div>
  </div>
</template>

<script setup>
// Rich-text counterpart to AppTextField, for the long-form description fields. The value is HTML, stored
// as the editor produces it; `renderRichText` (utils/richText) is what sanitizes it on the way back out.
import { computed, toRef } from "vue";
import { Ckeditor } from "@ckeditor/ckeditor5-vue";
import {
  ClassicEditor, Essentials, Paragraph, Bold, Italic, Underline, Strikethrough,
  Link, List, BlockQuote, RemoveFormat, Undo
} from "ckeditor5";
import "ckeditor5/ckeditor5.css";
import AppFieldLabel from "components/common/AppFieldLabel.vue";
import { useFieldLabel } from "composables/useFieldLabel";
import { renderRichText } from "utils/richText";

const props = defineProps({
  modelValue: { type: String, default: "" },
  label: { type: String, default: "" },
  required: { type: Boolean, default: false },
  placeholder: { type: String, default: "" },
  hint: { type: String, default: "" },
  readonly: { type: Boolean, default: false },
  disable: { type: Boolean, default: false },
  // Extra CKEditor config merged over the defaults (e.g. a different toolbar for one field).
  editorConfig: { type: Object, default: () => ({}) }
});

const emit = defineEmits(["update:modelValue"]);

const model = computed({
  get: () => props.modelValue || "",
  set: (val) => emit("update:modelValue", val)
});

// Deliberately small plugin set — these are descriptions, not documents. Adding a toolbar button means
// adding its plugin here too; CKEditor throws on a toolbar item with no matching plugin.
const config = computed(() => ({
  // CKEditor 5 v44+ requires a licence key. "GPL" is the open-source key and obliges GPL-compatible
  // distribution — swap in the commercial key here if this ships as a proprietary product.
  licenseKey: "GPL",
  plugins: [
    Essentials, Paragraph, Bold, Italic, Underline, Strikethrough,
    Link, List, BlockQuote, RemoveFormat, Undo
  ],
  toolbar: {
    items: [
      "undo", "redo", "|",
      "bold", "italic", "underline", "strikethrough", "|",
      "bulletedList", "numberedList", "blockQuote", "|",
      "link", "removeFormat"
    ],
    // Wrap onto a second row rather than fold what does not fit into CKEditor's "Show more items" (⋮)
    // menu.
    shouldNotGroupWhenFull: true
  },
  link: {
    addTargetToExternalLinks: true,
    defaultProtocol: "https://"
  },
  placeholder: props.placeholder,
  ...props.editorConfig
}));

const rendered = computed(() => renderRichText(props.modelValue));

// The visible label sits outside the control (AppFieldLabel), so name the editable itself the way the
// other App* fields do — otherwise a screen reader only hears CKEditor's generic "Rich Text Editor".
const { text: ariaLabel } = useFieldLabel(toRef(props, "label"), toRef(props, "required"));

const onReady = (editor) => {
  const view = editor.editing.view;
  view.change((writer) => writer.setAttribute("aria-label", ariaLabel.value, view.document.getRoot()));
};
</script>

<style scoped>
/* Quasar's outlined fields set the visual language here; match their border/radius so the editor does not
   read as a foreign control sitting between two q-inputs. */
.app-rich-text :deep(.ck.ck-editor__editable) {
  min-height: 8rem;
  max-height: 22rem;
  overflow-y: auto;
}
.app-rich-text :deep(.ck.ck-editor__editable.ck-focused) {
  border-color: var(--q-primary);
  box-shadow: none;
}
.app-rich-text :deep(.ck.ck-toolbar),
.app-rich-text :deep(.ck.ck-editor__editable) {
  border-color: rgba(0, 0, 0, 0.24);
}
.app-rich-text__readonly {
  padding: 6px 0;
  word-break: break-word;
}
.app-rich-text__readonly :deep(p) {
  margin: 0 0 0.5em;
}
.app-rich-text__readonly :deep(p:last-child) {
  margin-bottom: 0;
}
.app-rich-text__readonly :deep(ul),
.app-rich-text__readonly :deep(ol) {
  margin: 0 0 0.5em;
  padding-left: 1.25rem;
}
</style>
