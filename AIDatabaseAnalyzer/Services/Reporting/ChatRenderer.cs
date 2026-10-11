using System.Net;
using System.Text;
using AIDatabaseAnalyzer.Services.Llm;
using AIDatabaseAnalyzer.Services.Markdown;

namespace AIDatabaseAnalyzer.Services.Reporting;

/// <summary>One entry of the schema chat as shown to the user.</summary>
public sealed record ChatEntry(LlmRole Role, string Text, bool IsError = false, string? Meta = null);

/// <summary>Renders the schema chat as a page: user bubbles, Markdown answers with linked table names and copyable SQL.</summary>
public static class ChatRenderer
{
    public static string ToHtml(IReadOnlyList<ChatEntry> entries, string theme, Func<string, string?>? objectLink, bool thinking, string? thinkingText = null)
    {
        var body = new StringBuilder();
        foreach (var entry in entries)
        {
            if (entry.Role == LlmRole.User)
            {
                body.Append("<div class=\"msg user\"><div class=\"bubble\">")
                    .Append(WebUtility.HtmlEncode(entry.Text).Replace("\n", "<br>"))
                    .Append("</div></div>");
            }
            else
            {
                body.Append($"<div class=\"msg ai{(entry.IsError ? " error" : "")}\"><div class=\"bubble md\">")
                    .Append(entry.IsError ? "<p>" + WebUtility.HtmlEncode(entry.Text) + "</p>" : MarkdownHtmlRenderer.Render(entry.Text, 1, objectLink))
                    .Append("</div>");
                if (!string.IsNullOrEmpty(entry.Meta)) body.Append("<div class=\"meta\">").Append(WebUtility.HtmlEncode(entry.Meta)).Append("</div>");
                body.Append("</div>");
            }
        }

        if (thinking)
            body.Append("<div class=\"msg ai\"><div class=\"bubble typing\"><i></i><i></i><i></i><span>")
                .Append(WebUtility.HtmlEncode(thinkingText ?? "Thinking…"))
                .Append("</span></div></div>");

        return Template
            .Replace("{{THEME}}", theme is "light" or "dark" ? theme : "auto")
            .Replace("{{VARS}}", HtmlTheme.Variables)
            .Replace("{{FONT}}", HtmlTheme.FontStack)
            .Replace("{{MONO}}", HtmlTheme.MonoStack)
            .Replace("{{BODY}}", body.ToString());
    }

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
        body{font:14px/1.6 {{FONT}};color:var(--text);padding:18px 18px 8px;-webkit-font-smoothing:antialiased}
        .msg{display:flex;margin:0 0 14px}
        .msg.user{justify-content:flex-end}
        .bubble{max-width:86%;padding:11px 15px;border-radius:18px;border:1px solid var(--stroke);background:var(--glass-strong)}
        .user .bubble{background:linear-gradient(135deg,#5E5CE6,#0A84FF);color:#fff;border-color:transparent;border-bottom-right-radius:6px;white-space:normal}
        .ai .bubble{border-bottom-left-radius:6px;box-shadow:var(--shadow)}
        .ai{flex-direction:column;align-items:flex-start}
        .ai.error .bubble{border-color:var(--bad);color:var(--bad)}
        .meta{font-size:11px;color:var(--text-3);margin:4px 8px 0}
        .md p{margin:0 0 8px} .md p:last-child{margin-bottom:0}
        .md h2,.md h3,.md h4{margin:12px 0 6px;font-size:15px}
        .md ul,.md ol{padding-left:20px;margin:6px 0}
        .md code{font:12.5px {{MONO}};background:var(--code-bg);padding:2px 6px;border-radius:7px}
        .md pre{position:relative;background:var(--code-bg);border:1px solid var(--line);padding:12px 14px;border-radius:12px;overflow:auto;margin:8px 0}
        .md pre code{background:none;padding:0}
        .table-wrap{overflow:auto;margin:8px 0;border:1px solid var(--line);border-radius:12px}
        table{border-collapse:collapse;width:100%;font-size:13px}
        th,td{padding:7px 11px;text-align:left;vertical-align:top;border-bottom:1px solid var(--line)}
        th{background:var(--code-bg);font-weight:700}
        a{color:var(--accent)} a.obj{text-decoration:none} a.obj code{color:var(--accent)} a.obj:hover code{background:var(--accent-soft)}
        .copy{position:absolute;top:7px;right:7px;border:1px solid var(--line);background:var(--glass-strong);color:var(--text-2);border-radius:9px;padding:2px 9px;font:600 11px {{FONT}};cursor:pointer}
        .copy:hover{color:var(--accent)}
        .typing{display:flex;align-items:center;gap:5px;color:var(--text-3);font-size:13px}
        .typing i{width:7px;height:7px;border-radius:50%;background:var(--accent);opacity:.35;animation:b 1.2s infinite}
        .typing i:nth-child(2){animation-delay:.2s} .typing i:nth-child(3){animation-delay:.4s}
        .typing span{margin-left:6px}
        @keyframes b{0%,80%,100%{opacity:.25;transform:translateY(0)}40%{opacity:1;transform:translateY(-3px)}}
        </style>
        </head>
        <body>
        {{BODY}}
        <script>
        (function(){
          document.querySelectorAll('.md pre').forEach(function(pre){
            var b=document.createElement('button');b.className='copy';b.textContent='Copy';
            b.onclick=function(){var t=pre.querySelector('code'),a=document.createElement('a');
              a.href='aida://copy/'+encodeURIComponent(t?t.innerText:pre.innerText);document.body.appendChild(a);a.click();a.remove();b.textContent='Copied';};
            pre.appendChild(b);
          });
          window.scrollTo(0, document.body.scrollHeight);
        })();
        </script>
        </body>
        </html>
        """;
}
