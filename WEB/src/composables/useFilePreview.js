// Reading a file that is already STORED on the server — the counterpart to useFileDrop, which describes a
// File the browser is still holding.

import { mediaApi } from "services/api";
import { extOf, formatFileSize, iconForExtension, isImageExtension } from "composables/useFileDrop";

/** The media id to fetch a stored file's bytes by. */
export const mediaIdOf = (file) => file?.mediaId || file?.id || null;

/** What to call the file on screen. */
export const nameOf = (file) => file?.fileName || file?.originalFileName || file?.name || "Attachment";

/** The file's extension, with a leading dot, from whichever of the two places it is recorded in. */
export const extOfStored = (file) => {
  const declared = file?.fileExtension;
  return declared ? `.${String(declared).replace(/^\./, "").toLowerCase()}` : extOf(nameOf(file));
};

/** The Material icon for a stored file's type. */
export const iconForStored = (file) => iconForExtension(extOfStored(file));

/** True when the stored file is a picture. */
export const isImageStored = (file) =>
  (file?.mimeType || "").startsWith("image/") || isImageExtension(extOfStored(file));

/** "PDF · 1.2 MB", or just the size where the name carries no extension. */
export const describeStored = (file) => {
  const ext = extOfStored(file).replace(".", "").toUpperCase();
  const size = file?.fileSize || file?.size;
  return [ext, size ? formatFileSize(size) : ""].filter(Boolean).join(" · ");
};

// The tab holds the blob for as long as it is rendering it; the URL only has to survive the navigation.
const REVOKE_AFTER_MS = 120000;

/** Fetches a stored file's bytes. */
export const fetchStoredBytes = (file, fetchBlob = null) => {
  if (fetchBlob) return fetchBlob(file);
  const mediaId = mediaIdOf(file);
  if (!mediaId) return Promise.reject(new Error("This file has no stored copy to open."));
  return mediaApi.content(mediaId);
};

/** Opens a stored file in a new browser tab. */
export async function openStoredFile (file, fetchBlob = null) {
  const tab = window.open("", "_blank");
  try {
    const blob = await fetchStoredBytes(file, fetchBlob);
    // A blob with no type opens as a download prompt rather than in the viewer, and the server's own
    // Content-Type is the better answer for a row that recorded one.
    const typed = file?.mimeType && !blob.type ? blob.slice(0, blob.size, file.mimeType) : blob;
    const url = URL.createObjectURL(typed);
    if (tab) {
      tab.location.href = url;
    } else {
      saveBlob(url, nameOf(file));
    }
    setTimeout(() => URL.revokeObjectURL(url), REVOKE_AFTER_MS);
  } catch (err) {
    // A tab left sitting on about:blank after a failed fetch is worse than no tab at all.
    tab?.close();
    throw err;
  }
}

/** Downloads a stored file under its own name rather than opening it. */
export async function downloadStoredFile (file, fetchBlob = null) {
  const blob = await fetchStoredBytes(file, fetchBlob);
  const url = URL.createObjectURL(blob);
  saveBlob(url, nameOf(file));
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function saveBlob (url, fileName) {
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
}
