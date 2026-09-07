// What a person's first or last name may be, in one place, so every form that asks for one asks for the
// same thing — and so the browser and the server agree (the C# twin is Api/Validators/PersonNames.cs).

export const NAME_MAX_LENGTH = 100;

/** The generational suffixes offered beside a name — Jr., Sr., II, III, IV. Suggestions, not a closed
    list: these are what most people need, not all a person may have. */
export const NAME_SUFFIXES = [
  { value: "Jr.", label: "Jr.", caption: "Junior" },
  { value: "Sr.", label: "Sr.", caption: "Senior" },
  { value: "II", label: "II", caption: "The second" },
  { value: "III", label: "III", caption: "The third" },
  { value: "IV", label: "IV", caption: "The fourth" }
];

/** Mirrors the nvarchar(16) the suffix columns are stored in. */
export const NAME_SUFFIX_MAX_LENGTH = 16;

// A letter from any script, plus the combining marks that go with one.
const LETTER = /^[\p{L}\p{M}]/u;
// The whole name: opens on a letter, and carries nothing but letters, single spaces and the three marks.
const NAME_SHAPE = /^[\p{L}\p{M}][\p{L}\p{M} '’.-]*$/u;

/** What is wrong with a name, as a sentence to show under the field — or "" when there is nothing wrong. */
export function nameIssue (value, label = "This name") {
  const raw = String(value ?? "");
  const trimmed = raw.trim();
  if (!trimmed) return "";

  if (raw !== trimmed) return `${label} cannot start or end with a space.`;
  if (trimmed.length > NAME_MAX_LENGTH) return `${label} is at most ${NAME_MAX_LENGTH} characters.`;
  if (/\d/.test(trimmed)) return `${label} cannot contain numbers.`;
  if (/\s{2,}/.test(trimmed)) return `${label} cannot contain two spaces in a row.`;
  if (!LETTER.test(trimmed)) return `${label} must start with a letter.`;
  if (!NAME_SHAPE.test(trimmed)) {
    return `${label} can only contain letters, spaces, hyphens, apostrophes and periods.`;
  }
  return "";
}

/** True when a name is usable as it stands (an empty one included — see nameIssue). */
export const nameIsValid = (value) => nameIssue(value) === "";

/** Quasar `:rules` for a name field. <app-text-field :rules="nameRules('First Name', { required: true })"
    /> */
export function nameRules (label = "This name", { required = false } = {}) {
  const rules = [];
  if (required) {
    rules.push((v) => !!String(v ?? "").trim() || `${label} is required.`);
  }
  rules.push((v) => nameIssue(v, label) || true);
  return rules;
}
