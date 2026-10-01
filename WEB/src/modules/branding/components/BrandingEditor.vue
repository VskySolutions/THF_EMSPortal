<template>
  <div>
    <div v-if="loading" class="row flex-center q-pa-xl">
      <q-spinner color="primary" size="40px" />
    </div>

    <div v-else class="row q-col-gutter-md">
      <!-- The settings -->
      <div class="col-12 col-md-7">
        <q-card flat bordered>
          <q-tabs
            v-model="tab" dense no-caps align="left" active-color="primary" indicator-color="primary"
            class="text-grey-8" outside-arrows mobile-arrows
          >
            <q-tab v-for="s in SECTIONS" :key="s.name" :name="s.name" :icon="s.icon" :label="s.label" />
          </q-tabs>
          <q-separator />

          <q-tab-panels v-model="tab" animated keep-alive>
            <!-- Identity -->
            <q-tab-panel name="identity">
              <div class="row q-col-gutter-md">
                <app-text-field
                  v-model="draft.identity.applicationName" label="Application name" class="col-12 col-sm-6"
                  :placeholder="DEFAULT_THEME.identity.applicationName" maxlength="60"
                  hint="Beside the logo, in the browser tab and on the sign-in screen."
                />
                <app-text-field
                  v-model="draft.identity.tagline" label="Tagline" class="col-12 col-sm-6" maxlength="120"
                  hint="A line under the name on the sign-in screen."
                />
                <app-text-field
                  v-model="draft.identity.footerText" label="Footer text" class="col-12" maxlength="200"
                  hint="Replaces the copyright line at the foot of every page."
                />
                <app-text-field
                  v-model="draft.identity.supportEmail" label="Support email" class="col-12 col-sm-6" maxlength="200"
                  hint="Offered on the sign-in screen to somebody who cannot get in."
                />
                <app-text-field
                  v-model="draft.identity.supportUrl" label="Support link" class="col-12 col-sm-6" maxlength="300"
                  placeholder="https://" hint="A help page, also offered on the sign-in screen. https only."
                />
              </div>
            </q-tab-panel>

            <!-- Logos and images -->
            <q-tab-panel name="images">
              <p class="text-body2 text-grey-8">
                PNG, JPEG, WebP or ICO. An image is saved as soon as it is uploaded — it does not wait for Save.
              </p>
              <div class="column q-gutter-md">
                <branding-asset-slot
                  v-for="slot in ASSET_SLOTS" :key="slot.name"
                  :label="slot.label" :hint="slot.hint" :icon="slot.icon" :shape="slot.shape"
                  :max-size-mb="slot.maxSizeMb" :backdrop="slot.dark ? resolved.colors.primary : '#ffffff'"
                  :url="assetUrl(assets[slot.name])" :busy="assetBusy === slot.name" :disable="!!assetBusy"
                  @upload="uploadAsset(slot.name, $event)" @remove="removeAsset(slot)"
                />
              </div>
              <q-separator class="q-my-md" />
              <div class="row q-col-gutter-md items-end">
                <branding-number-field
                  v-model="draft.identity.logoHeight" label="Logo height in the side menu" suffix="px" class="col-12 col-sm-5"
                  :min="20" :max="64" :fallback="DEFAULT_THEME.identity.logoHeight"
                />
                <div class="col-12 col-sm-7">
                  <q-toggle
                    :model-value="draft.identity.showNameBesideLogo !== false" label="Show the application name beside the logo"
                    @update:model-value="draft.identity.showNameBesideLogo = $event ? null : false"
                  />
                  <div class="text-caption text-grey-7">Turn off when the logo already spells the name out.</div>
                </div>
              </div>
            </q-tab-panel>

            <!-- Colours -->
            <q-tab-panel name="colors">
              <div class="section-subhead q-mt-none">Start from a scheme</div>
              <div class="row q-gutter-sm q-mb-sm">
                <q-btn
                  v-for="p in COLOUR_PRESETS" :key="p.name" outline dense no-caps color="grey-8" :label="p.name"
                  @click="applyPreset(p)"
                >
                  <span class="brand-preset-dot q-mr-xs" :style="{ background: p.swatch }" />
                </q-btn>
              </div>
              <div class="text-caption text-grey-7 q-mb-md">
                A scheme sets the three brand colours. Tints, the selected menu item, links and headings follow the
                primary colour unless you give them a colour of their own below.
              </div>

              <template v-for="group in COLOUR_GROUPS" :key="group.label">
                <div class="section-subhead">{{ group.label }}</div>
                <div class="row q-col-gutter-md">
                  <branding-color-field
                    v-for="f in group.fields" :key="f.key" v-model="draft.colors[f.key]"
                    :label="f.label" :info="f.info" :fallback="resolved.colors[f.key]" class="col-12 col-sm-6"
                  />
                </div>
              </template>
            </q-tab-panel>

            <!-- Typography -->
            <q-tab-panel name="typography">
              <div class="row q-col-gutter-md">
                <app-select
                  v-model="draft.typography.bodyFont" :options="FONT_OPTIONS" label="Body font" class="col-12 col-sm-6"
                  :hint="`Default: ${DEFAULT_THEME.typography.bodyFont}`"
                />
                <app-select
                  v-model="draft.typography.headingFont" :options="FONT_OPTIONS" label="Heading font" class="col-12 col-sm-6"
                  :hint="`Default: ${DEFAULT_THEME.typography.headingFont}`"
                />
                <branding-number-field
                  v-model="draft.typography.baseFontSize" label="Body text size" suffix="px" class="col-12 col-sm-6"
                  :min="12" :max="18" :fallback="DEFAULT_THEME.typography.baseFontSize"
                />
                <branding-color-field
                  v-model="draft.typography.headingColor" label="Heading colour" class="col-12 col-sm-6"
                  :fallback="resolved.typography.headingColor" info="Used by every heading that has no colour of its own."
                />
              </div>

              <div class="section-subhead">Headings</div>
              <q-list bordered separator class="rounded-borders">
                <q-expansion-item v-for="h in HEADING_KEYS" :key="h" group="headings" dense-toggle>
                  <template #header>
                    <q-item-section side class="text-weight-bold text-grey-9" style="min-width: 34px;">{{ h.toUpperCase() }}</q-item-section>
                    <q-item-section>
                      <q-item-label caption>
                        {{ resolved.typography[h].size }}px · weight {{ resolved.typography[h].weight }}
                        <span v-if="headingChanged(h)" class="text-primary"> · customised</span>
                      </q-item-label>
                    </q-item-section>
                  </template>
                  <div class="row q-col-gutter-md q-pa-md">
                    <branding-number-field
                      v-model="draft.typography[h].size" label="Size" suffix="px" class="col-6 col-sm-4"
                      :min="10" :max="72" :fallback="DEFAULT_THEME.typography[h].size"
                    />
                    <app-select
                      v-model="draft.typography[h].weight" :options="WEIGHT_OPTIONS" label="Weight" class="col-6 col-sm-4"
                      :hint="`Default: ${DEFAULT_THEME.typography[h].weight}`"
                    />
                    <app-select
                      v-model="draft.typography[h].transform" :options="TRANSFORM_OPTIONS" label="Letter case" class="col-6 col-sm-4"
                      hint="Default: as typed"
                    />
                    <branding-number-field
                      v-model="draft.typography[h].lineHeight" label="Line height" class="col-6 col-sm-4"
                      :min="1" :max="2" :step="0.05" :fallback="DEFAULT_THEME.typography[h].lineHeight"
                    />
                    <branding-number-field
                      v-model="draft.typography[h].letterSpacing" label="Letter spacing" suffix="em" class="col-6 col-sm-4"
                      :min="-0.05" :max="0.3" :step="0.01" :fallback="DEFAULT_THEME.typography[h].letterSpacing"
                    />
                    <branding-color-field
                      v-model="draft.typography[h].color" label="Colour" class="col-6 col-sm-4"
                      :fallback="resolved.typography.headingColor"
                    />
                  </div>
                </q-expansion-item>
              </q-list>
            </q-tab-panel>

            <!-- Buttons -->
            <q-tab-panel name="buttons">
              <div class="row q-col-gutter-md">
                <branding-number-field
                  v-model="draft.buttons.radius" label="Corner radius" suffix="px" class="col-12 col-sm-4"
                  :min="0" :max="28" :fallback="DEFAULT_THEME.buttons.radius"
                />
                <app-select
                  v-model="draft.buttons.fontWeight" :options="BUTTON_WEIGHT_OPTIONS" label="Text weight" class="col-12 col-sm-4"
                  :hint="`Default: ${DEFAULT_THEME.buttons.fontWeight}`"
                />
                <div class="col-12 col-sm-4 self-end">
                  <q-toggle
                    :model-value="draft.buttons.uppercase === true" label="UPPERCASE labels"
                    @update:model-value="draft.buttons.uppercase = $event ? true : null"
                  />
                </div>
              </div>

              <div class="section-subhead">Primary button</div>
              <div class="text-caption text-grey-7 q-mb-sm">The filled button for a screen's main action: Save, Add, Login.</div>
              <div class="row q-col-gutter-md">
                <branding-color-field
                  v-model="draft.buttons.primary.background" label="Background" class="col-12 col-sm-4"
                  :fallback="resolved.buttons.primary.background" info="Follows the primary colour unless set."
                />
                <branding-color-field
                  v-model="draft.buttons.primary.text" label="Text" class="col-12 col-sm-4"
                  :fallback="resolved.buttons.primary.text"
                />
                <branding-color-field
                  v-model="draft.buttons.primary.border" label="Border" class="col-12 col-sm-4"
                  :fallback="resolved.buttons.primary.background"
                />
              </div>

              <div class="section-subhead">Secondary button</div>
              <div class="text-caption text-grey-7 q-mb-sm">The outlined button beside it: Filters, Test connection, Upload.</div>
              <div class="row q-col-gutter-md">
                <branding-color-field
                  v-model="draft.buttons.secondary.background" label="Background" class="col-12 col-sm-4"
                  info="Transparent unless set."
                />
                <branding-color-field
                  v-model="draft.buttons.secondary.text" label="Text" class="col-12 col-sm-4"
                  :fallback="resolved.buttons.secondary.text" info="Follows the primary colour unless set."
                />
                <branding-color-field
                  v-model="draft.buttons.secondary.border" label="Border" class="col-12 col-sm-4"
                  :fallback="resolved.buttons.secondary.border" info="Follows the text colour unless set."
                />
              </div>
            </q-tab-panel>

            <!-- Shape -->
            <q-tab-panel name="shape">
              <div class="row q-col-gutter-md">
                <branding-number-field
                  v-model="draft.shape.cardRadius" label="Card corner radius" suffix="px" class="col-12 col-sm-4"
                  :min="0" :max="24" :fallback="DEFAULT_THEME.shape.cardRadius"
                />
                <branding-number-field
                  v-model="draft.shape.inputRadius" label="Field corner radius" suffix="px" class="col-12 col-sm-4"
                  :min="0" :max="16" :fallback="DEFAULT_THEME.shape.inputRadius"
                />
                <app-select
                  v-model="draft.shape.cardShadow" :options="SHADOW_OPTIONS" label="Card shadow" class="col-12 col-sm-4"
                  hint="Default: Soft"
                />
              </div>
            </q-tab-panel>

            <!-- Sign-in screen -->
            <q-tab-panel name="login">
              <div class="row q-col-gutter-md">
                <app-text-field
                  v-model="draft.login.headline" label="Headline" class="col-12 col-sm-6" maxlength="80"
                  :placeholder="DEFAULT_THEME.login.headline"
                />
                <branding-color-field
                  v-model="draft.login.backgroundColor" label="Background colour" class="col-12 col-sm-6"
                  :fallback="resolved.login.backgroundColor"
                />
                <app-text-field
                  v-model="draft.login.subtext" label="Text under the headline" class="col-12" maxlength="160"
                  :placeholder="DEFAULT_THEME.login.subtext"
                />
              </div>
              <div class="text-caption text-grey-7 q-mt-md">
                The background image is with the other images, under Logos. Colleagues see this screen branded once
                they have signed in on a browser; a link of the form
                <code>{{ loginLink }}</code> shows it branded to somebody who never has.
              </div>
            </q-tab-panel>
          </q-tab-panels>

          <q-separator />
          <q-card-actions class="q-gutter-sm bg-grey-1">
            <q-btn
              v-if="isCustomised" flat no-caps color="negative" icon="o_restart_alt" label="Reset to default"
              :disable="busy" @click="reset"
            />
            <q-space />
            <q-btn v-if="dirty" flat no-caps color="grey-8" label="Discard changes" :disable="busy" @click="discard" />
            <q-btn unelevated no-caps color="primary" label="Save" :loading="saving" :disable="busy || !dirty" @click="save" />
          </q-card-actions>
        </q-card>

        <div v-if="updatedLine" class="text-caption text-grey-7 q-mt-sm">{{ updatedLine }}</div>
      </div>

      <!-- What it looks like -->
      <div class="col-12 col-md-5">
        <q-card flat bordered class="brand-editor__preview">
          <q-card-section class="row items-center q-py-sm">
            <div class="text-subtitle2 text-primary"><q-icon name="o_visibility" size="18px" class="q-mr-xs" />Preview</div>
            <q-space />
            <q-toggle v-model="tryOnApp" dense label="Try it on this application">
              <q-tooltip max-width="260px">
                Dresses the whole application in the draft, in this browser only, until you leave the page. Nothing is saved.
              </q-tooltip>
            </q-toggle>
          </q-card-section>
          <q-separator />
          <q-card-section>
            <branding-preview
              :theme="resolved" :logo="assetUrl(assets.logo)" :login-background="assetUrl(assets.loginBackground)"
            />
          </q-card-section>
        </q-card>
      </div>
    </div>
  </div>
