import { defineStore } from "pinia";
import { LocalStorage } from "quasar";
import { authApi } from "services/api";
import { useTenantStore } from "stores/tenant";
import { decodeJwtPermissions, isJwtExpired } from "services/jwt";

// Fallback only: used when the API does not send refreshExpiresIn.
const REFRESH_WINDOW_MS = 7 * 24 * 60 * 60 * 1000;

// The refresh token is single-use: /api/auth/refresh revokes the one presented. Concurrent callers
// share this one flight, or the losers get a 401 and end a valid session.
let refreshInFlight = null;

// Bumped on clearSession, so a refresh still in flight at logout cannot store its new tokens.
let sessionGeneration = 0;

// Only the server saying so ends a session; a network blip or a 5xx must not.
const isAuthFailure = (error) => error?.response?.status === 401 || error?.response?.status === 403;

export const useAuthStore = defineStore("auth", {
  state: () => ({
    token: LocalStorage.getItem("token"),
    refreshToken: LocalStorage.getItem("refreshToken"),
    user: LocalStorage.getItem("user"),
    mustChangePassword: false,
    // Effective permissions for the active tenant, decoded from the JWT. Re-derived whenever
    // the token changes (login / refresh / tenant switch) so the UI gates stay in sync.
    permissions: decodeJwtPermissions(LocalStorage.getItem("token")),
    sessionExpiresAt: LocalStorage.getItem("sessionExpiresAt")
      ? new Date(LocalStorage.getItem("sessionExpiresAt"))
      : null
  }),

  getters: {
    isAuthenticated: (state) => !!state.token,
    // WO-123: the RBAC role names the user holds in the active tenant (multi-role).
    roles: () => useTenantStore().activeTenant?.roleNames || [],
    // Permission predicates for the active tenant.
    hasPermission: (state) => (permission) => state.permissions.includes(permission),
    hasAnyPermission: (state) => (permissions) =>
      Array.isArray(permissions) && permissions.some((p) => state.permissions.includes(p))
  },

  actions: {
    // AC-UI-001.2 / AC-UI-001.4
    async login (credentials) {
      const email = credentials.email ?? credentials.username;
      const resp = await authApi.login({ email, password: credentials.password });
      const data = resp?.data;
      if (!data?.accessToken) {
        return resp;
      }
      this._setTokens(data.accessToken, data.refreshToken, data.refreshExpiresIn);
      this.mustChangePassword = !!data.mustChangePassword;
      await this.loadProfile();
      return resp;
    },

    // GET /api/auth/profile → populate current user + tenant assignments.
    async loadProfile () {
      const profile = await authApi.profile();
      this.user = {
        userId: profile.userId,
        email: profile.email,
        displayName: profile.displayName,
        tenants: profile.tenants || []
      };
      LocalStorage.set("user", this.user);
      useTenantStore().setAssignments(profile.tenants || []);
      return profile;
    },

    // AC-UI-004.1: silent token refresh, single-flight (see refreshInFlight).
    refresh () {
      if (!refreshInFlight) {
        const flight = this._rotateTokens().finally(() => {
          if (refreshInFlight === flight) {
            refreshInFlight = null;
          }
        });
        refreshInFlight = flight;
      }
      return refreshInFlight;
    },

    // Called by the router before a protected route renders, so a stale tab refreshes up front
    // instead of rendering and 401ing. False means the session is over and already cleared.
    async ensureSession () {
      // Another tab may have refreshed since this store was created.
      this._hydrateFromStorage();
      if (!this.token) {
        return false;
      }
      if (!isJwtExpired(this.token)) {
        return true;
      }
      if (!this.refreshToken) {
        this.clearSession();
        return false;
      }
      try {
        await this.refresh();
        return true;
      } catch (error) {
        if (isAuthFailure(error)) {
          this.clearSession();
          return false;
        }
        // API unreachable: keep the tokens and let the page report the error.
        return true;
      }
    },

    // AC-UI-002.1 / AC-UI-002.3: clear local session even if the API call fails.
    async logout () {
      const rt = this.refreshToken;
      try {
        await authApi.logout(rt);
      } catch {
        // best-effort; clear locally regardless
      } finally {
        this.clearSession();
      }
    },

    // AC-UI-002.2
    async logoutAll () {
      try {
        await authApi.logoutAll();
      } catch {
        // best-effort
      } finally {
        this.clearSession();
      }
    },

    // Called on app mount to restore the session.
    async initialize () {
      if (!this.token) {
        return;
      }
      try {
        await this.loadProfile();
      } catch (error) {
        // A 401 here is already past the refresh interceptor, so the session really is over.
        if (isAuthFailure(error)) {
          this.clearSession();
        }
      }
    },

    setUserInfo (payload) {
      this.user = { ...this.user, ...payload };
      LocalStorage.set("user", this.user);
    },

    // Never call directly — go through refresh(), which shares one rotation.
    async _rotateTokens () {
      const generation = sessionGeneration;
      const presented = LocalStorage.getItem("refreshToken") || this.refreshToken;
      if (!presented) {
        throw new Error("No refresh token available.");
      }
      try {
        return await this._exchangeRefreshToken(presented, generation);
      } catch (error) {
        // Another tab may have rotated meanwhile; retry with the newer stored token.
        const current = LocalStorage.getItem("refreshToken");
        if (isAuthFailure(error) && current && current !== presented) {
          return await this._exchangeRefreshToken(current, generation);
        }
        throw error;
      }
    },

    async _exchangeRefreshToken (refreshToken, generation) {
      const resp = await authApi.refresh(refreshToken);
      const data = resp?.data;
      if (!data?.accessToken) {
        throw new Error("Token refresh failed.");
      }
      if (generation !== sessionGeneration) {
        // Signed out mid-flight: drop the new tokens rather than storing them.
        throw new Error("Session ended during refresh.");
      }
      this._setTokens(data.accessToken, data.refreshToken, data.refreshExpiresIn);
      return data.accessToken;
    },

    // Adopt whatever another tab last wrote.
    _hydrateFromStorage () {
      const token = LocalStorage.getItem("token");
      if (token !== this.token) {
        this.token = token;
        this.permissions = decodeJwtPermissions(token);
      }
      const refreshToken = LocalStorage.getItem("refreshToken");
      if (refreshToken !== this.refreshToken) {
        this.refreshToken = refreshToken;
      }
    },

    _setTokens (accessToken, refreshToken, refreshExpiresInSeconds) {
      this.token = accessToken;
      this.permissions = decodeJwtPermissions(accessToken);
      LocalStorage.set("token", accessToken);
      // Only a new refresh token moves the session clock — a tenant switch reuses the old one.
      if (refreshToken) {
        this.refreshToken = refreshToken;
        LocalStorage.set("refreshToken", refreshToken);
        const windowMs = refreshExpiresInSeconds > 0 ? refreshExpiresInSeconds * 1000 : REFRESH_WINDOW_MS;
        this.sessionExpiresAt = new Date(Date.now() + windowMs);
        LocalStorage.set("sessionExpiresAt", this.sessionExpiresAt.toISOString());
      }
    },

    // Clear local session without calling the API (safe to use inside interceptors).
    clearSession () {
      refreshInFlight = null;
      sessionGeneration += 1;
      this.token = null;
      this.refreshToken = null;
      this.user = null;
      this.permissions = [];
      this.mustChangePassword = false;
      this.sessionExpiresAt = null;
      LocalStorage.clear();
      useTenantStore().clear();
      // Signing out is a router navigation, not a page load, so anything a module cached at import time
      // survives it and is inherited by whoever signs in next.
      window.dispatchEvent(new Event("session-cleared"));
    }
  }
});
