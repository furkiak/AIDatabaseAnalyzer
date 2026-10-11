using System.Net;
using System.Text;

namespace AIDatabaseAnalyzer.Services.Markdown;

/// <summary>Renders the Markdown model as safe (always escaped) HTML.</summary>
public static class MarkdownHtmlRenderer
{
    /// <param name="headingOffset">Added to heading levels so embedded answers never outrank the page headings.</param>
    /// <param name="codeLink">Optional: returns a link target for inline code (for example a table name that opens the diagram).</param>
    public static string Render(string? markdown, int headingOffset = 2, Func<string, string?>? codeLink = null) =>
        Render(MarkdownParser.Parse(markdown), headingOffset, codeLink);

    public static string Render(IReadOnlyList<MdBlock> blocks, int headingOffset = 2, Func<string, string?>? codeLink = null)
    {
        var sb = new StringBuilder();
        foreach (var block in blocks)
        {
            switch (block)
            {
                case MdHeading h:
                    int level = Math.Clamp(h.Level + headingOffset, 1, 6);
                    sb.Append($"<h{level}>").Append(Inlines(h.Inlines, codeLink)).Append($"</h{level}>\n");
                    break;
                case MdParagraph p:
                    sb.Append("<p>").Append(Inlines(p.Inlines, codeLink)).Append("</p>\n");
                    break;
                case MdList l:
                    int i = 0;
                    while (i < l.Items.Count) RenderList(l.Items, ref i, l.Items[i].Level, sb, codeLink);
                    sb.Append('\n');
                    break;
                case MdCode c:
                    sb.Append("<pre><code>").Append(WebUtility.HtmlEncode(c.Text)).Append("</code></pre>\n");
                    break;
                case MdQuote q:
                    sb.Append("<blockquote>").Append(Inlines(q.Inlines, codeLink)).Append("</blockquote>\n");
                    break;
                case MdRule:
                    sb.Append("<hr>\n");
                    break;
                case MdTable t:
                    RenderTable(t, sb, codeLink);
                    break;
            }
        }
        return sb.ToString();
    }

    public static string Inlines(IEnumerable<MdInline> inlines, Func<string, string?>? codeLink = null)
    {
        var sb = new StringBuilder();
        foreach (var inline in inlines)
        {
            string text = WebUtility.HtmlEncode(inline.Text).Replace("\n", "<br>");
            if (inline.Code)
            {
                text = $"<code>{text}</code>";
                if (inline.Href is null && codeLink?.Invoke(inline.Text) is { } target)
                    text = $"<a class=\"obj\" href=\"{WebUtility.HtmlEncode(target)}\">{text}</a>";
            }
            if (inline.Bold) text = $"<strong>{text}</strong>";
            if (inline.Italic) text = $"<em>{text}</em>";
            if (inline.Href is { } href && IsSafeLink(href))
                text = $"<a href=\"{WebUtility.HtmlEncode(href)}\" target=\"_blank\" rel=\"noopener noreferrer\">{text}</a>";
            sb.Append(text);
        }
        return sb.ToString();
    }

    private static bool IsSafeLink(string href) =>
        href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    private static void RenderList(IReadOnlyList<MdListItem> items, ref int i, int level, StringBuilder sb, Func<string, string?>? codeLink)
    {
        bool ordered = items[i].Ordered;
        sb.Append(ordered ? "<ol>" : "<ul>");
        while (i < items.Count && items[i].Level == level)
        {
            sb.Append("<li>").Append(Inlines(items[i].Inlines, codeLink));
            i++;
            while (i < items.Count && items[i].Level > level)
                RenderList(items, ref i, items[i].Level, sb, codeLink);
            sb.Append("</li>");
        }
        sb.Append(ordered ? "</ol>" : "</ul>");
    }

    private static void RenderTable(MdTable table, StringBuilder sb, Func<string, string?>? codeLink)
    {
        sb.Append("<div class=\"table-wrap\"><table><thead><tr>");
        foreach (var cell in table.Header) sb.Append("<th>").Append(Inlines(cell, codeLink)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var row in table.Rows)
        {
            sb.Append("<tr>");
            foreach (var cell in row) sb.Append("<td>").Append(Inlines(cell, codeLink)).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table></div>\n");
    }
}
