using System.Text;

namespace EmsPortal.Api.Users;

/// <summary>A small CSV reader: quoted cells, doubled quotes, commas and line breaks inside quotes.</summary>
internal static class CsvText
{
    /// <summary>Every record that has something in it, cells trimmed.</summary>
    public static List<string[]> Read(string content)
    {
        var records = new List<string[]>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;

        // Excel's "CSV UTF-8" starts with a byte order mark.
        var text = content.Length > 0 && content[0] == '﻿' ? content[1..] : content;

        void EndCell()
        {
            cells.Add(cell.ToString().Trim());
            cell.Clear();
        }

        void EndRecord()
        {
            EndCell();
            if (cells.Any(c => c.Length > 0))
            {
                records.Add(cells.ToArray());
            }
            cells.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                EndCell();
            }
            else if (c == '\n')
            {
                EndRecord();
            }
            else if (c != '\r')
            {
                cell.Append(c);
            }
        }

        EndRecord();
        return records;
    }
}
