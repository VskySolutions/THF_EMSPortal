<template>
  <!-- q-mini-drawer-hide / q-mini-drawer-only are Quasar's own markers: collapsed, the wordmark drops out
       and the logo is left alone in the rail — the tenant's compact mark, when it has uploaded one. -->
  <div class="aside-header flex justify-center items-center no-wrap">
    <img
      :src="branding.logoUrl || stockLogo" alt="" class="logo"
      :class="{ 'q-mini-drawer-hide': !!branding.logoMarkUrl }" :style="logoStyle"
    >
    <img v-if="branding.logoMarkUrl" :src="branding.logoMarkUrl" alt="" class="logo logo--mark q-mini-drawer-only">
    <span v-if="branding.showNameBesideLogo" class="text-weight-bold fs-20 q-mini-drawer-hide">{{ branding.appName }}</span>
  </div>
</template>

<script setup>
import { computed } from "vue";
import stockLogo from "assets/logo.png";
import { useBrandingStore } from "stores/branding";

const branding = useBrandingStore();

// Only a height the tenant chose is imposed; otherwise the stylesheet sizes the logo as it always has.
const logoStyle = computed(() => {
  const height = branding.theme?.identity?.logoHeight;
  return height ? { height: `${height}px` } : null;
});
</script>

<style scoped>
.logo {
  max-width: 100%;
  object-fit: contain;
}
.logo--mark {
  height: 36px;
}
</style>
