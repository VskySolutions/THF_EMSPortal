import { boot } from "quasar/wrappers";
import { watch } from "vue";
import { useBrandingStore } from "stores/branding";

export default boot(({ router, store }) => {
  const branding = useBrandingStore(store);
  let pageTitle = null;

  // A page named after the site itself (the client's "<site> Form") gives the whole tab title as a
  // function of the site's name; every other page is "<page> - <site>".
  const render = () => {
    if (typeof pageTitle === "function") document.title = pageTitle(branding.appName);
    else document.title = pageTitle ? `${pageTitle} - ${branding.appName}` : branding.appName;
  };

  router.afterEach((to) => {
    pageTitle = to.meta.title;
    render();
  });
  // The tenant's name can arrive after the page has: the tab follows it.
  watch(() => branding.appName, render);
});
