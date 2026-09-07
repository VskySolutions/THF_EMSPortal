// Minimal client-side JWT helpers. The token is NOT verified here — it is only decoded
// to read the claims the server already issued (permissions drive permission-gated UI).

export function decodeJwtPayload (token) {
  if (!token || typeof token !== "string") return null;
  const parts = token.split(".");
  if (parts.length < 2) return null;
  try {
    const base64 = parts[1].replace(/-/g, "+").replace(/_/g, "/");
    const json = decodeURIComponent(
      atob(base64)
        .split("")
        .map((c) => "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2))
        .join("")
    );
    return JSON.parse(json);
  } catch {
    return null;
  }
}

// The effective permission keys carried by the active-tenant-scoped token. .NET emits a
// repeated "permission" claim, which serializes to a string (one) or an array (many).
export function decodeJwtPermissions (token) {
  const payload = decodeJwtPayload(token);
  const perms = payload?.permission;
  if (Array.isArray(perms)) return perms;
  if (typeof perms === "string") return [perms];
  return [];
}

// Slack: a token dying mid-flight counts as dead, so it is refreshed before the request, not after a 401.
const EXPIRY_SKEW_SECONDS = 60;

// The token's expiry, or null when it carries no `exp`.
export function jwtExpiresAt (token) {
  const exp = decodeJwtPayload(token)?.exp;
  return typeof exp === "number" ? new Date(exp * 1000) : null;
}

// A token whose expiry cannot be read counts as live — the API is the authority on that.
export function isJwtExpired (token, skewSeconds = EXPIRY_SKEW_SECONDS) {
  const expiresAt = jwtExpiresAt(token);
  if (!expiresAt) return false;
  return expiresAt.getTime() - (skewSeconds * 1000) <= Date.now();
}
