import { isValidPhoneNumber, parsePhoneNumber, validatePhoneNumberLength } from "libphonenumber-js";

// A North American number in the right shape — area code and exchange both starting 2–9 — is taken even
// where the library has not caught up with the area code: 232 and 687 are assigned, and it refused them.
const NANP_SHAPE = /^[2-9]\d{2}[2-9]\d{6}$/;

const parsed = (value, iso) => {
  try { return parsePhoneNumber(String(value), iso) || null; } catch { return null; }
};

/** Whether `value` is a complete number in the length and format `iso` (ISO-2 country) uses. */
export const phoneFitsCountry = (value, iso) => {
  if (!value || !iso) return false;
  try {
    if (isValidPhoneNumber(String(value), iso)) return true;
  } catch { /* judged below */ }
  const number = parsed(value, iso);
  return !!number && number.countryCallingCode === "1" && NANP_SHAPE.test(number.nationalNumber);
};

/** What is wrong with it, said for the person typing: "" when nothing is. */
export const phoneProblem = (value, iso, countryName = "the selected country", example = "") => {
  if (!value || phoneFitsCountry(value, iso)) return "";
  const eg = example ? ` (e.g. ${example})` : "";
  let length;
  try { length = validatePhoneNumberLength(String(value), iso); } catch { length = undefined; }
  if (length === "TOO_SHORT") return `This number is too short for ${countryName}${eg}.`;
  if (length === "TOO_LONG") return `This number is too long for ${countryName}${eg}.`;
  return `This is not a valid phone number for ${countryName}${eg}.`;
};

// The country whose numbers are written the way they are dialled at home: "(201) 555-0123". A number
// from anywhere else keeps its dial code, "+91 98765 43210", because that is all that says where it is from.
const HOME_COUNTRY_ISO = "US";

const written = (number, iso) => {
  const home = (number.country || iso) === HOME_COUNTRY_ISO;
  const text = home ? number.formatNational() : number.formatInternational();
  // The library leaves a +1 number in an area code it does not know as bare digits: the pattern is put on by hand.
  const parts = number.countryCallingCode === "1" && !/\D/.test(home ? text : text.replace(/^\+1\s*/, ""))
    ? /^(\d{3})(\d{3})(\d{4})$/.exec(number.nationalNumber)
    : null;
  if (!parts) return text;
  return home ? `(${parts[1]}) ${parts[2]}-${parts[3]}` : `+1 ${parts[1]} ${parts[2]} ${parts[3]}`;
};

/** What a phone field stores: the number formatted as it reads on screen, or the text as typed while it
    is not yet a complete number. */
export const phoneForStorage = (value, iso) => {
  if (!value) return "";
  const number = phoneFitsCountry(value, iso) ? parsed(value, iso) : null;
  return number ? written(number, iso) : String(value);
};

/** A stored phone for showing on a view. Numbers saved before the format changed are bare digits
    ("+12015550123"); anything that is not a number is shown as it is. */
export const formatPhone = (value) => {
  const text = String(value ?? "").trim();
  if (!text) return "";
  // Looser than the form: an old number the form would now refuse is still shown formatted.
  const number = parsed(text, HOME_COUNTRY_ISO);
  return number?.isPossible() ? written(number, HOME_COUNTRY_ISO) : text;
};
