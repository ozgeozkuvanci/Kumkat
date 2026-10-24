using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kumkat.Core
{
    /// <summary>CSV / JSON / Markdown dönüşümleri (harici araç gerektirmez).</summary>
    internal static class DataEngine
    {
        private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static readonly JsonWriterOptions PrettyJson = new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // Türkçe karakterler \u015f olmasın
        };

        private static readonly JsonWriterOptions MinJson = new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static void Convert(string groupId, string actionId, string input, string output)
        {
            switch ($"{groupId}/{actionId}")
            {
                case "csv/json": CsvToJson(input, output); break;
                case "csv/md": CsvToMarkdown(input, output); break;
                case "json/csv": JsonToCsv(input, output); break;
                case "json/pretty": RewriteJson(input, output, PrettyJson); break;
                case "json/min": RewriteJson(input, output, MinJson); break;
                case "markdown/html":
                case "markdown/pdf": // PDF: önce HTML, sonra Converter LibreOffice'e verir
                    File.WriteAllText(output, MarkdownToHtmlDocument(File.ReadAllText(input), Path.GetFileNameWithoutExtension(input)), Utf8NoBom);
                    break;
                case "markdown/txt": File.WriteAllText(output, MarkdownToText(File.ReadAllText(input)), Utf8NoBom); break;
                default: throw new NotSupportedException($"Veri dönüşümü tanımlı değil: {groupId}/{actionId}");
            }
        }

        // ---------------------------------------------------------------- CSV

        public static List<List<string>> ReadCsv(string path)
        {
            string text = File.ReadAllText(path); // BOM'u otomatik algılar
            char delim = DetectDelimiter(path, text);
            return ParseCsv(text, delim);
        }

        private static char DetectDelimiter(string path, string text)
        {
            if (path.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)) return '\t';
            int nl = text.IndexOf('\n');
            string first = nl >= 0 ? text.Substring(0, nl) : text;
            int commas = first.Count(c => c == ','), semis = first.Count(c => c == ';'), tabs = first.Count(c => c == '\t');
            if (tabs > commas && tabs > semis) return '\t';
            return semis > commas ? ';' : ',';
        }

        /// <summary>RFC 4180: tırnaklı alanlar, "" kaçışı, alan içinde satır sonu.</summary>
        public static List<List<string>> ParseCsv(string text, char delim)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            int i = 0;
            if (text.Length > 0 && text[0] == '\uFEFF') i = 1;

            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == delim) { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\r') { /* \r\n içinde yok say */ }
                else if (c == '\n')
                {
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new List<string>();
                }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            // tamamen boş satırları at
            rows.RemoveAll(r => r.All(string.IsNullOrWhiteSpace));
            return rows;
        }

        private static string CsvEscape(string s)
        {
            bool needs = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 || s.StartsWith(" ") || s.EndsWith(" ");
            return needs ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        private static void CsvToJson(string input, string output)
        {
            var rows = ReadCsv(input);
            using var fs = new FileStream(output, FileMode.Create);
            using var w = new Utf8JsonWriter(fs, PrettyJson);
            w.WriteStartArray();
            if (rows.Count > 0)
            {
                var headers = UniqueHeaders(rows[0]);
                foreach (var r in rows.Skip(1))
                {
                    w.WriteStartObject();
                    for (int c = 0; c < headers.Count; c++)
                    {
                        string v = c < r.Count ? r[c] : "";
                        w.WritePropertyName(headers[c]);
                        WriteScalar(w, v);
                    }
                    w.WriteEndObject();
                }
            }
            w.WriteEndArray();
        }

        private static List<string> UniqueHeaders(List<string> raw)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var result = new List<string>();
            for (int i = 0; i < raw.Count; i++)
            {
                string h = raw[i].Trim();
                if (h.Length == 0) h = $"sütun{i + 1}";
                if (seen.TryGetValue(h, out int n)) { seen[h] = n + 1; h = $"{h}_{n + 1}"; }
                else seen[h] = 1;
                result.Add(h);
            }
            return result;
        }

        private static readonly Regex JsonNumber = new Regex(@"^-?(0|[1-9]\d{0,14})(\.\d+)?$", RegexOptions.Compiled);

        private static void WriteScalar(Utf8JsonWriter w, string v)
        {
            string t = v.Trim();
            if (JsonNumber.IsMatch(t) && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                w.WriteRawValue(t);
            else if (t == "true" || t == "false")
                w.WriteBooleanValue(t == "true");
            else
                w.WriteStringValue(v);
        }

        private static void CsvToMarkdown(string input, string output)
        {
            var rows = ReadCsv(input);
            var sb = new StringBuilder();
            if (rows.Count > 0)
            {
                int cols = rows.Max(r => r.Count);
                string Cell(List<string> r, int c) => (c < r.Count ? r[c] : "").Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>");
                sb.Append("| ").Append(string.Join(" | ", Enumerable.Range(0, cols).Select(c => Cell(rows[0], c)))).Append(" |\n");
                sb.Append("|").Append(string.Concat(Enumerable.Repeat(" --- |", cols))).Append('\n');
                foreach (var r in rows.Skip(1))
                    sb.Append("| ").Append(string.Join(" | ", Enumerable.Range(0, cols).Select(c => Cell(r, c)))).Append(" |\n");
            }
            File.WriteAllText(output, sb.ToString(), Utf8NoBom);
        }

        // ---------------------------------------------------------------- JSON

        private static void RewriteJson(string input, string output, JsonWriterOptions options)
        {
            using var src = File.OpenRead(input);
            using var doc = JsonDocument.Parse(src, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            using var fs = new FileStream(output, FileMode.Create);
            using var w = new Utf8JsonWriter(fs, options);
            doc.WriteTo(w);
        }

        private static void JsonToCsv(string input, string output)
        {
            using var src = File.OpenRead(input);
            using var doc = JsonDocument.Parse(src, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            JsonElement root = doc.RootElement;
            JsonElement array = default;
            bool found = false;
            if (root.ValueKind == JsonValueKind.Array)
            {
                array = root;
                found = true;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // { "kayitlar": [ ... ] } biçimi: ilk dizi alanını al
                foreach (var p in root.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.Array)
                    {
                        array = p.Value;
                        found = true;
                        break;
                    }
                }
            }
            if (!found) throw new InvalidDataException(Loc.T("JSON bir dizi (veya dizi içeren bir nesne) olmalı."));

            var headers = new List<string>();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                foreach (var p in item.EnumerateObject())
                    if (!headers.Contains(p.Name)) headers.Add(p.Name);
            }

            var sb = new StringBuilder();
            bool scalarArray = headers.Count == 0;
            if (scalarArray) headers.Add("değer");
            sb.Append(string.Join(",", headers.Select(CsvEscape))).Append("\r\n");

            foreach (var item in array.EnumerateArray())
            {
                IEnumerable<string> cells;
                if (item.ValueKind == JsonValueKind.Object)
                    cells = headers.Select(h => item.TryGetProperty(h, out var v) ? ScalarText(v) : "");
                else
                    cells = new[] { ScalarText(item) };
                sb.Append(string.Join(",", cells.Select(CsvEscape))).Append("\r\n");
            }
            // BOM: Excel Türkçe karakterleri doğru açsın
            File.WriteAllText(output, sb.ToString(), Utf8Bom);
        }

        private static string ScalarText(JsonElement v)
        {
            switch (v.ValueKind)
            {
                case JsonValueKind.String: return v.GetString() ?? "";
                case JsonValueKind.Number: return v.GetRawText();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                case JsonValueKind.Null:
                case JsonValueKind.Undefined: return "";
                default: return v.GetRawText(); // iç içe nesne/dizi: ham JSON
            }
        }

        // ------------------------------------------------------------ Markdown

        public static string MarkdownToHtmlDocument(string md, string title)
        {
            string body = MarkdownToHtml(md);
            return "<!doctype html>\n<html lang=\"tr\"><head><meta charset=\"utf-8\">" +
                   $"<title>{WebUtility.HtmlEncode(title)}</title>\n<style>\n" +
                   "body{font-family:'Segoe UI',system-ui,sans-serif;max-width:46em;margin:2em auto;padding:0 1em;line-height:1.6;color:#1f2328}\n" +
                   "h1,h2,h3{line-height:1.25}h1,h2{border-bottom:1px solid #d8dee4;padding-bottom:.3em}\n" +
                   "code{background:#f2f2f2;padding:.1em .35em;border-radius:4px;font-family:Consolas,monospace}\n" +
                   "pre{background:#f6f8fa;padding:1em;border-radius:6px;overflow:auto}pre code{background:none;padding:0}\n" +
                   "blockquote{margin:0;padding:0 1em;color:#59636e;border-left:4px solid #d1d9e0}\n" +
                   "table{border-collapse:collapse}th,td{border:1px solid #d1d9e0;padding:.4em .8em}th{background:#f6f8fa}\n" +
                   "img{max-width:100%}\n</style></head>\n<body>\n" + body + "</body></html>\n";
        }

        public static string MarkdownToHtml(string md)
        {
            var lines = md.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var html = new StringBuilder();
            var para = new List<string>();
            string? listType = null;
            int i = 0;

            void FlushPara()
            {
                if (para.Count == 0) return;
                html.Append("<p>").Append(Inline(string.Join("\n", para)).Replace("\n", "<br>\n")).Append("</p>\n");
                para.Clear();
            }

            void CloseList()
            {
                if (listType == null) return;
                html.Append("</").Append(listType).Append(">\n");
                listType = null;
            }

            while (i < lines.Length)
            {
                string line = lines[i];
                string trimmed = line.Trim();

                // kod bloğu
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                {
                    FlushPara(); CloseList();
                    string fence = trimmed.Substring(0, 3);
                    string lang = trimmed.Substring(3).Trim();
                    var code = new StringBuilder();
                    i++;
                    while (i < lines.Length && !lines[i].Trim().StartsWith(fence)) { code.Append(lines[i]).Append('\n'); i++; }
                    i++;
                    html.Append(lang.Length > 0 ? $"<pre><code class=\"language-{WebUtility.HtmlEncode(lang)}\">" : "<pre><code>")
                        .Append(WebUtility.HtmlEncode(code.ToString())).Append("</code></pre>\n");
                    continue;
                }

                if (trimmed.Length == 0) { FlushPara(); CloseList(); i++; continue; }

                // başlık
                var h = Regex.Match(trimmed, @"^(#{1,6})\s+(.*?)\s*#*$");
                if (h.Success)
                {
                    FlushPara(); CloseList();
                    int level = h.Groups[1].Value.Length;
                    html.Append($"<h{level}>").Append(Inline(h.Groups[2].Value)).Append($"</h{level}>\n");
                    i++; continue;
                }

                // yatay çizgi
                if (Regex.IsMatch(trimmed, @"^([-*_])(\s*\1){2,}$"))
                {
                    FlushPara(); CloseList();
                    html.Append("<hr>\n"); i++; continue;
                }

                // tablo
                if (trimmed.StartsWith("|") && i + 1 < lines.Length && Regex.IsMatch(lines[i + 1].Trim(), @"^\|?\s*:?-{3,}"))
                {
                    FlushPara(); CloseList();
                    html.Append("<table>\n<thead><tr>");
                    foreach (var cell in SplitRow(trimmed)) html.Append("<th>").Append(Inline(cell)).Append("</th>");
                    html.Append("</tr></thead>\n<tbody>\n");
                    i += 2;
                    while (i < lines.Length && lines[i].Trim().StartsWith("|"))
                    {
                        html.Append("<tr>");
                        foreach (var cell in SplitRow(lines[i].Trim())) html.Append("<td>").Append(Inline(cell)).Append("</td>");
                        html.Append("</tr>\n");
                        i++;
                    }
                    html.Append("</tbody></table>\n");
                    continue;
                }

                // alıntı
                if (trimmed.StartsWith(">"))
                {
                    FlushPara(); CloseList();
                    var quote = new List<string>();
                    while (i < lines.Length && lines[i].Trim().StartsWith(">"))
                    {
                        quote.Add(lines[i].Trim().Substring(1).TrimStart());
                        i++;
                    }
                    html.Append("<blockquote>\n").Append(MarkdownToHtml(string.Join("\n", quote))).Append("</blockquote>\n");
                    continue;
                }

                // listeler
                var ul = Regex.Match(line, @"^\s*[-*+]\s+(.*)$");
                var ol = Regex.Match(line, @"^\s*\d+[.)]\s+(.*)$");
                if (ul.Success || ol.Success)
                {
                    FlushPara();
                    string want = ul.Success ? "ul" : "ol";
                    if (listType != want) { CloseList(); html.Append('<').Append(want).Append(">\n"); listType = want; }
                    string item = (ul.Success ? ul : ol).Groups[1].Value;
                    var task = Regex.Match(item, @"^\[( |x|X)\]\s+(.*)$");
                    if (task.Success)
                        item = (task.Groups[1].Value == " " ? "☐ " : "☑ ") + task.Groups[2].Value;
                    html.Append("<li>").Append(Inline(item)).Append("</li>\n");
                    i++; continue;
                }

                CloseList();
                para.Add(trimmed);
                i++;
            }
            FlushPara();
            CloseList();
            return html.ToString();
        }

        private static IEnumerable<string> SplitRow(string row)
        {
            string r = row.Trim();
            if (r.StartsWith("|")) r = r.Substring(1);
            if (r.EndsWith("|") && !r.EndsWith("\\|")) r = r.Substring(0, r.Length - 1);
            return Regex.Split(r, @"(?<!\\)\|").Select(c => c.Trim().Replace("\\|", "|"));
        }

        private static string Inline(string text)
        {
            // önce satır içi kodları koru
            var codes = new List<string>();
            text = Regex.Replace(text, @"`([^`]+)`", m =>
            {
                codes.Add("<code>" + WebUtility.HtmlEncode(m.Groups[1].Value) + "</code>");
                return "\u0000" + (codes.Count - 1) + "\u0000";
            });

            text = WebUtility.HtmlEncode(text);
            text = Regex.Replace(text, @"!\[([^\]]*)\]\(([^)\s]+)\)", "<img src=\"$2\" alt=\"$1\">");
            text = Regex.Replace(text, @"\[([^\]]+)\]\(([^)\s]+)\)", "<a href=\"$2\">$1</a>");
            text = Regex.Replace(text, @"(\*\*|__)(?=\S)(.+?)(?<=\S)\1", "<strong>$2</strong>");
            text = Regex.Replace(text, @"(?<![\w*])\*(?=\S)(.+?)(?<=\S)\*(?![\w*])", "<em>$1</em>");
            text = Regex.Replace(text, @"(?<!\w)_(?=\S)(.+?)(?<=\S)_(?!\w)", "<em>$1</em>");
            text = Regex.Replace(text, @"~~(?=\S)(.+?)(?<=\S)~~", "<del>$1</del>");

            text = Regex.Replace(text, "\u0000(\\d+)\u0000", m => codes[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]);
            return text;
        }

        public static string MarkdownToText(string md)
        {
            var sb = new StringBuilder();
            bool inCode = false;
            foreach (var raw in md.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw;
                if (line.Trim().StartsWith("```") || line.Trim().StartsWith("~~~")) { inCode = !inCode; continue; }
                if (!inCode)
                {
                    line = Regex.Replace(line, @"^\s{0,3}#{1,6}\s+", "");
                    line = Regex.Replace(line, @"^\s*>\s?", "");
                    line = Regex.Replace(line, @"^\s*[-*+]\s+\[( |x|X)\]\s+", "• ");
                    line = Regex.Replace(line, @"^\s*[-*+]\s+", "• ");
                    line = Regex.Replace(line, @"!\[([^\]]*)\]\([^)]*\)", "$1");
                    line = Regex.Replace(line, @"\[([^\]]+)\]\(([^)]*)\)", "$1 ($2)");
                    line = Regex.Replace(line, @"(\*\*|__|~~)(.+?)\1", "$2");
                    line = Regex.Replace(line, @"(?<![\w*])\*(.+?)\*(?![\w*])", "$1");
                    line = Regex.Replace(line, @"`([^`]+)`", "$1");
                    if (Regex.IsMatch(line.Trim(), @"^\|?\s*:?-{3,}")) continue;
                    if (line.TrimStart().StartsWith("|")) line = string.Join("\t", SplitRow(line));
                }
                sb.Append(line).Append("\r\n");
            }
            return sb.ToString();
        }
    }
}
