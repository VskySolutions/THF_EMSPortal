<template>
  <div class="brand-preview" :style="{ fontFamily: bodyFont, fontSize: `${t.typography.baseFontSize}px`, color: c.text }">
    <!-- The application shell, in miniature -->
    <div class="brand-preview__shell" :style="{ borderColor: c.border }">
      <aside class="brand-preview__side" :style="{ background: c.sidebarBackground, color: c.sidebarText, borderColor: c.border }">
        <div class="brand-preview__brand" :style="{ borderColor: c.border }">
          <img :src="logo || stockLogo" alt="" :style="{ height: `${Math.min(t.identity.logoHeight, 34)}px` }">
          <span v-if="t.identity.showNameBesideLogo" class="brand-preview__name">{{ t.identity.applicationName }}</span>
        </div>
        <div class="brand-preview__item"><q-icon name="o_dashboard" size="15px" />Dashboard</div>
        <div class="brand-preview__item" :style="{ background: c.sidebarActiveBackground, color: c.sidebarActiveText }">
          <q-icon name="o_business_center" size="15px" />Requests
        </div>
        <div class="brand-preview__item"><q-icon name="o_settings" size="15px" />Settings</div>
      </aside>

      <div class="brand-preview__main">
        <div class="brand-preview__header" :style="{ background: c.headerBackground, color: c.headerText, borderColor: c.border }">
          <q-icon name="o_menu" size="17px" />
          <span class="brand-preview__name" :style="{ color: c.primary }">{{ t.identity.applicationName }}</span>
        </div>

        <div class="brand-preview__page" :style="{ background: c.pageBackground }">
          <div class="brand-preview__card" :style="cardStyle">
            <component :is="h" v-for="h in HEADING_KEYS" :key="h" :style="headingStyle(h)">
              {{ h.toUpperCase() }} · Heading {{ h.slice(1) }}
            </component>
            <p class="q-mt-sm q-mb-sm">
              Body text looks like this. <span :style="{ color: c.textMuted }">Muted text is quieter,</span>
              and <a :style="{ color: c.link }" @click.prevent>a link</a> stands out.
            </p>

            <div class="brand-preview__field" :style="{ borderColor: c.border, borderRadius: `${t.shape.inputRadius}px` }">
              Text field
            </div>

            <div class="row items-center q-gutter-sm q-mt-sm">
              <span class="brand-preview__btn" :style="primaryButton">Primary</span>
              <span class="brand-preview__btn" :style="secondaryButton">Secondary</span>
            </div>
            <div
              v-if="primaryContrast !== null && primaryContrast < 4.5"
              class="text-caption q-mt-xs" :style="{ color: c.negative }"
            >
              The primary button's text is hard to read on its background (contrast {{ primaryContrast.toFixed(1) }} : 1; aim for 4.5).
            </div>

            <div class="row q-gutter-xs q-mt-sm">
              <span v-for="s in statuses" :key="s.label" class="brand-preview__chip" :style="{ background: s.colour }">{{ s.label }}</span>
            </div>

            <table class="brand-preview__table" :style="{ borderColor: c.border }">
              <thead>
                <tr :style="{ background: c.tableHeaderBackground, color: c.tableHeaderText }">
                  <th :style="{ borderColor: c.border }">Client</th>
                  <th :style="{ borderColor: c.border }">Status</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td :style="{ borderColor: c.border }">Acme Holdings</td>
                  <td :style="{ borderColor: c.border }">In review</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>

    <!-- The sign-in screen -->
    <div class="brand-preview__login" :style="loginStyle">
      <div class="row items-center justify-center q-mb-sm">
        <img :src="logo || stockLogo" alt="" style="height: 26px;">
        <span v-if="t.identity.showNameBesideLogo" class="brand-preview__name q-ml-sm">{{ t.identity.applicationName }}</span>
      </div>
      <div v-if="t.identity.tagline" class="text-caption text-center q-mb-sm" :style="{ color: c.textMuted }">{{ t.identity.tagline }}</div>
      <div class="brand-preview__card brand-preview__card--login" :style="cardStyle">
        <div :style="headingStyle('h5')">{{ t.login.headline }}</div>
        <div class="text-caption" :style="{ color: c.textMuted }">{{ t.login.subtext }}</div>
        <span class="brand-preview__btn brand-preview__btn--block q-mt-sm" :style="primaryButton">Login</span>
      </div>
    </div>
  </div>
</template>

<script setup>
// What a theme looks like, drawn from the theme itself rather than from the page's stylesheet — so the
// draft can be seen before it is saved, and without the rest of the application changing underneath.
import { computed, watch } from "vue";
import stockLogo from "assets/logo.png";
import { CARD_SHADOWS, HEADING_KEYS, contrastRatio, ensureFonts, fontStack } from "services/branding";

