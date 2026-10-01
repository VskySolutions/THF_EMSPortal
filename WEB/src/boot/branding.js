import { boot } from "quasar/wrappers";
import { watch } from "vue";
import { useAuthStore } from "stores/auth";
import { useBrandingStore } from "stores/branding";

// Tenant branding: the cached brand goes on before the first paint, then the real one is fetched and
// kept in step with whoever is signed in and whichever tenant they are working in.
export default boot(({ store }) => {
  const branding = useBrandingStore(store);
  const auth = useAuthStore(store);

  branding.hydrate();
  if (auth.token) branding.load();

  // Signing in. A refresh also changes the token, but only null → token is a new session.
  watch(() => auth.token, (token, previous) => {
    if (token && !previous) branding.load();
  });

  // Switching tenant, and a Super Admin changing the tenant in view, both announce themselves this way.
  window.addEventListener("tenant-switched", () => { if (auth.token) branding.load(); });
  window.addEventListener("session-cleared", () => branding.keepCache());
});