</template>

<script setup>
// A tenant's branding, edited. One component for both doors onto it: Tenant Settings → Branding (the
// tenant the user is working in) and the Tenants screen (whichever tenant is open there).
import { ref, computed, watch, onMounted, onBeforeUnmount } from "vue";
import { onBeforeRouteLeave } from "vue-router";
import { brandingApi, getApiErrorMessage, webUrl } from "services/api";
import {
  COLOUR_PRESETS, DEFAULT_THEME, FONT_OPTIONS, HEADING_KEYS, assetUrl, emptyTheme, resolveTheme, toDraft
} from "services/branding";
import { useBrandingStore } from "stores/branding";
import { useNotify } from "composables/useNotify";
import { useConfirm } from "composables/useConfirm";
import { useDateFormat } from "composables/useDateFormat";
import { useTenantScope } from "composables/useTenantScope";

import AppTextField from "components/common/AppTextField.vue";
import AppSelect from "components/common/AppSelect.vue";
import BrandingColorField from "modules/branding/components/BrandingColorField.vue";
import BrandingNumberField from "modules/branding/components/BrandingNumberField.vue";
import BrandingAssetSlot from "modules/branding/components/BrandingAssetSlot.vue";
import BrandingPreview from "modules/branding/components/BrandingPreview.vue";