const props = defineProps({
  // A RESOLVED theme (services/branding resolveTheme): every setting present.
  theme: { type: Object, required: true },
  logo: { type: String, default: null },
  loginBackground: { type: String, default: null }
});

const t = computed(() => props.theme);
const c = computed(() => props.theme.colors);

const bodyFont = computed(() => fontStack(t.value.typography.bodyFont));
const headingFont = computed(() => fontStack(t.value.typography.headingFont));

// The preview is the first place a newly chosen font is needed.
watch(() => [t.value.typography.bodyFont, t.value.typography.headingFont], ensureFonts, { immediate: true });

const headingStyle = (key) => {
  const h = t.value.typography[key];
  return {
    margin: 0,
    fontFamily: headingFont.value,
    fontSize: `${h.size}px`,
    fontWeight: h.weight,
    lineHeight: h.lineHeight,
    letterSpacing: `${h.letterSpacing}em`,
    textTransform: h.transform,
    color: h.color || t.value.typography.headingColor
  };
};

const cardStyle = computed(() => ({
  background: c.value.surface,
  borderColor: c.value.border,
  borderRadius: `${t.value.shape.cardRadius}px`,
  boxShadow: CARD_SHADOWS[t.value.shape.cardShadow]
}));

const buttonBase = computed(() => ({
  borderRadius: `${t.value.buttons.radius}px`,
  fontWeight: t.value.buttons.fontWeight,
  textTransform: t.value.buttons.uppercase ? "uppercase" : "none"
}));

const primaryButton = computed(() => {
  const b = t.value.buttons.primary;
  return { ...buttonBase.value, background: b.background, color: b.text, border: `1px solid ${b.border || b.background}` };
});

const secondaryButton = computed(() => {
  const b = t.value.buttons.secondary;
  return { ...buttonBase.value, background: b.background || "transparent", color: b.text, border: `1px solid ${b.border}` };
});

const primaryContrast = computed(() =>
  contrastRatio(t.value.buttons.primary.background, t.value.buttons.primary.text));

const statuses = computed(() => [
  { label: "Positive", colour: c.value.positive },
  { label: "Warning", colour: c.value.warning },
  { label: "Negative", colour: c.value.negative },
  { label: "Info", colour: c.value.info },
  { label: "Accent", colour: c.value.accent }
]);

const loginStyle = computed(() => ({
  backgroundColor: t.value.login.backgroundColor,
  backgroundImage: props.loginBackground ? `url("${props.loginBackground}")` : "none",
  borderColor: c.value.border
}));
</script>

<style scoped>
.brand-preview__shell {
  display: flex;
  min-height: 300px;
  border: 1px solid;
  border-radius: 8px;
  overflow: hidden;
}
.brand-preview__side {
  flex: 0 0 132px;
  border-right: 1px solid;
  font-size: 12px;
}
.brand-preview__brand {
  display: flex;
  align-items: center;
  gap: 6px;
  min-height: 44px;
  padding: 5px 8px;
  border-bottom: 1px solid;
}
.brand-preview__brand img { max-width: 100%; object-fit: contain; }
.brand-preview__name { font-weight: 700; font-size: 13px; }
.brand-preview__item {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
}
.brand-preview__main { flex: 1; min-width: 0; display: flex; flex-direction: column; }
.brand-preview__header {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 36px;
  padding: 0 10px;
  border-bottom: 1px solid;
}
.brand-preview__page { flex: 1; padding: 10px; }
.brand-preview__card { padding: 12px; border: 1px solid; }
.brand-preview__card--login { max-width: 240px; margin: 0 auto; }
.brand-preview__field {
  padding: 6px 10px;
  border: 1px solid;
  background: #f9f9f9;
  font-size: 12px;
  color: #8a94a3;
}
.brand-preview__btn {
  display: inline-block;
  padding: 5px 14px;
  font-size: 13px;
  text-align: center;
}
.brand-preview__btn--block { display: block; }
.brand-preview__chip {
  padding: 1px 8px;
  border-radius: 10px;
  color: #ffffff;
  font-size: 11px;
}
.brand-preview__table {
  width: 100%;
  margin-top: 10px;
  border-collapse: collapse;
  font-size: 12px;
}
.brand-preview__table th,
.brand-preview__table td {
  padding: 4px 8px;
  border: 1px solid;
  text-align: left;
  font-weight: 500;
}
.brand-preview__login {
  margin-top: 12px;
  padding: 16px;
  border: 1px solid;
  border-radius: 8px;
  background-size: cover;
  background-position: center;
}
</style>
