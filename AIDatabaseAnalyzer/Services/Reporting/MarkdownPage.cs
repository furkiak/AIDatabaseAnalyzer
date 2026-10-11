using AIDatabaseAnalyzer.Services.Markdown;

namespace AIDatabaseAnalyzer.Services.Reporting;

/// <summary>A plain themed page for Markdown content shown inside the app (schema drift, notes).</summary>
public static class MarkdownPage
{
    public static string ToHtml(string markdown, string theme, Func<string, string?>? objectLink = null) =>
        Template
            .Replace("{{THEME}}", theme is "light" or "dark" ? theme : "auto")
            .Replace("{{VARS}}", HtmlTheme.Variables)
            .Replace("{{FONT}}", HtmlTheme.FontStack)
            .Replace("{{MONO}}", HtmlTheme.MonoStack)
            .Replace("{{BODY}}", MarkdownHtmlRenderer.Render(markdown, 0, objectLink));

    private const string Template = """
        <!doctype html>
        <html lang="en" data-theme="{{THEME}}">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <style>
        {{VARS}}
        *{box-sizing:border-box}
        html,body{margin:0;background:var(--surface);min-height:100%}
        body{font:14px/1.6 {{FONT}};color:var(--text);padding:20px 24px;-webkit-font-smoothing:antialiased}
        h2{font-size:16px;margin:22px 0 8px;letter-spacing:-.01em} h3{font-size:14px;margin:14px 0 6px}
        p{margin:0 0 10px} ul{padding-left:20px;margin:6px 0 10px} li{margin:3px 0}
        code{font:12.5px {{MONO}};background:var(--code-bg);padding:2px 6px;border-radius:7px}
        a{color:var(--accent)} a.obj{text-decoration:none} a.obj code{color:var(--accent)} a.obj:hover code{background:var(--accent-soft)}
        .table-wrap{overflow:auto;margin:8px 0;border:1px solid var(--line);border-radius:12px}
        table{border-collapse:collapse;width:100%;font-size:13px}
        th,td{padding:7px 11px;text-align:left;border-bottom:1px solid var(--line)} th{background:var(--code-bg)}
        em{color:var(--text-3)}
        </style>
        </head>
        <body>{{BODY}}</body>
        </html>
        """;
}
