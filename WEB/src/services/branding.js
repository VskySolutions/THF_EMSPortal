// Tenant branding: what a theme may hold, what each unset value falls back to, and how a theme becomes
// the stylesheet the page wears. Mirrors EmsPortal.Application.Branding (BrandingTheme + BrandingThemeRules).
//
// A theme is SPARSE: null means "as the application ships". Nothing is emitted for a null, so a tenant
// that has changed nothing gets exactly the stylesheets in src/css and not one rule more.

// ---- The stock look ----
// What every null stands for. Shown in the editor as the placeholder beside an untouched setting, and
// used to draw the preview. These restate src/css (tokens.scss, quasar.variables.scss, typography.scss,
// custom.scss) and have to be kept in step with it.
export const DEFAULT_THEME = Object.freeze({
  identity: {
    applicationName: "EMS Portal",
    tagline: null,
    footerText: null,
    supportEmail: null,
    supportUrl: null,
    logoHeight: 57,
    showNameBesideLogo: true
  },
  colors: {
    primary: "#1f6478",
    secondary: "#647a82",
    accent: "#b9862f",
    positive: "#21ba45",
    negative: "#c10015",
    warning: "#b9862f",
    info: "#2b7f96",
    pageBackground: "#f3f9fa",
    surface: "#ffffff",
    text: "#16262c",
    textMuted: "#647a82",
    border: "#dde6e8",
    link: "#1f6478",
    headerBackground: "#ffffff",
    headerText: "#16262c",
    sidebarBackground: "#ffffff",
    sidebarText: "#16262c",
    sidebarActiveBackground: "#e0f2f1",
    sidebarActiveText: "#1f6478",
    tableHeaderBackground: "#ffffff",
    tableHeaderText: "#16262c"
  },
  typography: {
    bodyFont: "Poppins",
    headingFont: "Poppins",
    baseFontSize: 15,
    headingColor: "#1f6478",
    h1: { size: 18, weight: 600, lineHeight: 1.3, letterSpacing: 0, transform: "none", color: null },
    h2: { size: 18, weight: 600, lineHeight: 1.3, letterSpacing: 0, transform: "none", color: null },
    h3: { size: 16, weight: 600, lineHeight: 1.3, letterSpacing: 0, transform: "none", color: null },
    h4: { size: 17, weight: 600, lineHeight: 1.3, letterSpacing: 0, transform: "none", color: null },
    h5: { size: 17, weight: 600, lineHeight: 1.3, letterSpacing: 0, transform: "none", color: null },
    h6: { size: 17, weight: 600, lineHeight: 1.3, letterSpacing: 0, transform: "none", color: null }
  },
  buttons: {
    radius: 3,
    uppercase: false,
    fontWeight: 500,
    primary: { background: null, text: "#ffffff", border: null },
    secondary: { background: null, text: null, border: null }
  },
  shape: {
    cardRadius: 5,
    inputRadius: 4,
    cardShadow: "soft"
  },
  login: {
    backgroundColor: "#f4f6fb",
    headline: "Welcome back 👋",
    subtext: "Please sign in to your account to continue."
  }
});

export const HEADING_KEYS = ["h1", "h2", "h3", "h4", "h5", "h6"];

// ---- Fonts ----
// The same list the API accepts. Everything but the system stack is fetched from Google Fonts by name.
export const FONT_OPTIONS = [
  { label: "System default", value: "System", serif: false },
  { label: "Poppins", value: "Poppins", serif: false },
  { label: "Inter", value: "Inter", serif: false },
  { label: "Roboto", value: "Roboto", serif: false },
  { label: "Open Sans", value: "Open Sans", serif: false },
  { label: "Lato", value: "Lato", serif: false },
  { label: "Montserrat", value: "Montserrat", serif: false },
  { label: "Nunito", value: "Nunito", serif: false },
  { label: "Nunito Sans", value: "Nunito Sans", serif: false },
  { label: "Source Sans 3", value: "Source Sans 3", serif: false },
  { label: "Work Sans", value: "Work Sans", serif: false },
  { label: "DM Sans", value: "DM Sans", serif: false },
  { label: "Manrope", value: "Manrope", serif: false },
  { label: "Plus Jakarta Sans", value: "Plus Jakarta Sans", serif: false },
  { label: "Raleway", value: "Raleway", serif: false },
  { label: "Rubik", value: "Rubik", serif: false },
  { label: "IBM Plex Sans", value: "IBM Plex Sans", serif: false },
  { label: "Noto Sans", value: "Noto Sans", serif: false },
  { label: "Merriweather (serif)", value: "Merriweather", serif: true },
  { label: "Playfair Display (serif)", value: "Playfair Display", serif: true },
  { label: "Lora (serif)", value: "Lora", serif: true },
  { label: "Source Serif 4 (serif)", value: "Source Serif 4", serif: true },
  { label: "Roboto Slab (serif)", value: "Roboto Slab", serif: true }
];

