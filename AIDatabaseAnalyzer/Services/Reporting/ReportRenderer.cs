using System.Globalization;
using System.Net;
using System.Text;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Markdown;

namespace AIDatabaseAnalyzer.Services.Reporting;

/// <summary>Turns an <see cref="AnalysisReport"/> into Markdown or a self-contained, themed HTML page.</summary>
public static class ReportRenderer
{
    public static string ToMarkdown(AnalysisReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {report.Title}");
        sb.AppendLine();
        sb.AppendLine($"- **Database:** {report.DatabaseName} ({report.EngineName})");
        sb.AppendLine($"- **Generated:** {report.GeneratedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}");
        sb.AppendLine($"- **Model:** {report.Provider} · {report.Model}");
        sb.AppendLine($"- **Tables analysed:** {report.TableCount}");
        if (report.HealthScore > 0) sb.AppendLine($"- **Schema health score:** {report.HealthScore}/100 (grade {report.HealthGrade})");
        sb.AppendLine();

        if (report.Findings.Count > 0)
        {
            sb.AppendLine("## Automated findings");
            sb.AppendLine();
            foreach (var f in report.Findings)
            {
                sb.AppendLine($"### [{f.Severity}] {f.Title}");
                sb.AppendLine();
                sb.AppendLine(f.Description);
                if (f.Objects.Count > 0)
                {
                    sb.AppendLine();
                    foreach (var o in f.Objects.Take(15)) sb.AppendLine($"- `{o}`");
                }
                sb.AppendLine();
                sb.AppendLine($"**Recommendation:** {f.Recommendation}");
                sb.AppendLine();
            }
        }

        for (int i = 0; i < report.Sections.Count; i++)
        {
            var s = report.Sections[i];
            sb.AppendLine($"## {s.Title}");
            sb.AppendLine();
            sb.AppendLine(s.Content.Trim());
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <param name="theme">"auto", "light" or "dark".</param>
    public static string ToHtml(AnalysisReport report, string theme = "auto")
    {
        var body = new StringBuilder();
        var toc = new StringBuilder();
        int n = 0;

        if (report.Findings.Count > 0)
        {
            string id = "findings";
            toc.Append($"<a href=\"#{id}\">Automated findings</a>");
            body.Append($"<section class=\"card\" id=\"{id}\"><h2>Automated findings</h2>");
            body.Append("<p class=\"muted\">Rule based checks that ran instantly on the extracted schema.</p>");
            foreach (var f in report.Findings)
            {
                string sev = f.Severity.ToLowerInvariant();
                body.Append($"<article class=\"finding sev-{WebUtility.HtmlEncode(sev)}\">");
                body.Append($"<div class=\"finding-head\"><span class=\"badge sev-{WebUtility.HtmlEncode(sev)}\">{WebUtility.HtmlEncode(f.Severity)}</span>");
                body.Append($"<h3>{WebUtility.HtmlEncode(f.Title)}</h3></div>");
                body.Append($"<p>{WebUtility.HtmlEncode(f.Description)}</p>");
                if (f.Objects.Count > 0)
                {
                    body.Append("<ul class=\"objects\">");
                    foreach (var o in f.Objects.Take(12)) body.Append($"<li><code>{WebUtility.HtmlEncode(o)}</code></li>");
                    body.Append("</ul>");
                }
                body.Append($"<p class=\"reco\"><strong>Recommendation:</strong> {WebUtility.HtmlEncode(f.Recommendation)}</p></article>");
            }
            body.Append("</section>");
        }

        foreach (var section in report.Sections)
        {
            n++;
            string id = $"s{n}";
            toc.Append($"<a href=\"#{id}\">{WebUtility.HtmlEncode(section.Title)}</a>");
            body.Append($"<section class=\"card{(section.IsError ? " error" : "")}\" id=\"{id}\">");
            body.Append($"<h2>{WebUtility.HtmlEncode(section.Title)}</h2>");
            if (!string.IsNullOrEmpty(section.Category)) body.Append($"<span class=\"tag\">{WebUtility.HtmlEncode(section.Category)}</span>");
            body.Append("<div class=\"md\">").Append(MarkdownHtmlRenderer.Render(section.Content)).Append("</div></section>");
        }

        string scoreColor = report.HealthScore >= 80 ? "var(--good)" : report.HealthScore >= 55 ? "var(--warn)" : "var(--bad)";
        string ring = report.HealthScore > 0
            ? $"<div class=\"ring\" style=\"--p:{report.HealthScore};--c:{scoreColor}\"><span>{report.HealthScore}</span><small>Grade {WebUtility.HtmlEncode(report.HealthGrade)}</small></div>"
            : "";

        string chips = string.Join("", new[]
        {
            report.EngineName,
            $"{report.TableCount} tables",
            $"{report.Provider} · {report.Model}",
            report.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        }.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => $"<span class=\"chip\">{WebUtility.HtmlEncode(c)}</span>"));

        return Template
            .Replace("{{THEME}}", theme is "light" or "dark" ? theme : "auto")
            .Replace("{{TITLE}}", WebUtility.HtmlEncode(report.Title))
            .Replace("{{VARS}}", HtmlTheme.Variables)
            .Replace("{{AURORA}}", HtmlTheme.AuroraBackground)
            .Replace("{{FONT}}", HtmlTheme.FontStack)
            .Replace("{{MONO}}", HtmlTheme.MonoStack)
            .Replace("{{DB}}", WebUtility.HtmlEncode(report.DatabaseName))
            .Replace("{{CHIPS}}", chips)
            .Replace("{{RING}}", ring)
            .Replace("{{TOC}}", toc.ToString())
            .Replace("{{BODY}}", body.ToString());
    }

    private const string Template = """
        <!doctype html>
        <html lang="en" data-theme="{{THEME}}">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{TITLE}}</title>
        <style>
        {{VARS}}
        *{box-sizing:border-box}
        html{scroll-behavior:smooth}
        body{margin:0;min-height:100vh;color:var(--text);font:15px/1.6 {{FONT}};-webkit-font-smoothing:antialiased;{{AURORA}}}
        .page{max-width:1180px;margin:0 auto;padding:28px 22px 64px}
        .card{background:var(--glass);border:1px solid var(--stroke);border-radius:24px;box-shadow:var(--shadow);
              -webkit-backdrop-filter:blur(28px) saturate(1.6);backdrop-filter:blur(28px) saturate(1.6);padding:26px 30px;margin-bottom:18px}
        .hero{display:flex;align-items:center;justify-content:space-between;gap:24px;padding:32px 36px}
        .eyebrow{margin:0 0 6px;font-size:12px;font-weight:700;letter-spacing:.14em;text-transform:uppercase;color:var(--accent)}
        h1{margin:0 0 14px;font-size:34px;line-height:1.15;letter-spacing:-.02em}
        h2{margin:0 0 10px;font-size:22px;letter-spacing:-.01em}
        h3{margin:18px 0 6px;font-size:17px} h4{margin:14px 0 4px;font-size:15px}
        .chips{display:flex;flex-wrap:wrap;gap:8px}
        .chip{padding:5px 12px;border-radius:999px;background:var(--glass-strong);border:1px solid var(--stroke);font-size:12.5px;color:var(--text-2);font-weight:600}
        .ring{--p:0;--c:var(--good);flex:none;width:118px;height:118px;border-radius:50%;display:grid;place-content:center;text-align:center;position:relative;
              background:conic-gradient(var(--c) calc(var(--p)*1%), var(--line) 0)}
        .ring::before{content:"";position:absolute;inset:10px;border-radius:50%;background:var(--surface)}
        .ring span,.ring small{position:relative}
        .ring span{font-size:34px;font-weight:800;letter-spacing:-.03em;line-height:1}
        .ring small{font-size:11px;color:var(--text-3);font-weight:700;margin-top:3px}
        .layout{display:grid;grid-template-columns:260px minmax(0,1fr);gap:18px;align-items:start}
        nav.toc{position:sticky;top:18px;padding:18px 14px;max-height:calc(100vh - 36px);overflow:auto}
        nav.toc a{display:block;padding:7px 12px;border-radius:12px;color:var(--text-2);text-decoration:none;font-size:13px;font-weight:600}
        nav.toc a:hover{background:var(--accent-soft);color:var(--accent)}
        .muted{color:var(--text-3);margin-top:0}
        .tag{display:inline-block;margin:0 0 8px;padding:2px 10px;border-radius:999px;background:var(--accent-soft);color:var(--accent);font-size:11.5px;font-weight:700}
        .md p,.md li{color:var(--text)} .md ul,.md ol{padding-left:22px}
        .md code,.objects code{font:12.5px {{MONO}};background:var(--code-bg);padding:2px 6px;border-radius:7px}
        .md pre{background:var(--code-bg);border:1px solid var(--line);padding:14px 16px;border-radius:14px;overflow:auto}
        .md pre code{background:none;padding:0}
        .md blockquote{margin:12px 0;padding:4px 16px;border-left:3px solid var(--accent);color:var(--text-2)}
        .table-wrap{overflow:auto;margin:12px 0;border:1px solid var(--line);border-radius:14px}
        table{border-collapse:collapse;width:100%;font-size:13.5px}
        th,td{padding:9px 13px;text-align:left;vertical-align:top;border-bottom:1px solid var(--line)}
        th{background:var(--code-bg);font-weight:700;white-space:nowrap} tr:last-child td{border-bottom:none}
        hr{border:none;border-top:1px solid var(--line);margin:18px 0}
        a{color:var(--accent)}
        section.error{border-color:var(--bad)}
        .finding{border-top:1px solid var(--line);padding:14px 0 4px}
        .finding-head{display:flex;gap:10px;align-items:center} .finding-head h3{margin:0}
        .badge{padding:3px 10px;border-radius:999px;font-size:11px;font-weight:800;letter-spacing:.04em;text-transform:uppercase;color:#fff;background:var(--text-3)}
        .badge.sev-critical{background:#B42318}.badge.sev-high{background:#E5483E}.badge.sev-medium{background:#D98A00}
        .badge.sev-low{background:#2E7CF6}.badge.sev-info{background:#7A7A85}
        .objects{margin:8px 0;padding-left:20px} .reco{color:var(--text-2)}
        footer{text-align:center;color:var(--text-3);font-size:12px;margin-top:26px}
        @media (max-width:860px){.layout{grid-template-columns:1fr}nav.toc{position:static;max-height:none}.hero{flex-direction:column-reverse;align-items:flex-start}}
        @media print{body{background:#fff;color:#000}.card{box-shadow:none;background:#fff;border-color:#ddd;-webkit-backdrop-filter:none;backdrop-filter:none}nav.toc{display:none}.layout{display:block}}
        </style>
        </head>
        <body>
        <div class="page">
          <header class="card hero">
            <div>
              <p class="eyebrow">AI Database Analyzer</p>
              <h1>{{DB}} — Analysis Report</h1>
              <div class="chips">{{CHIPS}}</div>
            </div>
            {{RING}}
          </header>
          <div class="layout">
            <nav class="toc card">{{TOC}}</nav>
            <main>{{BODY}}</main>
          </div>
          <footer>Generated by AI Database Analyzer</footer>
        </div>
        </body>
        </html>
        """;
}
