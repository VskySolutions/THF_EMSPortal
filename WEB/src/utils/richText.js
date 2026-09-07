// Rich-text helpers shared by every editor-backed field (descriptions, notes).

const ESCAPES = { "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" };

export const escapeHtml = (value) => (value || "").replace(/[&<>"']/g, (c) => ESCAPES[c]);

// Markup the editors can produce; everything else is dropped along with its attributes.
const DANGEROUS = "script,style,iframe,object,embed,link,meta,form,input,button,svg,math";

/// Allowlist pass over stored HTML: removes active elements, inline event handlers and script-bearing URLs.
export const sanitizeHtml = (html) => {
  const doc = new DOMParser().parseFromString(html || "", "text/html");
  doc.querySelectorAll(DANGEROUS).forEach((n) => n.remove());
  doc.querySelectorAll("*").forEach((el) => {
    Array.from(el.attributes).forEach((attr) => {
      const name = attr.name.toLowerCase();
      if (name.startsWith("on")) el.removeAttribute(attr.name);
      else if ((name === "href" || name === "src") && /^\s*(javascript|data|vbscript):/i.test(attr.value)) {
        el.removeAttribute(attr.name);
      }
    });
    // Anything leaving the app opens in a new tab without handing over the opener window.
    if (el.tagName === "A" && el.hasAttribute("href")) {
      el.setAttribute("target", "_blank");
      el.setAttribute("rel", "noopener noreferrer");
    }
  });
  return doc.body.innerHTML;
};

// True when the value looks like editor output rather than text someone typed before this field was rich.
export const isHtml = (value) => /<[a-z][\s\S]*>/i.test(value || "");

/// Display HTML for a stored value. Legacy plain text is escaped and keeps its line breaks, so records
/// written before the field became rich text still read correctly instead of collapsing to one line.
export const renderRichText = (value) => {
  if (!value) return "";
  return isHtml(value) ? sanitizeHtml(value) : escapeHtml(value).replace(/\n/g, "<br>");
};

/// The visible text only — for table cells, tooltips, and anywhere markup would be noise. Block-level
/// tags become spaces so "<p>a</p><p>b</p>" reads as "a b" rather than "ab".
export const stripHtml = (value) => {
  if (!value) return "";
  if (!isHtml(value)) return value;
  const doc = new DOMParser().parseFromString(sanitizeHtml(value), "text/html");
  return (doc.body.textContent || "").replace(/\s+/g, " ").trim();
};

/// True when an editor holds something a reader would see (an emptied editor still posts markup — a
/// stray <br>, or CKEditor's "<p>&nbsp;</p>").
export const hasRichTextContent = (html) =>
  !!(html || "").replace(/<br\s*\/?>/gi, "").replace(/<[^>]+>/g, "").replace(/&nbsp;/gi, " ").trim();
