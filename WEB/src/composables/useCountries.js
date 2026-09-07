import { Country } from "country-state-city";

// Centralised country-list helpers, reused by every country / country-code dropdown so the ordering and
// option shapes stay identical.
const PINNED_ISO = ["US", "IN"];

export const DEFAULT_COUNTRY_ISO = "US";

const all = Country.getAllCountries();
const pinned = PINNED_ISO.map((iso) => all.find((c) => c.isoCode === iso)).filter(Boolean);
const rest = all.filter((c) => !PINNED_ISO.includes(c.isoCode));

/** All countries with US then India pinned on top. */
export const orderedCountries = [...pinned, ...rest];

/** Dial-code option, e.g. "United States (+1)" → value is the ISO-2 code. The flag emoji is omitted
    because on Windows it renders as the two-letter ISO code, which looks like a duplicate. */
export const dialCodeOption = (c) => ({ label: `${c.name} (+${c.phonecode})`, value: c.isoCode });

/** Country option keyed by ISO-2 code. */
export const countryOption = (c) => ({ label: c.name, value: c.isoCode });

/** Display name for an ISO-2 code ("US" → "United States"). */
export const countryNameFromIso = (isoCode) => all.find((c) => c.isoCode === isoCode)?.name || null;

/** Country option keyed by display name (e.g. for nationality fields). */
export const countryNameOption = (c) => ({ label: c.name, value: c.name });

/** ISO-2 code from a stored dial code ("+91" → "IN"). */
export const isoFromDial = (dial) => {
  if (!dial) return null;
  const normalized = String(dial).replace("+", "");
  return orderedCountries.find((c) => c.phonecode === normalized)?.isoCode || null;
};

/** Dial code from an ISO-2 code ("IN" → "+91"). */
export const dialFromIso = (isoCode) => {
  const c = all.find((x) => x.isoCode === isoCode);
  return c ? `+${c.phonecode}` : null;
};

export function useCountries () {
  return {
    allCountries: all,
    orderedCountries,
    dialCodeOption,
    countryOption,
    countryNameOption,
    countryNameFromIso,
    isoFromDial,
    dialFromIso,
    DEFAULT_COUNTRY_ISO
  };
}
