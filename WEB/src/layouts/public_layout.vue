<template>
  <!-- Minimal PUBLIC layout for the anonymous REMS client EMS form (WO-116). -->
  <q-layout view="hHh lpR fFf">
    <q-header class="public-header">
      <q-toolbar class="public-toolbar">
        <img src="~assets/logo.png" alt="EMS Portal" class="public-header__logo">
        <q-toolbar-title class="text-weight-bold">EMS Portal</q-toolbar-title>
      </q-toolbar>
    </q-header>

    <q-page-container>
      <q-page class="public-page">
        <div class="public-page__inner">
          <router-view />
        </div>
      </q-page>
    </q-page-container>
  </q-layout>
</template>

<script setup>
// No shell chrome, no stores — the public form is fully self-contained and unauthenticated.
</script>

<style scoped>
.public-header {
  background: var(--q-primary);
}
.public-toolbar {
  max-width: 960px;
  width: 100%;
  margin: 0 auto;
}
.public-header__logo {
  width: 32px;
  height: 32px;
  margin-right: 12px;
}
/* The one page this layout carries is a long form, so the page gutter is kept modest — what is spent
   here is spent before the client has answered anything. */
.public-page {
  background: #f4f6fb;
  padding: 16px 16px 28px;
}
/* A phone has no width to give away to a gutter: the form cards get it instead. */
@media (max-width: 599px) {
  .public-page {
    padding: 12px 10px 24px;
  }
}
.public-page__inner {
  width: 100%;
  max-width: 960px;
  margin: 0 auto;
}
</style>
