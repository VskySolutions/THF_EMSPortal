// Shared permission-key categorisation + humanisation (WO-70).

// Display order for the rendered category sections.
export const CATEGORY_ORDER = Object.freeze([
  "Tenants", "Users", "Access", "REMS", "Settings", "Other"
]);

// Super-admin-only / elevated keys that a Tenant Admin cannot typically grant. Used as a
// best-effort ceiling hint in the form (the backend enforces the real ceiling). Keep conservative.
export const ELEVATED_KEYS = Object.freeze(["tenants.archive", "roles.assign", "tenants.write"]);

export function categoryForKey (key) {
  if (!key) return "Other";
  const prefix = key.split(".")[0];
  switch (prefix) {
    case "tenants": return "Tenants";
    case "users":
    case "persons": return "Users";
    case "roles":
    case "groups": return "Access";
    case "rems": return "REMS";
    case "settings":
    case "optionSets":
    case "records":
    case "email": return "Settings";
    default: return "Other";
  }
}

// "users.reset_password" → "Users · Reset Password"; underscores become spaces (title-cased segments).
export function humanizeKey (key) {
  if (!key) return "";
  return key
    .split(".")
    .map((seg) => seg.split("_").map((w) => w.charAt(0).toUpperCase() + w.slice(1)).join(" "))
    .join(" · ");
}

// Group a flat list of keys into ordered category buckets: [{ category, keys[] }].
export function groupKeysByCategory (keys = []) {
  const buckets = new Map();
  for (const key of keys) {
    const cat = categoryForKey(key);
    if (!buckets.has(cat)) buckets.set(cat, []);
    buckets.get(cat).push(key);
  }
  const ordered = CATEGORY_ORDER.filter((c) => buckets.has(c));
  // Any unexpected categories (defensive) appended after the known order.
  for (const cat of buckets.keys()) {
    if (!ordered.includes(cat)) ordered.push(cat);
  }
  return ordered.map((category) => ({ category, keys: buckets.get(category).slice().sort() }));
}

export function usePermissionCategories () {
  return { CATEGORY_ORDER, ELEVATED_KEYS, categoryForKey, humanizeKey, groupKeysByCategory };
}
