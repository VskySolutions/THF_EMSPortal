// Where a signed-in user lands when nothing sent them anywhere in particular: My REMS Requests, which is
// every user's own work. The Dashboard stays in the menu.
export const LANDING_PATH = "/rems/partner";

// Where to send someone once they are signed in: the internal path they were bounced from, if any. Never an
// absolute URL, which would turn the login page into an open redirect.
export function postLoginDestination (target) {
  const isInternalPath = typeof target === "string" && target.startsWith("/") && !target.startsWith("//");
  return isInternalPath ? target : LANDING_PATH;
}
