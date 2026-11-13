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

// What the cache holds: the look, and the name it is worn under.
const cacheable = (store) => ({ tenantName: store.tenantName, theme: store.theme, assets: store.assets });

// The invite whose branding has been asked for, and the request itself — see loadForInvite.
let inviteLoad = null;

// BrandingStore: the branding of the tenant the user is working in, and the page wearing it.
export const useBrandingStore = defineStore("branding", {
  state: () => ({
    theme: {},
    assets: {},
    // The tenant's own name: what the application goes by wherever the branding sets no name of its own.
    tenantName: null,
    // True while the editor is trying a draft on the whole page; the state above is not the draft.
    previewing: false
  }),

  getters: {
    resolved: (state) => resolveTheme(state.theme),
    // The name the branding sets, else the tenant's own name, else the product's.
    appName () { return this.theme?.identity?.applicationName || this.tenantName || this.resolved.identity.applicationName; },
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
      // A response that says nothing about the tenant (the editor's image uploads) leaves the name as it is.
      if (branding && "tenantName" in branding) this.tenantName = branding.tenantName || null;
      this.previewing = false;
      applyBranding(this);
      if (cache) writeCache(cacheable(this));
    },

    /** What this browser wore last, before the API has been asked. */
    hydrate () {
      const cached = readCache();
      if (cached) this.set(cached, { cache: false });
    },

    /** Sign-out wipes localStorage; the sign-in screen that follows should still look like the firm's. */
    keepCache () {
      writeCache(cacheable(this));
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
     * back to find their own sign-in screen rebranded by it. One request per invite however many ask:
     * the layout wears the brand and the form waits on it, because the form is named after the site.
     */
    loadForInvite (inviteCode) {
      if (inviteLoad?.inviteCode !== inviteCode) {
        const done = brandingApi.getForInvite(inviteCode)
          .then((branding) => this.set(branding, { cache: false }))
          .catch(() => { /* stays as it is */ });
        inviteLoad = { inviteCode, done };
      }
      return inviteLoad.done;
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
