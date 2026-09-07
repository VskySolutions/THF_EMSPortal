using System.Globalization;
using System.Text;

namespace EmsPortal.Api.Storage;

/// <summary>Minimal, dependency-free PDF generator for the Universal Features "Export to PDF" feature.</summary>
public static class SimplePdfWriter
{
    private const int LinesPerPage = 48;
    private const int FontSize = 11;
    private const int Leading = 15;
    private const int StartY = 800;
    private const int LeftX = 50;

    /// <summary>Builds a PDF document from a title and body lines, returning the raw bytes.</summary>
    public static byte[] Build(string title, IReadOnlyList<string> lines)
    {
        var allLines = new List<string> { title, new string('=', Math.Min(title.Length, 60)), string.Empty };
        allLines.AddRange(lines);

        var pages = Chunk(allLines, LinesPerPage);
        if (pages.Count == 0)
        {
            pages.Add(new List<string>());
        }

        // Object numbering: 1 = Catalog, 2 = Pages, 3 = Font, then page+content pairs.
        var objects = new List<string>();
        var pageRefs = new List<int>();
        var firstPageObj = 4;
        for (var i = 0; i < pages.Count; i++)
        {
            var pageObj = firstPageObj + (i * 2);
            var contentObj = pageObj + 1;
            pageRefs.Add(pageObj);

            var content = BuildContentStream(pages[i]);
            var contentBytes = Encoding.ASCII.GetByteCount(content);

            objects.Add($"{pageObj} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentObj} 0 R >>\nendobj\n");
            objects.Add($"{contentObj} 0 obj\n<< /Length {contentBytes} >>\nstream\n{content}endstream\nendobj\n");
        }

        var kids = string.Join(" ", pageRefs.Select(r => $"{r} 0 R"));
        var header = new List<string>
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            $"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {pageRefs.Count} >>\nendobj\n",
            "3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n",
        };

        var body = new StringBuilder();
        body.Append("%PDF-1.4\n");

        var offsets = new List<int>();
        var allObjects = header.Concat(objects).ToList();
        foreach (var obj in allObjects)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(body.ToString()));
            body.Append(obj);
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(body.ToString());
        var count = allObjects.Count + 1; // +1 for the free object 0
        body.Append($"xref\n0 {count}\n");
        body.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            body.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        body.Append($"trailer\n<< /Size {count} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
        return Encoding.ASCII.GetBytes(body.ToString());
    }

    private static string BuildContentStream(IReadOnlyList<string> lines)
    {
        var sb = new StringBuilder();
        sb.Append("BT\n");
        sb.Append($"/F1 {FontSize} Tf\n");
        sb.Append($"{LeftX} {StartY} Td\n");
        sb.Append($"{Leading} TL\n");
        for (var i = 0; i < lines.Count; i++)
        {
            var text = Escape(lines[i]);
            sb.Append(i == 0 ? $"({text}) Tj\n" : $"({text}) '\n");
        }
        sb.Append("ET\n");
        return sb.ToString();
    }

    private static string Escape(string text)
    {
        // Replace non-ASCII (Helvetica WinAnsi safe subset only) and escape PDF string delimiters.
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            sb.Append(ch switch
            {
                '\\' => "\\\\",
                '(' => "\\(",
                ')' => "\\)",
                '\r' or '\n' or '\t' => " ",
                _ => ch > 0x7E ? "?" : ch.ToString(),
            });
        }

        return sb.ToString();
    }

    private static List<List<string>> Chunk(IReadOnlyList<string> lines, int size)
    {
        var result = new List<List<string>>();
        for (var i = 0; i < lines.Count; i += size)
        {
            result.Add(lines.Skip(i).Take(size).ToList());
        }

        return result;
    }
}