const props = defineProps({
  // The tenant to edit. Left out, it is the tenant the user is working in.
  tenantId: { type: String, default: null }
});

const notify = useNotify();
const { confirm } = useConfirm();
const fmt = useDateFormat();
const brandingStore = useBrandingStore();
const { selectedTenantId } = useTenantScope();

// ---- What the form offers ----
const SECTIONS = [
  { name: "identity", label: "Identity", icon: "o_badge" },
  { name: "images", label: "Logos", icon: "o_image" },
  { name: "colors", label: "Colours", icon: "o_palette" },
  { name: "typography", label: "Typography", icon: "o_text_fields" },
  { name: "buttons", label: "Buttons", icon: "o_smart_button" },
  { name: "shape", label: "Shape", icon: "o_rounded_corner" },
  { name: "login", label: "Sign-in screen", icon: "o_login" }
];

const ASSET_SLOTS = [
  { name: "logo", label: "Logo", icon: "o_image", shape: "wide", maxSizeMb: 2, hint: "Side menu and sign-in screen. A transparent PNG about 400 px wide works best." },
  { name: "logoDark", label: "Logo for dark backgrounds", icon: "o_image", shape: "wide", maxSizeMb: 2, dark: true, hint: "The header of the form clients fill in. Falls back to the logo above." },
  { name: "logoMark", label: "Compact mark", icon: "o_crop_square", shape: "square", maxSizeMb: 2, hint: "Shown alone when the side menu is collapsed. Square, about 128 px." },
  { name: "favicon", label: "Browser tab icon", icon: "o_tab", shape: "square", maxSizeMb: 2, hint: "Square PNG or ICO, 32 px or larger." },
  { name: "loginBackground", label: "Sign-in background", icon: "o_wallpaper", shape: "wide", maxSizeMb: 5, hint: "Fills the screen behind the sign-in card. Up to 5 MB." }
];

