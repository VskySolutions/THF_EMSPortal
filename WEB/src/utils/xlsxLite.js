// Just enough Excel for the user import: write a workbook of text sheets, and read the first sheet of
// one back as rows of text. No formulas, dates or styling beyond a bold heading line.

const encoder = new TextEncoder();
const decoder = new TextDecoder();

const xml = (text) =>
  String(text ?? "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

const unxml = (text) =>
  text
    .replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, "\"").replace(/&apos;/g, "'")
    .replace(/&#x([0-9a-f]+);/gi, (_, hex) => String.fromCodePoint(parseInt(hex, 16)))
    .replace(/&#(\d+);/g, (_, dec) => String.fromCodePoint(Number(dec)))
    .replace(/&amp;/g, "&");

// A → 0, B → 1, … AA → 26.
const columnIndex = (ref) =>
  [...ref.replace(/[^A-Z]/g, "")].reduce((n, ch) => n * 26 + ch.charCodeAt(0) - 64, 0) - 1;

const columnName = (index) => {
  let name = "";
  for (let n = index + 1; n > 0; n = Math.floor((n - 1) / 26)) name = String.fromCharCode(65 + ((n - 1) % 26)) + name;
  return name;
};

// ---- Zip (stored, no compression: the files are a few kilobytes) ----

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});

