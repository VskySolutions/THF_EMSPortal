import { boot } from "quasar/wrappers";
import { Notify } from "quasar";
import { http, http2 } from "boot/axios";
import { useAuthStore } from "stores/auth";

// Error display is owned by the views (inline banners / useNotify on catch);
// these interceptors only handle authentication and the API's rate limit.
export default boot(({ store, router }) => {
  // Remember where they were so signing back in returns them there. Guarded: many 401s land here at once.
  const goToLogin = () => {
    const current = router.currentRoute.value;
    if (current.path.startsWith("/auth")) {
      return;
    }
    router.push({ name: "login", query: { redirect: current.fullPath } });
  };

  // 429 is the API's rate limit: the request fails like any other, and one notice per burst says how long to wait.
  let noticeMutedUntil = 0;
  const onTooManyRequests = (error) => {
    const seconds = Number(error.response?.headers?.["retry-after"]) || 60;
    error.retryAfterSeconds = seconds;
    if (Date.now() > noticeMutedUntil) {
      noticeMutedUntil = Date.now() + 5000;
      Notify.create({ type: "warning", icon: "o_warning", message: `Too many requests. Please wait ${seconds} seconds and try again.` });
    }
    return Promise.reject(error);
  };
  http2.interceptors.response.use(
    (response) => response,
    (error) => (error.response?.status === 429 ? onTooManyRequests(error) : Promise.reject(error))
  );

  // Authenticated instance — 401 triggers a single silent refresh + retry
  // (AC-UI-004.1); if refresh fails, clear the session and go to login (AC-UI-004.2).
  http.interceptors.response.use(
    (response) => response,
    async (error) => {
      const original = error.config;
      const status = error.response?.status;

      if (status === 429) {
        return onTooManyRequests(error);
      }

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