const COLOUR_GROUPS = [
  {
    label: "Brand",
    fields: [
      { key: "primary", label: "Primary", info: "Main buttons, links, headings, the selected menu item and every tint derived from them." },
      { key: "secondary", label: "Secondary", info: "Quiet supporting elements." },
      { key: "accent", label: "Accent", info: "Highlights, used sparingly." }
    ]
  },
  {
    label: "Status",
    fields: [
      { key: "positive", label: "Positive" },
      { key: "negative", label: "Negative" },
      { key: "warning", label: "Warning" },
      { key: "info", label: "Info" }
    ]
  },
  {
    label: "Surfaces and text",
    fields: [
      { key: "pageBackground", label: "Page background" },
      { key: "surface", label: "Cards" },
      { key: "border", label: "Borders and dividers" },
      { key: "text", label: "Text" },
      { key: "textMuted", label: "Muted text" },
      { key: "link", label: "Links" }
    ]
  },
  {
    label: "Header and side menu",
    fields: [
      { key: "headerBackground", label: "Header background" },
      { key: "headerText", label: "Header text" },
      { key: "sidebarBackground", label: "Menu background" },
      { key: "sidebarText", label: "Menu text" },
      { key: "sidebarActiveBackground", label: "Selected item background" },
      { key: "sidebarActiveText", label: "Selected item text" }
    ]
  },
  {
    label: "Tables",
    fields: [
      { key: "tableHeaderBackground", label: "Header row background" },
      { key: "tableHeaderText", label: "Header row text" }
    ]
  }
];

