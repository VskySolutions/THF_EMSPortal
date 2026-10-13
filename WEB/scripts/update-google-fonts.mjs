// Refreshes the catalogue of Google Fonts families the branding editor offers.
//
//   node scripts/update-google-fonts.mjs
//
// Writes the same file to two places: src/services/google-fonts.json, which fills the Body font and
// Heading font dropdowns and decides each family's fallback stack, and the API's embedded copy, which
// is what PUT /api/branding validates a chosen font against. Run it whenever the list looks stale;
// Google adds families most months. The source is the JSON the Google Fonts site itself reads, so no
// API key is needed.
import { mkdir, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const SOURCE = "https://fonts.google.com/metadata/fonts";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const TARGETS = [
  resolve(webRoot, "src/services/google-fonts.json"),
  resolve(webRoot, "../API/src/EmsPortal.Application/Branding/google-fonts.json")
];

// Category keys as they are stored; the labels live beside the dropdown in src/services/branding.js.
const CATEGORIES = {
  "Sans Serif": "sans-serif",
  Serif: "serif",
  Display: "display",
  Handwriting: "handwriting",
  Monospace: "monospace"
};

// A family name ends up inside CSS and a stylesheet URL on both sides, so the shape is enforced here,
// where a surprise costs a failed script rather than a broken page.
const SAFE_NAME = /^[A-Za-z0-9 ]{1,60}$/;

async function main () {
  const response = await fetch(SOURCE, { headers: { accept: "application/json" } });
  if (!response.ok) throw new Error(`${SOURCE} answered ${response.status}.`);
  let raw = await response.text();
  // The site's endpoints carry an anti-hijacking prefix some of the time.
  if (raw.startsWith(")]}'")) raw = raw.slice(4);
  const { familyMetadataList: families } = JSON.parse(raw);
  if (!Array.isArray(families) || families.length < 1000) {
    throw new Error(`Expected well over a thousand families and got ${families?.length ?? "none"}; the feed has changed shape.`);
  }

  const grouped = Object.fromEntries(Object.values(CATEGORIES).map((key) => [key, []]));
  const skipped = [];
  for (const family of families) {
    const key = CATEGORIES[family.category];
    // "isBrandFont" marks Google's own families (Roboto, Noto) and they are served like any other; only
    // an entry outside the open-source collection is not.
    if (!key || family.isOpenSource === false) {
      skipped.push(`${family.family} (${family.category}${family.isOpenSource === false ? ", not open source" : ""})`);
      continue;
    }
    if (!SAFE_NAME.test(family.family)) throw new Error(`"${family.family}" has characters the font rules do not allow.`);
    grouped[key].push(family.family);
  }
  for (const names of Object.values(grouped)) names.sort((a, b) => a.localeCompare(b, "en", { sensitivity: "base" }));

  const catalogue = {
    updated: new Date().toISOString().slice(0, 10),
    source: SOURCE,
    families: grouped
  };
  const json = `${JSON.stringify(catalogue, null, 2)}\n`;

  for (const target of TARGETS) {
    await mkdir(dirname(target), { recursive: true });
    await writeFile(target, json, "utf8");
    console.log(`wrote ${target}`);
  }
  const total = Object.values(grouped).reduce((n, names) => n + names.length, 0);
  console.log(`${total} families: ${Object.entries(grouped).map(([k, v]) => `${k} ${v.length}`).join(", ")}`);
  if (skipped.length) console.log(`skipped ${skipped.length}: ${skipped.join("; ")}`);
}

main().catch((err) => {
  console.error(err.message);
  process.exitCode = 1;
});
