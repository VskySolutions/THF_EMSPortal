// The user import file: its heading line, the Excel template, and turning rows back into CSV text.

const HEADINGS = ["MaconomyNumber", "FirstName", "LastName", "Email", "Phone", "Department", "Role", "Status"];
const FIELDS = ["maconomyNumber", "firstName", "lastName", "email", "phone", "department", "roles", "accountStatus"];

// The most one file may hold; the template's text-formatted rows run that far.
const MAX_ROWS = 500;

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

// Text cells: whatever is typed keeps its shape, a phone number its plus and a Maconomy number its leading zero.
const text = (value) => ({ value: value || "", type: String, format: "@" });
const heading = (value) => ({ value, type: String, fontWeight: "bold" });

// Sheet 1 is the file to fill in; sheet 2 lists what the Role and Department cells may hold.
export const downloadUserImportTemplate = async ({ roles = [], departments = [] } = {}) => {
  const { default: writeExcelFile } = await import("write-excel-file/universal");

  const notes = [
    "FirstName, LastName, Email and Role are required.",
    "Several roles go in one Role cell, separated by a semicolon or a comma.",
    "Role and Department must match a name on this sheet.",
    "Status is optional: Active or Inactive. Inactive switches the account off; Active never switches one back on.",
    "With \"Update users that already exist\" ticked, a line whose Email is already a user updates that user.",
    "On an update the Email is never changed, a blank cell leaves its value alone, and the Role cell replaces the user's roles.",
    "Fill in the Users sheet only, then import this workbook as it is."
  ];
  const length = Math.max(roles.length, departments.length, notes.length);
  const lists = Array.from({ length }, (_, i) => [text(roles[i]), text(departments[i]), text(notes[i])]);
  const blank = Array.from({ length: MAX_ROWS }, () => HEADINGS.map(() => text("")));

  const blob = await writeExcelFile([
    {
      sheet: "Users",
      data: [HEADINGS.map(heading), ...blank],
      columns: [18, 18, 18, 32, 18, 18, 36, 12].map((width) => ({ width })),
      stickyRowsCount: 1
    },
    {
      sheet: "Roles",
      data: [["Role", "Department", "Notes"].map(heading), ...lists],
      columns: [{ width: 30 }, { width: 24 }, { width: 110 }],
      stickyRowsCount: 1
    }
  ]).toBlob();
  download("users-import-template.xlsx", blob);
};

// A workbook cell as the text somebody would have typed in a CSV.
const cellText = (value) => {
  if (value == null) return "";
  if (value instanceof Date) return value.toISOString().slice(0, 10);
  return String(value).trim();
};

// The chosen file as CSV text, which is what the server reads: an Excel workbook gives its first sheet.
export const userImportFileToCsv = async (file) => {
  if (!/\.xlsx$/i.test(file.name)) return file.text();
  const { readSheet } = await import("read-excel-file/universal");
  const rows = await readSheet(file);
  return toCsv(rows.map((row) => row.map(cellText)));
};

// The rows as a file that can be corrected and loaded again; the reason each failed goes in a last column.
export const userImportRowsToCsv = (rows) =>
  toCsv([[...HEADINGS, "Error"], ...rows.map((r) => [...FIELDS.map((f) => r[f]), r.error])]);

export const downloadCsv = (fileName, text) => download(fileName, new Blob([text], { type: "text/csv;charset=utf-8" }));