// Already on the page through quasar.variables.scss and the roboto-font extra.
const BUNDLED_FONTS = ["Poppins", "Merriweather", "Roboto"];

const SYSTEM_STACK = "system-ui, -apple-system, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif";

const fontOption = (name) => FONT_OPTIONS.find((f) => f.value === name) || null;

/** The CSS font-family for a font name off the list, or null for a name that is not on it. */
export function fontStack (name) {
  const font = fontOption(name);
  if (!font) return null;
  if (font.value === "System") return SYSTEM_STACK;
  return `"${font.value}", ${font.serif ? "Georgia, \"Times New Roman\", serif" : SYSTEM_STACK}`;
}

/** Puts the stylesheet for these fonts on the page. Asking again for a font already loaded costs nothing. */
export function ensureFonts (names) {
  if (typeof document === "undefined") return;
  [...new Set(names)]
    .filter((name) => fontOption(name) && name !== "System" && !BUNDLED_FONTS.includes(name))
    .forEach((name) => {
      const id = `brand-font-${name.replace(/\s+/g, "-").toLowerCase()}`;
      if (document.getElementById(id)) return;
      const link = document.createElement("link");
      link.id = id;
      link.rel = "stylesheet";
      // The v1 endpoint on purpose: it serves the weights a family has and skips the rest, where css2
      // answers 400 for the whole request if one weight is missing.
      link.href = `https://fonts.googleapis.com/css?family=${encodeURIComponent(name)}:300,400,500,600,700,800&display=swap`;
      document.head.appendChild(link);
    });
}

// ---- Colour arithmetic ----
const HEX = /^#[0-9a-f]{6}$/i;

export const isHex = (value) => typeof value === "string" && HEX.test(value);

const toRgb = (hex) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
const toHex = (rgb) => `#${rgb.map((v) => Math.round(Math.min(255, Math.max(0, v))).toString(16).padStart(2, "0")).join("")}`;

/** `amount` of the way from `hex` to `target` (0 = hex, 1 = target). */
export const mix = (hex, target, amount) => {
  const a = toRgb(hex);
  const b = toRgb(target);
  return toHex(a.map((v, i) => v + (b[i] - v) * amount));
};
export const tint = (hex, amount) => mix(hex, "#ffffff", amount);
export const shade = (hex, amount) => mix(hex, "#000000", amount);

