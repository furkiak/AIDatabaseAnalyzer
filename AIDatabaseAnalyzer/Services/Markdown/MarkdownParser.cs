using System.Text;
using System.Text.RegularExpressions;

namespace AIDatabaseAnalyzer.Services.Markdown;

/// <summary>
/// A small, forgiving Markdown parser for the subset AI models actually produce: headings, paragraphs,
/// (nested) lists, fenced code, tables, block quotes, rules and basic inline formatting.
/// </summary>
public static partial class MarkdownParser
{
    public static IReadOnlyList<MdBlock> Parse(string? markdown)
    {
        var blocks = new List<MdBlock>();
        if (string.IsNullOrWhiteSpace(markdown)) return blocks;

        string[] lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();
        var listItems = new List<MdListItem>();
        var listTexts = new List<StringBuilder>();
        var quote = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new MdParagraph(ParseInlines(string.Join(" ", paragraph.Select(p => p.Trim())))));
            paragraph.Clear();
        }

        void FlushList()
        {
            if (listItems.Count == 0) return;
            var items = new List<MdListItem>();
            for (int i = 0; i < listItems.Count; i++)
                items.Add(listItems[i] with { Inlines = ParseInlines(listTexts[i].ToString().Trim()) });
            blocks.Add(new MdList(items));
            listItems.Clear();
            listTexts.Clear();
        }

        void FlushQuote()
        {
            if (quote.Count == 0) return;
            blocks.Add(new MdQuote(ParseInlines(string.Join(" ", quote.Select(q => q.Trim())))));
            quote.Clear();
        }

        void FlushAll()
        {
            FlushParagraph();
            FlushList();
            FlushQuote();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();

            // fenced code block
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                FlushAll();
                string fence = trimmed[..3];
                string? language = trimmed.Length > 3 ? trimmed[3..].Trim() : null;
                var code = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].Trim().StartsWith(fence, StringComparison.Ordinal))
                {
                    code.Add(lines[i]);
                    i++;
                }
                blocks.Add(new MdCode(string.IsNullOrEmpty(language) ? null : language, string.Join("\n", code)));
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushAll();
                continue;
            }

            // heading
            var heading = HeadingRegex().Match(trimmed);
            if (heading.Success)
            {
                FlushAll();
                blocks.Add(new MdHeading(heading.Groups[1].Length, ParseInlines(heading.Groups[2].Value.Trim())));
                continue;
            }

            // horizontal rule
            if (RuleRegex().IsMatch(trimmed))
            {
                FlushAll();
                blocks.Add(new MdRule());
                continue;
            }

            // table (header line followed by a separator line)
            if (trimmed.Contains('|') && i + 1 < lines.Length && TableSeparatorRegex().IsMatch(lines[i + 1].Trim()))
            {
                FlushAll();
                var header = SplitRow(trimmed).Select(c => ParseInlines(c)).ToList();
                i += 2;
                var rows = new List<IReadOnlyList<IReadOnlyList<MdInline>>>();
                while (i < lines.Length && lines[i].Trim().Length > 0 && lines[i].Contains('|'))
                {
                    var cells = SplitRow(lines[i].Trim()).Select(c => ParseInlines(c)).ToList();
                    while (cells.Count < header.Count) cells.Add(Array.Empty<MdInline>());
                    rows.Add(cells);
                    i++;
                }
                i--;
                blocks.Add(new MdTable(header, rows));
                continue;
            }

            // block quote
            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                FlushList();
                quote.Add(trimmed.TrimStart('>').Trim());
                continue;
            }

            // list item
            var item = ListRegex().Match(line.Replace("\t", "    "));
            if (item.Success)
            {
                FlushParagraph();
                FlushQuote();
                int indent = item.Groups[1].Length;
                string marker = item.Groups[2].Value;
                bool ordered = char.IsDigit(marker[0]);
                int number = ordered && int.TryParse(marker.TrimEnd('.', ')'), out int n) ? n : 0;
                listItems.Add(new MdListItem(Math.Min(indent / 2, 4), ordered, number, Array.Empty<MdInline>()));
                listTexts.Add(new StringBuilder(item.Groups[3].Value));
                continue;
            }

            // continuation of the previous list item
            if (listItems.Count > 0 && char.IsWhiteSpace(line[0]))
            {
                listTexts[^1].Append(' ').Append(trimmed);
                continue;
            }

            FlushList();
            FlushQuote();
            paragraph.Add(line);
        }

        FlushAll();
        return blocks;
    }

    // ------------------------------------------------------------------ inline formatting

    public static IReadOnlyList<MdInline> ParseInlines(string text) => ParseInlines(text, false, false);

    private static List<MdInline> ParseInlines(string s, bool bold, bool italic)
    {
        var result = new List<MdInline>();
        var sb = new StringBuilder();

        void Flush()
        {
            if (sb.Length == 0) return;
            result.Add(new MdInline(sb.ToString(), bold, italic));
            sb.Clear();
        }

        s = BreakRegex().Replace(s, "\n");

        for (int i = 0; i < s.Length;)
        {
            char c = s[i];

            if (c == '\\' && i + 1 < s.Length && "\\`*_{}[]()#+-.!|>~".IndexOf(s[i + 1]) >= 0)
            {
                sb.Append(s[i + 1]);
                i += 2;
                continue;
            }

            if (c == '`')
            {
                int end = s.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    result.Add(new MdInline(s[(i + 1)..end], bold, italic, Code: true));
                    i = end + 1;
                    continue;
                }
            }

            if (c is '*' or '_')
            {
                bool doubled = i + 1 < s.Length && s[i + 1] == c;
                bool openAllowed = c == '*' || i == 0 || !char.IsLetterOrDigit(s[i - 1]);

                if (openAllowed && doubled)
                {
                    int end = FindClosing(s, i + 2, new string(c, 2));
                    if (end > i + 2)
                    {
                        Flush();
                        result.AddRange(ParseInlines(s[(i + 2)..end], true, italic));
                        i = end + 2;
                        continue;
                    }
                }
                else if (openAllowed && i + 1 < s.Length && !char.IsWhiteSpace(s[i + 1]) && !doubled)
                {
                    int end = FindClosing(s, i + 1, c.ToString());
                    if (end > i + 1)
                    {
                        Flush();
                        result.AddRange(ParseInlines(s[(i + 1)..end], bold, true));
                        i = end + 1;
                        continue;
                    }
                }
            }

            if (c == '[')
            {
                int close = s.IndexOf(']', i + 1);
                if (close > i && close + 1 < s.Length && s[close + 1] == '(')
                {
                    int paren = s.IndexOf(')', close + 2);
                    if (paren > close)
                    {
                        Flush();
                        string label = s[(i + 1)..close];
                        string href = s[(close + 2)..paren].Trim();
                        result.Add(new MdInline(label, bold, italic, Href: href));
                        i = paren + 1;
                        continue;
                    }
                }
            }

            sb.Append(c);
            i++;
        }

        Flush();
        return result;
    }

    private static int FindClosing(string s, int from, string marker)
    {
        int pos = from;
        while (pos < s.Length)
        {
            int idx = s.IndexOf(marker, pos, StringComparison.Ordinal);
            if (idx < 0) return -1;
            bool afterWord = idx + marker.Length >= s.Length || !char.IsLetterOrDigit(s[idx + marker.Length]) || marker[0] == '*';
            bool notSpaceBefore = idx > from && !char.IsWhiteSpace(s[idx - 1]);
            if (afterWord && notSpaceBefore)
            {
                // for single markers skip doubled ones ("**") that belong to a bold run
                if (marker.Length == 1 && idx + 1 < s.Length && s[idx + 1] == marker[0])
                {
                    pos = idx + 2;
                    continue;
                }
                return idx;
            }
            pos = idx + marker.Length;
        }
        return -1;
    }

    private static List<string> SplitRow(string row)
    {
        string t = row.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|') && !t.EndsWith("\\|", StringComparison.Ordinal)) t = t[..^1];
        return t.Split('|').Select(c => c.Replace("\\|", "|").Trim()).ToList();
    }

    /// <summary>Flattens inlines to plain text (used for table of contents, titles...).</summary>
    public static string ToPlainText(IEnumerable<MdInline> inlines) => string.Concat(inlines.Select(i => i.Text));

    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^([-*_])(\s*\1){2,}$")]
    private static partial Regex RuleRegex();

    [GeneratedRegex(@"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?$")]
    private static partial Regex TableSeparatorRegex();

    [GeneratedRegex(@"^(\s*)([-*+]|\d{1,3}[.)])\s+(.*)$")]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();
}
