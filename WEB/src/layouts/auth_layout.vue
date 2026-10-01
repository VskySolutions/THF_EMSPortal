<template>
  <q-layout view="hHh lpR fFf">
    <q-page-container>
      <q-page class="auth-wrap column flex-center" :style="wrapStyle">
        <div class="auth-panel column items-center">
          <div class="row items-center justify-center" :class="branding.tagline ? 'q-mb-xs' : 'q-mb-lg'">
            <img
              :src="branding.logoUrl || stockLogo" :alt="branding.appName"
              class="auth-brand__logo" :class="{ 'auth-brand__logo--custom': !!branding.logoUrl }"
            >
            <span v-if="branding.showNameBesideLogo" class="text-h5 text-weight-bold q-ml-sm text-grey-9">{{ branding.appName }}</span>
          </div>
          <div v-if="branding.tagline" class="text-body2 text-grey-7 text-center q-mb-lg">{{ branding.tagline }}</div>

          <router-view />

          <div v-if="branding.supportEmail || branding.supportUrl" class="text-caption text-grey-7 q-mt-lg text-center">
            Need help?
            <a v-if="branding.supportEmail" :href="`mailto:${branding.supportEmail}`">{{ branding.supportEmail }}</a>
            <span v-if="branding.supportEmail && branding.supportUrl"> · </span>
            <a v-if="branding.supportUrl" :href="branding.supportUrl" target="_blank" rel="noopener">Support</a>
          </div>

          <!-- Kept from the removed panel: it was the only copyright notice on the sign-in screen. -->
          <div class="text-caption text-grey-6 q-mt-lg text-center">
            <template v-if="branding.footerText">{{ branding.footerText }}</template>
            <template v-else>&copy; {{ year }} VSky Solutions. All rights reserved.</template>
          </div>
        </div>
      </q-page>
    </q-page-container>
  </q-layout>
</template>

<script setup>
import { computed, onMounted } from "vue";
import { useRoute } from "vue-router";
import stockLogo from "assets/logo.png";
import { useBrandingStore } from "stores/branding";

const route = useRoute();
const branding = useBrandingStore();

// Derived rather than hard-coded — the old panel still read 2025.
const year = computed(() => new Date().getFullYear());

// Only what the tenant set is laid over the stylesheet's own background.
const wrapStyle = computed(() => {
  const style = {};
  const colour = branding.theme?.login?.backgroundColor;
  if (colour) style.backgroundColor = colour;
  if (branding.loginBackgroundUrl) {
    style.backgroundImage = `url("${branding.loginBackgroundUrl}")`;
    style.backgroundSize = "cover";
    style.backgroundPosition = "center";
  }
  return style;
});

// A tenant's own sign-in link (/auth/login?tenant=acme) brands the screen for somebody who has never
// signed in on this browser. Without it the screen wears what this browser wore last.
onMounted(() => {
  const tenant = route.query.tenant;
  if (typeof tenant === "string" && tenant) branding.loadPublic(tenant);
});
</script>

<style scoped>
.auth-wrap {
  min-height: 100vh;
  padding: 24px;
  background: #f4f6fb;
}

.auth-panel {
  width: 100%;
  max-width: 420px;
}

.auth-brand__logo {
  width: 44px;
  height: 44px;
}

/* A tenant's logo is rarely square: it keeps its height and takes the width it needs. */
.auth-brand__logo--custom {
  width: auto;
  max-width: 240px;
  object-fit: contain;
}

/* The routed page owns its own card; this just makes every one of them fill the centred column. */
.auth-panel :deep(> *) {
  width: 100%;
}
</style>
