// The user import file: its heading line, the Excel template, and turning rows back into CSV text.
import { writeXlsx, readXlsxFirstSheet } from "utils/xlsxLite";

const HEADINGS = ["MaconomyNumber", "FirstName", "LastName", "Email", "Phone", "Department", "Role"];
const FIELDS = ["maconomyNumber", "firstName", "lastName", "email", "phone", "department", "roles"];

// A cell is quoted when it holds a comma, a quote or a line break — which is what a list of roles does.
const cell = (value) => {
  const text = value == null ? "" : String(value);
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, "\"\"")}"` : text;
};

const toCsv = (rows) => rows.map((row) => row.map(cell).join(",")).join("\r\n") + "\r\n";

const download = (fileName, blob) => {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
};

// Sheet 1 is the file to fill in; sheet 2 lists what the Role and Department cells may hold.
export const downloadUserImportTemplate = ({ roles = [], departments = [] } = {}) => {
  const notes = [
    "FirstName, LastName, Email and Role are required.",
    "Several roles go in one Role cell, separated by a semicolon or a comma.",
    "Role and Department must match a name on this sheet.",
    "Fill in the Users sheet only, then import this workbook as it is."
  ];
  const length = Math.max(roles.length, departments.length, notes.length);
  const lists = Array.from({ length }, (_, i) => [roles[i] || "", departments[i] || "", notes[i] || ""]);

  download("users-import-template.xlsx", writeXlsx([
    { name: "Users", rows: [HEADINGS], widths: [18, 18, 18, 32, 18, 18, 36] },
    { name: "Roles", rows: [["Role", "Department", "Notes"], ...lists], widths: [30, 24, 70] }
  ]));
};

// The chosen file as CSV text, which is what the server reads: an Excel workbook gives its first sheet.
export const userImportFileToCsv = async (file) =>
  /\.xlsx$/i.test(file.name) ? toCsv(await readXlsxFirstSheet(await file.arrayBuffer())) : file.text();

// The rows as a file that can be corrected and loaded again; the reason each failed goes in a last column.
export const userImportRowsToCsv = (rows) =>
  toCsv([[...HEADINGS, "Error"], ...rows.map((r) => [...FIELDS.map((f) => r[f]), r.error])]);

export const downloadCsv = (fileName, text) => download(fileName, new Blob([text], { type: "text/csv;charset=utf-8" }));
