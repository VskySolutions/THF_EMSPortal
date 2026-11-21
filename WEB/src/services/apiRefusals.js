// A failed call that is the server saying "not like that" — a validation, a rule, a right the caller
// lacks — is the user's to put right and is shown as a WARNING. Anything else (5xx, no answer, not found)
// is an error, and only errors are red.
const REFUSAL_STATUSES = [400, 403, 409, 413, 415, 422];

export const isApiRefusal = (error) => REFUSAL_STATUSES.includes(error?.response?.status);

// Only the sentence reaches a toast or a save indicator, never the error, so what kind of answer it was
// is kept by the sentence. The latest answer for a sentence wins; old ones fall off the end.
const MAX_REMEMBERED = 50;
const refusals = new Set();

/** Called by getApiErrorMessage for every sentence it hands out. */
export function noteApiMessage (message, error) {
  if (typeof message !== "string" || !message) return;
  refusals.delete(message);
  if (!isApiRefusal(error)) return;
  refusals.add(message);
  if (refusals.size > MAX_REMEMBERED) refusals.delete(refusals.values().next().value);
}

/** Whether this sentence came from the API as a refusal: draw it as a warning, not as an error. */
export const isRefusalMessage = (message) => typeof message === "string" && refusals.has(message);