const luminance = (hex) => {
  const [r, g, b] = toRgb(hex).map((v) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};

/** WCAG contrast ratio between two colours, 1 to 21. Body text wants 4.5 or more. */
export function contrastRatio (a, b) {
  if (!isHex(a) || !isHex(b)) return null;
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

// ---- Shapes ----
const isObject = (value) => value !== null && typeof value === "object" && !Array.isArray(value);

/** A theme with every setting present and null — the editor's blank form. */
export function emptyTheme () {
  const blank = (source) => Object.fromEntries(
    Object.entries(source).map(([key, value]) => [key, isObject(value) ? blank(value) : null]));
  return blank(DEFAULT_THEME);
}

/** `theme` laid over `base`, key by key; a null in `theme` leaves the base value standing. */
function overlay (base, theme) {
  return Object.fromEntries(Object.entries(base).map(([key, value]) => {
    const given = theme?.[key];
    if (isObject(value)) return [key, overlay(value, isObject(given) ? given : {})];
    return [key, given === null || given === undefined || given === "" ? value : given];
  }));
}

/** The sparse theme the API returned, shaped for the editor: every key present, unset ones null. */
export const toDraft = (theme) => overlay(emptyTheme(), theme);

/**
 * The theme as it will actually look: the stock value wherever the tenant set none, and the values that
 * follow another setting (a button that is "the primary colour") followed through.
 */
export function resolveTheme (theme) {
  const t = overlay(DEFAULT_THEME, theme);
  const sparse = toDraft(theme);
  t.colors.link = sparse.colors.link || t.colors.primary;
  t.colors.sidebarActiveText = sparse.colors.sidebarActiveText || t.colors.primary;
  t.colors.sidebarActiveBackground = sparse.colors.sidebarActiveBackground ||
    (sparse.colors.primary ? tint(t.colors.primary, 0.88) : t.colors.sidebarActiveBackground);
  t.typography.headingColor = sparse.typography.headingColor || t.colors.primary;
  t.buttons.primary.background = t.buttons.primary.background || t.colors.primary;
  t.buttons.secondary.text = t.buttons.secondary.text || t.colors.primary;
  t.buttons.secondary.border = t.buttons.secondary.border || t.buttons.secondary.text;
  return t;
}

export const CARD_SHADOWS = Object.freeze({
  none: "none",
  soft: "0 2px 10px rgba(0, 0, 0, 0.07)",
  raised: "0 8px 24px rgba(0, 0, 0, 0.14)"
});

// ---- Theme → stylesheet ----
const TRANSFORMS = ["none", "uppercase", "capitalize"];

const colour = (value) => (isHex(value) ? value.toLowerCase() : null);
const number = (value, min, max) => {
  if (value === null || value === undefined || value === "") return null;
  const n = Number(value);
  return Number.isFinite(n) && n >= min && n <= max ? n : null;
};

/**
 * The stylesheet for a sparse theme. Every value is checked again here even though the API validated it:
 * the theme is also read back from localStorage, and what goes into a <style> element should not depend
 * on nobody having edited that.
 */
export function buildCss (theme) {
  const t = toDraft(theme);
  const c = t.colors;
  const root = [];
  const rules = [];

  const decl = (prop, value, important = false) =>
    (value === null || value === undefined ? null : `${prop}:${value}${important ? " !important" : ""}`);
  const rule = (selector, declarations) => {
    const body = declarations.filter(Boolean).join(";");
    if (body) rules.push(`${selector}{${body}}`);
  };
  const px = (value) => (value === null ? null : `${value}px`);

  // Brand colours. Quasar's own (--q-*) and the token ramps in tokens.scss are the same palette under two
  // names, so a brand colour moves both.
  const primary = colour(c.primary);
  if (primary) {
    root.push(
      `--q-primary:${primary}`,
      `--teal-900:${shade(primary, 0.45)}`,
      `--teal-800:${shade(primary, 0.25)}`,
      `--teal-700:${primary}`,
      `--teal-600:${tint(primary, 0.15)}`,
      `--teal-500:${tint(primary, 0.3)}`,
      `--teal-300:${tint(primary, 0.55)}`,
      `--teal-100:${tint(primary, 0.88)}`,
      `--teal-050:${tint(primary, 0.94)}`
    );
    // teal-1 is used across the app as "a tint of the brand colour" (selected rows, chips, role badges).
    rule(".bg-teal-1", [decl("background", tint(primary, 0.88), true)]);
  }
  const accent = colour(c.accent);
  if (accent) root.push(`--q-accent:${accent}`, `--gold-600:${accent}`, `--gold-100:${tint(accent, 0.85)}`);
  [["secondary", "--q-secondary"], ["positive", "--q-positive"], ["negative", "--q-negative"],
    ["warning", "--q-warning"], ["info", "--q-info"]].forEach(([key, name]) => {
    const value = colour(c[key]);
    if (value) root.push(`${name}:${value}`);
  });

  const text = colour(c.text);
  if (text) root.push(`--q-dark:${text}`, `--ink-900:${text}`);
  const muted = colour(c.textMuted);
  if (muted) root.push(`--ink-500:${muted}`);
  const border = colour(c.border);
  if (border) root.push(`--line:${border}`);

  if (root.length) rules.unshift(`:root{${root.join(";")}}`);

  // Page
  rule("body", [
    decl("background-color", colour(c.pageBackground)),
    decl("color", text),
    decl("font-family", fontStack(t.typography.bodyFont)),
    decl("font-size", px(number(t.typography.baseFontSize, 12, 18)))
  ]);
  rule(".q-card", [
    decl("background-color", colour(c.surface)),
    decl("border-radius", px(number(t.shape.cardRadius, 0, 24)), true),
    decl("box-shadow", CARD_SHADOWS[t.shape.cardShadow] || null)
  ]);
  rule(".q-card--bordered,.q-separator", [decl("border-color", border)]);
  rule(".q-field--outlined .q-field__control", [decl("border-radius", px(number(t.shape.inputRadius, 0, 16)))]);
  rule("a:not([class*=\"q-\"])", [decl("color", colour(c.link))]);

  // Chrome
  rule(".q-header.header", [decl("background-color", colour(c.headerBackground)), decl("color", colour(c.headerText))]);
  rule(".q-header.header .text-black,.q-header.header .text-grey-9", [decl("color", colour(c.headerText), true)]);
  rule(".q-drawer--left.bg-white", [decl("background", colour(c.sidebarBackground), true)]);
  rule(".q-drawer--left", [decl("color", colour(c.sidebarText))]);
  rule(".app-menu .app-menu__active,.app-menu__flyout .app-menu__active", [
    decl("background", colour(c.sidebarActiveBackground) || (primary ? tint(primary, 0.88) : null), true),
    decl("color", colour(c.sidebarActiveText), true)
  ]);
  rule(".q-table thead th", [
    decl("background-color", colour(c.tableHeaderBackground)),
    decl("color", colour(c.tableHeaderText))
  ]);

  // Type
  const headingSelectors = HEADING_KEYS.flatMap((h) => [h, `.text-${h}`]);
  rule([...headingSelectors, ".text-subtitle1", ".text-subtitle2"].join(","), [
    decl("font-family", fontStack(t.typography.headingFont))
  ]);
  rule([...headingSelectors, ".text-subtitle1"].join(","), [decl("color", colour(t.typography.headingColor))]);
  HEADING_KEYS.forEach((h) => {
    const s = t.typography[h];
    const spacing = number(s.letterSpacing, -0.05, 0.3);
    rule(`${h},.text-${h}`, [
      decl("font-size", px(number(s.size, 10, 72))),
      decl("font-weight", number(s.weight, 300, 900)),
      decl("line-height", number(s.lineHeight, 1, 2)),
      decl("letter-spacing", spacing === null ? null : `${spacing}em`),
      decl("text-transform", TRANSFORMS.includes(s.transform) ? s.transform : null),
      decl("color", colour(s.color))
    ]);
  });

  // Buttons. "Primary" is the filled main action; "secondary" is the outlined alternative beside it —
  // which is how this application pairs them (Save / Test connection, Add / Filters).
  const b = t.buttons;
  rule(".q-btn:not(.q-btn--round):not(.q-btn--fab)", [decl("border-radius", px(number(b.radius, 0, 28)))]);
  rule(".q-btn,.q-btn.q-btn--no-uppercase", [
    decl("text-transform", b.uppercase === true ? "uppercase" : b.uppercase === false ? "none" : null),
    decl("font-weight", number(b.fontWeight, 400, 700))
  ]);
  rule(".q-btn.bg-primary", [
    decl("background", colour(b.primary.background), true),
    decl("color", colour(b.primary.text), true)
  ]);
  rule(".q-btn.bg-primary:before", [
    decl("border", colour(b.primary.border) ? `1px solid ${colour(b.primary.border)}` : null)
  ]);
  rule(".q-btn.q-btn--outline.text-primary", [
    decl("background", colour(b.secondary.background), true),
    decl("color", colour(b.secondary.text), true)
  ]);
  rule(".q-btn.q-btn--outline.text-primary:before", [decl("border-color", colour(b.secondary.border))]);

  return rules.join("\n");
}

// ---- Applying ----
const STYLE_ID = "brand-theme";
let stockFavicons = null;

/** Absolute URL for an image the API serves (`/api/media/{id}/content`). */
export const assetUrl = (path) => (path ? `${(process.env.API_BASE_URL || "").replace(/\/+$/, "")}${path}` : null);

function applyFavicon (path) {
  const links = [...document.querySelectorAll("link[rel~=\"icon\"]")];
  if (!stockFavicons) stockFavicons = links.map((link) => ({ link, href: link.getAttribute("href"), type: link.getAttribute("type") }));
  stockFavicons.forEach(({ link, href, type }) => {
    if (path) {
      link.setAttribute("href", assetUrl(path));
      link.removeAttribute("type"); // the upload may be an .ico or a .webp; let the browser read it
    } else {
      link.setAttribute("href", href);
      if (type) link.setAttribute("type", type);
    }
  });
}

/** Makes the page wear `theme` and `assets`. An empty theme puts it back on the stock look. */
export function applyBranding ({ theme, assets } = {}) {
  if (typeof document === "undefined") return;
  let style = document.getElementById(STYLE_ID);
  if (!style) {
    style = document.createElement("style");
    style.id = STYLE_ID;
    // Last in <head>, so at equal specificity these rules beat the bundled stylesheets.
    document.head.appendChild(style);
  }
  style.textContent = buildCss(theme);
  ensureFonts([theme?.typography?.bodyFont, theme?.typography?.headingFont].filter(Boolean));
  applyFavicon(assets?.favicon || null);
}

// ---- Presets ----
// Starting points for the Colours tab: each sets the three brand colours and leaves the rest to follow.
export const COLOUR_PRESETS = [
  { name: "Teal (stock)", primary: null, secondary: null, accent: null, swatch: "#1f6478" },
  { name: "Ocean", primary: "#1d4ed8", secondary: "#64748b", accent: "#f59e0b", swatch: "#1d4ed8" },
  { name: "Forest", primary: "#166534", secondary: "#57645c", accent: "#ca8a04", swatch: "#166534" },
  { name: "Burgundy", primary: "#7f1d3a", secondary: "#6b5b62", accent: "#b9862f", swatch: "#7f1d3a" },
  { name: "Indigo", primary: "#4338ca", secondary: "#6b7280", accent: "#ec4899", swatch: "#4338ca" },
  { name: "Slate", primary: "#334155", secondary: "#64748b", accent: "#0ea5e9", swatch: "#334155" },
  { name: "Sunset", primary: "#c2410c", secondary: "#78716c", accent: "#0f766e", swatch: "#c2410c" }
];
