// Where to send someone once they are signed in: the internal path they were bounced from, if any. Never an
// absolute URL, which would turn the login page into an open redirect.
export function postLoginDestination (target) {
  const isInternalPath = typeof target === "string" && target.startsWith("/") && !target.startsWith("//");
  return isInternalPath ? target : "/dashboard";
}
