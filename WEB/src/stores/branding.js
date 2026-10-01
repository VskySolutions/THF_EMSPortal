import { defineStore } from "pinia";
import { brandingApi } from "services/api";
import { applyBranding, assetUrl, resolveTheme } from "services/branding";

// Kept in the browser's own localStorage rather than Quasar's: it has to be readable before the app
// boots (so the first paint is already branded) and it is put back after sign-out clears the rest.
const CACHE_KEY = "branding";

const readCache = () => {
  try {
    const raw = window.localStorage.getItem(CACHE_KEY);
    const cached = raw ? JSON.parse(raw) : null;
    return cached && typeof cached === "object" ? cached : null;
  } catch {
    return null;
  }
};

const writeCache = (value) => {
  try {
    window.localStorage.setItem(CACHE_KEY, JSON.stringify(value));
  } catch {
    // Storage blocked: the brand is simply fetched again on the next load.
  }
};

// BrandingStore: the branding of the tenant the user is working in, and the page wearing it.
export const useBrandingStore = defineStore("branding", {
  state: () => ({
    theme: {},
    assets: {},
    // True while the editor is trying a draft on the whole page; the state above is not the draft.
    previewing: false
  }),

  getters: {
    resolved: (state) => resolveTheme(state.theme),
    appName () { return this.resolved.identity.applicationName; },
    tagline () { return this.resolved.identity.tagline; },
    footerText () { return this.resolved.identity.footerText; },
    supportEmail () { return this.resolved.identity.supportEmail; },
    supportUrl () { return this.resolved.identity.supportUrl; },
    showNameBesideLogo () { return this.resolved.identity.showNameBesideLogo !== false; },
    login () { return this.resolved.login; },
    // null while the tenant has uploaded none — the caller falls back to the bundled logo.
    logoUrl: (state) => assetUrl(state.assets?.logo),
    logoDarkUrl: (state) => assetUrl(state.assets?.logoDark || state.assets?.logo),
    logoMarkUrl: (state) => assetUrl(state.assets?.logoMark),
    loginBackgroundUrl: (state) => assetUrl(state.assets?.loginBackground)
  },

  actions: {
    /** Adopt a branding (an API response, or the cache) and put it on the page. */
    set (branding, { cache = true } = {}) {
      this.theme = branding?.theme || {};
      this.assets = branding?.assets || {};
      this.previewing = false;
      applyBranding(this);
      if (cache) writeCache({ theme: this.theme, assets: this.assets });
    },

    /** What this browser wore last, before the API has been asked. */
    hydrate () {
      const cached = readCache();
      if (cached) this.set(cached, { cache: false });
    },

    /** Sign-out wipes localStorage; the sign-in screen that follows should still look like the firm's. */
    keepCache () {
      writeCache({ theme: this.theme, assets: this.assets });
    },

    /** The signed-in user's tenant (or, for a Super Admin, the tenant they are viewing). Never rejects. */
    async load () {
      try {
        this.set(await brandingApi.get());
      } catch {
        // The page keeps whatever it is wearing; a failed brand fetch is not worth an error toast.
      }
    },

    /** The sign-in screen reached through a tenant's own link (`/auth/login?tenant=acme`). */
    async loadPublic (tenantIdentifier) {
      try {
        this.set(await brandingApi.getPublic(tenantIdentifier));
      } catch { /* stays as it is */ }
    },

    /**
     * The public client form. Not cached: a member of staff opening a client's link should not come
     * back to find their own sign-in screen rebranded by it.
     */
    async loadForInvite (inviteCode) {
      try {
        this.set(await brandingApi.getForInvite(inviteCode), { cache: false });
      } catch { /* stays as it is */ }
    },

    /** Try a draft on the whole page without adopting it. */
    preview (theme, assets) {
      this.previewing = true;
      applyBranding({ theme, assets: assets || this.assets });
    },

    endPreview () {
      if (!this.previewing) return;
      this.previewing = false;
      applyBranding(this);
    }
  }
});