const crc32 = (bytes) => {
  let crc = 0xffffffff;
  for (const b of bytes) crc = CRC_TABLE[(crc ^ b) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
};

const zip = (files) => {
  const parts = [];
  const central = [];
  let offset = 0;

  for (const [name, text] of files) {
    const nameBytes = encoder.encode(name);
    const data = encoder.encode(text);
    const crc = crc32(data);

    const local = new DataView(new ArrayBuffer(30));
    local.setUint32(0, 0x04034b50, true);
    local.setUint16(4, 20, true);
    local.setUint16(6, 0x0800, true); // names are UTF-8
    local.setUint16(10, 0, true);
    local.setUint16(12, 0x21, true); // 1 Jan 1980: the date carries no meaning here
    local.setUint32(14, crc, true);
    local.setUint32(18, data.length, true);
    local.setUint32(22, data.length, true);
    local.setUint16(26, nameBytes.length, true);
    parts.push(new Uint8Array(local.buffer), nameBytes, data);

    const entry = new DataView(new ArrayBuffer(46));
    entry.setUint32(0, 0x02014b50, true);
    entry.setUint16(4, 20, true);
    entry.setUint16(6, 20, true);
    entry.setUint16(8, 0x0800, true);
    entry.setUint16(14, 0x21, true);
    entry.setUint32(16, crc, true);
    entry.setUint32(20, data.length, true);
    entry.setUint32(24, data.length, true);
    entry.setUint16(28, nameBytes.length, true);
    entry.setUint32(42, offset, true);
    central.push(new Uint8Array(entry.buffer), nameBytes);

    offset += 30 + nameBytes.length + data.length;
  }

  const centralSize = central.reduce((n, p) => n + p.length, 0);
  const end = new DataView(new ArrayBuffer(22));
  end.setUint32(0, 0x06054b50, true);
  end.setUint16(8, files.length, true);
  end.setUint16(10, files.length, true);
  end.setUint32(12, centralSize, true);
  end.setUint32(16, offset, true);

  return new Blob([...parts, ...central, new Uint8Array(end.buffer)], {
    type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
  });
};

const inflate = async (bytes) =>
  new Uint8Array(await new Response(new Blob([bytes]).stream().pipeThrough(new DecompressionStream("deflate-raw"))).arrayBuffer());

// name → text for every file in the archive.
const unzip = async (buffer) => {
  const view = new DataView(buffer);
  const bytes = new Uint8Array(buffer);

  let end = buffer.byteLength - 22;
  while (end >= 0 && view.getUint32(end, true) !== 0x06054b50) end--;
  if (end < 0) throw new Error("Not an Excel workbook.");

  const files = new Map();
  let at = view.getUint32(end + 16, true);
  for (let i = view.getUint16(end + 10, true); i > 0; i--) {
    const method = view.getUint16(at + 10, true);
    const size = view.getUint32(at + 20, true);
    const nameLength = view.getUint16(at + 28, true);
    const local = view.getUint32(at + 42, true);
    const name = decoder.decode(bytes.subarray(at + 46, at + 46 + nameLength));

    const start = local + 30 + view.getUint16(local + 26, true) + view.getUint16(local + 28, true);
    const data = bytes.subarray(start, start + size);
    if (method !== 0 && method !== 8) throw new Error("Not an Excel workbook.");
    files.set(name, decoder.decode(method === 8 ? await inflate(data) : data));

    at += 46 + nameLength + view.getUint16(at + 30, true) + view.getUint16(at + 32, true);
  }
  return files;
};

// ---- Writing ----

// Every cell is text, and so is whatever is typed under it: a phone number keeps its plus, a Maconomy
// number its leading zero.
const sheetXml = (rows, widths) => {
  const cols = widths.map((w, i) => `<col min="${i + 1}" max="${i + 1}" width="${w}" style="2" customWidth="1"/>`).join("");
  const body = rows.map((row, r) =>
    `<row r="${r + 1}">${row.map((value, c) =>
      `<c r="${columnName(c)}${r + 1}" t="inlineStr" s="${r === 0 ? 1 : 2}"><is><t xml:space="preserve">${xml(value)}</t></is></c>`
    ).join("")}</row>`
  ).join("");
  return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
    "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
    `<cols>${cols}</cols><sheetData>${body}</sheetData></worksheet>`;
};

// sheets: [{ name, rows: [[text, …], …], widths: [number, …] }] — the first row of each is its heading line.
export const writeXlsx = (sheets) => {
  const head = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
  const main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
  const rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
  const pkg = "http://schemas.openxmlformats.org/package/2006";

  return zip([
    ["[Content_Types].xml",
      `${head}<Types xmlns="${pkg}/content-types">` +
      "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
      "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
      "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
      "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
      sheets.map((_, i) => `<Override PartName="/xl/worksheets/sheet${i + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>`).join("") +
      "</Types>"],
    ["_rels/.rels",
      `${head}<Relationships xmlns="${pkg}/relationships">` +
      `<Relationship Id="rId1" Type="${rel}/officeDocument" Target="xl/workbook.xml"/></Relationships>`],
    ["xl/workbook.xml",
      `${head}<workbook xmlns="${main}" xmlns:r="${rel}"><sheets>` +
      sheets.map((s, i) => `<sheet name="${xml(s.name)}" sheetId="${i + 1}" r:id="rId${i + 1}"/>`).join("") +
      "</sheets></workbook>"],
    ["xl/_rels/workbook.xml.rels",
      `${head}<Relationships xmlns="${pkg}/relationships">` +
      sheets.map((_, i) => `<Relationship Id="rId${i + 1}" Type="${rel}/worksheet" Target="worksheets/sheet${i + 1}.xml"/>`).join("") +
      `<Relationship Id="rId${sheets.length + 1}" Type="${rel}/styles" Target="styles.xml"/></Relationships>`],
    // Three cell formats: plain, bold text (heading line), text.
    ["xl/styles.xml",
      `${head}<styleSheet xmlns="${main}">` +
      "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
      "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
      "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
      "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
      "<cellXfs count=\"3\">" +
      "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
      "<xf numFmtId=\"49\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyNumberFormat=\"1\"/>" +
      "<xf numFmtId=\"49\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
      "</cellXfs></styleSheet>"],
    ...sheets.map((s, i) => [`xl/worksheets/sheet${i + 1}.xml`, sheetXml(s.rows, s.widths)])
  ]);
};

// ---- Reading ----

const textOf = (fragment) =>
  [...fragment.replace(/<rPh[\s\S]*?<\/rPh>/g, "").matchAll(/<t\b[^>]*>([\s\S]*?)<\/t>/g)].map((m) => unxml(m[1])).join("");

const attribute = (attributes, name) => attributes.match(new RegExp(`(?:^|\\s)${name}="([^"]*)"`))?.[1];

// The first sheet as rows of text, blanks filled in so every row lines up with the heading line.
export const readXlsxFirstSheet = async (buffer) => {
  const files = await unzip(buffer);

  // The first sheet in the workbook's own order, which is not always sheet1.xml.
  const firstId = files.get("xl/workbook.xml")?.match(/<sheet\b[^>]*\br:id="([^"]+)"/)?.[1];
  const target = firstId && [...(files.get("xl/_rels/workbook.xml.rels") || "").matchAll(/<Relationship\b([^>]*)>/g)]
    .map((m) => m[1]).find((a) => attribute(a, "Id") === firstId);
  const path = target ? attribute(target, "Target").replace(/^\/?(xl\/)?/, "xl/") : "xl/worksheets/sheet1.xml";
  const sheet = files.get(path);
  if (!sheet) throw new Error("The workbook has no sheet to read.");

  const shared = [...(files.get("xl/sharedStrings.xml") || "").matchAll(/<si\b[^>]*>([\s\S]*?)<\/si>/g)].map((m) => textOf(m[1]));

  const rows = [];
  for (const rowMatch of sheet.matchAll(/<row\b[^>]*>([\s\S]*?)<\/row>/g)) {
    const row = [];
    for (const [, attributes, inner = ""] of rowMatch[1].matchAll(/<c\b([^>]*?)(?:\/>|>([\s\S]*?)<\/c>)/g)) {
      const type = attribute(attributes, "t");
      const raw = inner.match(/<v\b[^>]*>([\s\S]*?)<\/v>/)?.[1];
      let value = "";
      if (type === "inlineStr") value = textOf(inner);
      else if (raw == null) value = "";
      else if (type === "s") value = shared[Number(raw)] ?? "";
      // A number as Excel shows it by default, not as it stores it (1.0E4, 0.30000000000000004).
      else if (!type || type === "n") value = String(Number(Number(raw).toPrecision(15)));
      else value = unxml(raw);
      row[columnIndex(attribute(attributes, "r") || columnName(row.length))] = value.trim();
    }
    rows.push(Array.from(row, (v) => v ?? ""));
  }
  return rows.filter((row) => row.some((v) => v !== ""));
};
