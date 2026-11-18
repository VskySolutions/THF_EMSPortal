import { computed } from "vue";
import { useBrandingStore } from "stores/branding";

// What the site goes by (the branding's name, else the tenant's): the word the request feature is named with.
export const useSiteName = () => {
  const branding = useBrandingStore();
  return computed(() => branding.appName);
};
