import { ref } from "vue";

// Drag-and-drop state + file validation shared by the file-upload components (AppSingleFileUpload,
// AppMultiFileUpload). Keeps the dropzone behaviour and accept/size rules identical everywhere.
export function useFileDrop () {
  const dragOver = ref(false);
  const onDragOver = () => { dragOver.value = true; };
  const onDragLeave = () => { dragOver.value = false; };
  return { dragOver, onDragOver, onDragLeave };
}

const IMAGE_EXTS = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg"];

/** The biggest file any upload on this platform accepts. */
export const MAX_UPLOAD_MB = 10;

/** The file types a business document can be, as an `accept` list. */
export const ATTACHMENT_ACCEPT = [
  ".pdf",
  ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg",
  ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
  ".txt", ".csv", ".rtf", ".md", ".json", ".xml",
  ".msg", ".eml",
  ".zip"
].join(",");

/** The caps on a SET of attachments, and the sentence that states all three. */
export const MAX_ATTACHMENT_FILES = 15;
export const MAX_ATTACHMENT_TOTAL_MB = 100;
export const ATTACHMENT_HINT =
  `Up to ${MAX_ATTACHMENT_FILES} files, ${MAX_UPLOAD_MB} MB each and ${MAX_ATTACHMENT_TOTAL_MB} MB in total.`;

// Lower-cased extension (with dot) of a file name, or "".
export function extOf (name) {
  const i = String(name || "").lastIndexOf(".");
  return i >= 0 ? String(name).slice(i).toLowerCase() : "";
}

/** An extension in the one shape the helpers below compare against: lower-cased, with a leading dot. */
export function normalizeExt (ext) {
  const raw = String(ext || "").trim().toLowerCase();
  if (!raw) return "";
  return raw.startsWith(".") ? raw : `.${raw}`;
}

// True for an extension the browser renders as a picture.
export function isImageExtension (ext) {
  return IMAGE_EXTS.includes(normalizeExt(ext));
}

// True when a File is an image (by MIME type or extension) — used to show a thumbnail preview.
export function isImageFile (file) {
  return (file?.type || "").startsWith("image/") || isImageExtension(extOf(file?.name));
}

// A Material icon representing a file type, by extension. Takes either shape (".pdf" or "pdf").
export function iconForExtension (ext) {
  const e = normalizeExt(ext);
  if (IMAGE_EXTS.includes(e)) return "o_image";
  if (e === ".pdf") return "o_picture_as_pdf";
  if ([".xls", ".xlsx", ".csv"].includes(e)) return "o_table_chart";
  if ([".doc", ".docx", ".txt", ".rtf", ".md"].includes(e)) return "o_description";
  if ([".ppt", ".pptx"].includes(e)) return "o_slideshow";
  if ([".zip", ".rar", ".7z"].includes(e)) return "o_folder_zip";
  return "o_insert_drive_file";
}

// A Material icon representing a picked File's type (for non-image previews).
export function iconForFile (file) {
  return iconForExtension(extOf(file?.name));
}

// Validate a picked/dropped FileList against an accept list (extensions, e.g. ".pdf,.png") and an
// optional max size (MB). Returns the accepted files plus a human-readable error for any rejection.
export function validateFiles (fileList, { accept = "", maxSizeMb = null } = {}) {
  const exts = String(accept || "")
    .split(",")
    .map((s) => s.trim().toLowerCase())
    .filter((s) => s.startsWith("."));
  const maxBytes = maxSizeMb ? maxSizeMb * 1024 * 1024 : null;

  const accepted = [];
  let error = null;
  for (const file of Array.from(fileList || [])) {
    if (exts.length && !exts.includes(extOf(file.name))) {
      error = `"${file.name}" is not an allowed file type.`;
      continue;
    }
    if (maxBytes && file.size > maxBytes) {
      error = `"${file.name}" exceeds the ${maxSizeMb} MB limit.`;
      continue;
    }
    accepted.push(file);
  }
  return { accepted, error };
}

// Human-readable byte size, shared by the upload components' selected-file rows.
export function formatFileSize (bytes) {
  if (!bytes) return "0 B";
  const units = ["B", "KB", "MB", "GB"];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return `${(bytes / Math.pow(1024, i)).toFixed(i ? 1 : 0)} ${units[i]}`;
}