const WEIGHT_OPTIONS = [300, 400, 500, 600, 700, 800, 900].map((w) => ({ label: String(w), value: w }));
const BUTTON_WEIGHT_OPTIONS = WEIGHT_OPTIONS.filter((w) => w.value >= 400 && w.value <= 700);
const TRANSFORM_OPTIONS = [
  { label: "As typed", value: "none" },
  { label: "UPPERCASE", value: "uppercase" },
  { label: "Capitalised", value: "capitalize" }
];
const SHADOW_OPTIONS = [
  { label: "None", value: "none" },
  { label: "Soft", value: "soft" },
  { label: "Raised", value: "raised" }
];

// ---- State ----
const tab = ref("identity");
const loading = ref(true);
const saving = ref(false);
const resetting = ref(false);
const assetBusy = ref(null);
const busy = computed(() => saving.value || resetting.value || !!assetBusy.value);

// The form: every setting present, null where the tenant has set nothing.
const draft = ref(emptyTheme());
const savedSnapshot = ref(JSON.stringify(draft.value));
const dirty = computed(() => JSON.stringify(draft.value) !== savedSnapshot.value);

// What the API last said: the images, and who changed the branding last.
const saved = ref(null);
const assets = computed(() => saved.value?.assets || {});
const isCustomised = computed(() => !!saved.value?.isCustomised);

const resolved = computed(() => resolveTheme(draft.value));

const updatedLine = computed(() => {
  const s = saved.value;
  if (!s?.updatedOnUtc) return "";
  return `Last changed ${fmt.formatDateTime(s.updatedOnUtc)}${s.updatedByName ? ` by ${s.updatedByName}` : ""}.`;
});

const loginLink = computed(() => webUrl(`/auth/login?tenant=${saved.value?.tenantIdentifier || "your-tenant"}`));

const headingChanged = (h) => Object.values(draft.value.typography[h]).some((v) => v !== null);

