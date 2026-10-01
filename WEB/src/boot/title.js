import { boot } from "quasar/wrappers";
import { watch } from "vue";
import { useBrandingStore } from "stores/branding";

export default boot(({ router, store }) => {
  const branding = useBrandingStore(store);
  let pageTitle = null;

  const render = () => {
    document.title = pageTitle ? `${pageTitle} - ${branding.appName}` : branding.appName;
  };

  router.afterEach((to) => {
    pageTitle = to.meta.title;
    render();
  });
  // The tenant's name can arrive after the page has: the tab follows it.
  watch(() => branding.appName, render);
});
