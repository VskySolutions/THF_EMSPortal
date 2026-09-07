import { ufConversationApi } from "services/api";
import { escapeHtml } from "utils/richText";

// The single definition of what an @mention IS, shared by every editor that supports them.

/// Class on the rendered token. Styled in app.scss; also the upcast hook for CKEditor.
export const MENTION_CLASS = "uf-mention";

/// Attribute carrying the mentioned user's id. This — not the visible text — is what recipients derive from.
export const MENTION_ID_ATTR = "data-user-id";

/// The stored markup for one mention. contenteditable=false makes it delete as a single unit in q-editor.
export const mentionTokenHtml = (user) =>
  `<span class="${MENTION_CLASS}" ${MENTION_ID_ATTR}="${escapeHtml(user.userId)}" contenteditable="false">` +
  `@${escapeHtml(user.name)}</span>`;

/// The mentioned user ids in a stored value, deduped. Read from the markup rather than tracked as the user
/// types, so it stays correct when someone deletes a mention, pastes one, or edits the HTML directly.
export const extractMentionIds = (html) => {
  const doc = new DOMParser().parseFromString(html || "", "text/html");
  const ids = Array.from(doc.querySelectorAll(`[${MENTION_ID_ATTR}]`))
    .map((el) => el.getAttribute(MENTION_ID_ATTR))
    .filter(Boolean);
  return [...new Set(ids)];
};

/// Mentionable users matching a typed term. Never throws — an autocomplete that explodes mid-keystroke is
/// worse than one that comes back empty.
export const fetchMentionCandidates = async (term) => {
  try {
    return (await ufConversationApi.mentionCandidates(term)) || [];
  } catch {
    return [];
  }
};