// Whether the tenant being edited is the one this browser is dressed as — only then does saving change
// the page around the editor.
const isTenantInView = computed(() => !props.tenantId || props.tenantId === selectedTenantId.value);

const adopt = (response) => {
  saved.value = response;
  draft.value = toDraft(response?.theme);
  savedSnapshot.value = JSON.stringify(draft.value);
  if (isTenantInView.value) brandingStore.set(response);
  retry();
};

const load = async () => {
  loading.value = true;
  try {
    adopt(await brandingApi.get(props.tenantId || undefined));
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    loading.value = false;
  }
};

// ---- Theme ----
const save = async () => {
  saving.value = true;
  try {
    adopt(await brandingApi.save(draft.value, props.tenantId || undefined));
    notify.success("Branding saved.");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    saving.value = false;
  }
};

const discard = () => { draft.value = toDraft(saved.value?.theme); };

const reset = async () => {
  const ok = await confirm({
    title: "Reset branding",
    message: "Put this tenant back on the default look? Its colours, fonts, text and uploaded images are all removed.",
    confirmLabel: "Reset",
    type: "danger"
  });
  if (!ok) return;
  resetting.value = true;
  try {
    adopt(await brandingApi.reset(props.tenantId || undefined));
    notify.success("Branding reset to the default.");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    resetting.value = false;
  }
};

const applyPreset = (preset) => {
  draft.value.colors.primary = preset.primary;
  draft.value.colors.secondary = preset.secondary;
  draft.value.colors.accent = preset.accent;
};

// ---- Images ----
// Saved on their own, straight away: the response replaces the images held here and leaves the draft
// alone, so an upload never costs the user their unsaved colours.
const adoptAssets = (response) => {
  saved.value = { ...response, theme: saved.value?.theme || response.theme };
  if (isTenantInView.value) brandingStore.set({ theme: saved.value.theme, assets: response.assets });
  retry();
};

const uploadAsset = async (slot, file) => {
  assetBusy.value = slot;
  try {
    adoptAssets(await brandingApi.uploadAsset(slot, file, props.tenantId || undefined));
    notify.success("Image uploaded.");
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    assetBusy.value = null;
  }
};

const removeAsset = async (slot) => {
  const ok = await confirm({
    title: "Remove image",
    message: `Remove the ${slot.label.toLowerCase()}?`,
    confirmLabel: "Remove",
    type: "danger"
  });
  if (!ok) return;
  assetBusy.value = slot.name;
  try {
    adoptAssets(await brandingApi.removeAsset(slot.name, props.tenantId || undefined));
  } catch (err) {
    notify.error(getApiErrorMessage(err));
  } finally {
    assetBusy.value = null;
  }
};

// ---- Trying the draft on the real page ----
const tryOnApp = ref(false);
// Adopting a response re-dresses the page in what was SAVED; a draft being tried on goes back on top.
const retry = () => { if (tryOnApp.value) brandingStore.preview(draft.value, assets.value); };
watch([tryOnApp, draft], () => {
  if (tryOnApp.value) brandingStore.preview(draft.value, assets.value);
  else brandingStore.endPreview();
}, { deep: true });

// ---- Lifecycle ----
// With no tenant named, the form follows the tenant in view (the settings page under a Super Admin's scope).
const onTenantSwitched = () => { if (!props.tenantId) load(); };

onMounted(() => {
  load();
  window.addEventListener("tenant-switched", onTenantSwitched);
});

onBeforeUnmount(() => {
  window.removeEventListener("tenant-switched", onTenantSwitched);
  brandingStore.endPreview();
});

onBeforeRouteLeave(async () => {
  if (!dirty.value) return true;
  return confirm({
    title: "Unsaved changes",
    message: "Leave without saving the branding changes?",
    confirmLabel: "Leave",
    type: "danger"
  });
});
</script>

<style scoped>
.brand-preset-dot {
  display: inline-block;
  width: 12px;
  height: 12px;
  border-radius: 50%;
  order: -1;
}
/* Stays in sight while the settings beside it scroll. */
@media (min-width: 1024px) {
  .brand-editor__preview {
    position: sticky;
    top: 62px;
  }
}
</style>
