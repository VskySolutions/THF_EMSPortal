import { boot } from "quasar/wrappers";
import { http } from "boot/axios";
import { useAuthStore } from "stores/auth";

// Error display is owned by the views (inline banners / useNotify on catch);
// these interceptors only handle authentication concerns.
export default boot(({ store, router }) => {
  // Remember where they were so signing back in returns them there. Guarded: many 401s land here at once.
  const goToLogin = () => {
    const current = router.currentRoute.value;
    if (current.path.startsWith("/auth")) {
      return;
    }
    router.push({ name: "login", query: { redirect: current.fullPath } });
  };

  // Authenticated instance — 401 triggers a single silent refresh + retry
  // (AC-UI-004.1); if refresh fails, clear the session and go to login (AC-UI-004.2).
  http.interceptors.response.use(
    (response) => response,
    async (error) => {
      const original = error.config;
      const status = error.response?.status;

      if (status === 401 && original && !original._retry) {
        const authStore = useAuthStore(store);
        original._retry = true;

        if (authStore.refreshToken) {
          try {
            // Shared single flight: the refresh token is single-use, so one rotation per burst of 401s.
            const newToken = await authStore.refresh();
            original.headers = original.headers || {};
            original.headers.Authorization = `Bearer ${newToken}`;
            return http(original);
          } catch (refreshError) {
            // Only a dead refresh token ends the session; a network blip or 5xx leaves it alone.
            if (refreshError?.response?.status === 401 || refreshError?.response?.status === 403) {
              authStore.clearSession();
              goToLogin();
            }
            return Promise.reject(error);
          }
        }

        authStore.clearSession();
        goToLogin();
        return Promise.reject(error);
      }

      return Promise.reject(error);
    }
  );
});
