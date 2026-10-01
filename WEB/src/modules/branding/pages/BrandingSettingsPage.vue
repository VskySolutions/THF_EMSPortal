<template>
  <q-page padding>
    <app-list-header
      :breadcrumbs="[{ label: 'Home', icon: 'o_home', to: '/' }, { label: 'Tenant Settings' }, { label: 'Branding' }]"
      show-back
      @back="$router.back()"
    >
      <template #note>
        How this tenant's copy of the application looks to everyone who uses it: its name and logos, colours,
        fonts and headings, and how buttons, cards and the sign-in screen are styled. Anything left empty keeps
        the application's default.
      </template>
    </app-list-header>

    <!-- No tenant named: the editor follows the tenant in view, as the other settings pages do. -->
    <branding-editor />
  </q-page>
</template>

<script setup>
import AppListHeader from "components/common/AppListHeader.vue";
import BrandingEditor from "modules/branding/components/BrandingEditor.vue";
</script>
