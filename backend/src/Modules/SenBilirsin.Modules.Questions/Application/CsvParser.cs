using System.Text;

namespace SenBilirsin.Modules.Questions.Application;

/// <summary>
/// Küçük, bağımlılıksız CSV okuyucu (RFC 4180 tırnak kuralları). Ayraç ilk satırdan sezilir:
/// Türkçe Excel genellikle noktalı virgül (;) kullanır. Tırnak içinde satır sonu ve ayraç olabilir.
/// </summary>
public static class CsvParser
{
    public static List<string[]> Parse(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..]; // BOM
        var delimiter = DetectDelimiter(text);

        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldWasQuoted = false;

        void EndField()
        {
            fields.Add(fieldWasQuoted ? field.ToString() : field.ToString().Trim());
            field.Clear();
            fieldWasQuoted = false;
        }

        void EndRow()
        {
            EndField();
            // Tamamen boş satırları atla
            if (!(fields.Count == 1 && fields[0].Length == 0)) rows.Add(fields.ToArray());
            fields.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
                fieldWasQuoted = true;
            }
            else if (c == delimiter) EndField();
            else if (c == '\r') { /* \n ile birlikte işlenir */ }
            else if (c == '\n') EndRow();
            else field.Append(c);
        }

        if (field.Length > 0 || fields.Count > 0 || fieldWasQuoted) EndRow();
        return rows;
    }

    private static char DetectDelimiter(string text)
    {
        var end = text.IndexOf('\n');
        var header = end < 0 ? text : text[..end];
        char[] candidates = [';', ',', '\t'];
        return candidates.OrderByDescending(c => header.Count(x => x == c)).First();
    }
}
