using System.Text;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Markdown;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Colors = QuestPDF.Helpers.Colors;
using FontManager = QuestPDF.Drawing.FontManager;

namespace AIDatabaseAnalyzer.Services.Reporting;

public interface IPdfReportService
{
    /// <summary>False where the PDF engine cannot run; callers offer HTML export instead.</summary>
    bool IsSupported { get; }

    /// <summary>Renders the report into a PDF file on a background thread.</summary>
    Task GenerateAsync(AnalysisReport report, string outputPath, CancellationToken ct = default);
}

/// <summary>
/// Creates the PDF version of a report. Markdown produced by the AI is rendered properly (headings, lists, tables, code).
/// Text uses the Lato font bundled with QuestPDF; code uses DejaVu Sans Mono, supplied by the host application.
/// </summary>
public sealed class PdfReportService : IPdfReportService
{
    public const string MonoFamily = "DejaVu Sans Mono";

    private const string Accent = "#0A6CFF";
    private const string Ink = "#1D1D1F";
    private const string Muted = "#6B6B76";
    private const string Soft = "#F2F4F8";

    private static readonly object SetupLock = new();
    private static bool _configured;
    private static bool _fontsRegistered;
    private readonly Func<Stream?>? _monoFontProvider;

    /// <param name="monoFontProvider">Opens the TTF of <see cref="MonoFamily"/>; when absent, code is set in Lato.</param>
    public PdfReportService(Func<Stream?>? monoFontProvider = null) => _monoFontProvider = monoFontProvider;

    /// <summary>
    /// QuestPDF's native engine ships for Windows, Linux and macOS but refuses to start under Mac Catalyst.
    /// Its settings are therefore only touched when a PDF is made, never while the app starts.
    /// </summary>
    public bool IsSupported => !OperatingSystem.IsMacCatalyst();

    private void EnsureReady()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("PDF export is not available on macOS yet. Export the report as HTML and print it to PDF.");

        lock (SetupLock)
        {
            if (!_configured)
            {
                QuestPDF.Settings.License = LicenseType.Community;
                // AI answers may contain symbols the font lacks, or a font may be missing on a machine:
                // never abort the whole report because of that.
                QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
                QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
                _configured = true;
            }

            if (_fontsRegistered || _monoFontProvider is null) return;
            try
            {
                using var stream = _monoFontProvider();
                if (stream is not null) FontManager.RegisterFontFromStream(stream);
            }
            catch
            {
                // Code blocks silently fall back to the default font.
            }
            _fontsRegistered = true;
        }
    }

    public Task GenerateAsync(AnalysisReport report, string outputPath, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            EnsureReady();
            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            Build(report).GeneratePdf(outputPath);
        }, ct);

    /// <summary>Renders to memory (used by tests and "export without saving").</summary>
    public byte[] ToBytes(AnalysisReport report)
    {
        EnsureReady();
        return Build(report).GeneratePdf();
    }

    private static Document Build(AnalysisReport report)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(2, Unit.Centimetre);
                page.MarginVertical(1.8f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10.5f).FontColor(Ink).LineHeight(1.35f));

                page.Header().PaddingBottom(8).Row(row =>
                {
                    row.RelativeItem().Text(t => t.Span("AI DATABASE ANALYZER").FontSize(8).Bold().FontColor(Accent).LetterSpacing(0.12f));
                    row.AutoItem().Text(report.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd")).FontSize(8).FontColor(Muted);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    ComposeHero(col, report);

                    if (report.Findings.Count > 0)
                    {
                        col.Item().PaddingTop(10).Text("Automated findings").FontSize(17).Bold();
                        col.Item().Text("Rule based checks that ran instantly on the extracted schema.").FontSize(9).FontColor(Muted);
                        foreach (var f in report.Findings) ComposeFinding(col, f);
                    }

                    foreach (var section in report.Sections)
                    {
                        col.Item().PaddingTop(14).Column(head =>
                        {
                            head.Item().Text(PdfText.Clean(section.Title)).FontSize(17).Bold().FontColor(section.IsError ? "#B42318" : Ink);
                            head.Item().PaddingTop(3).LineHorizontal(1.2f).LineColor(Accent);
                        });

                        ComposeMarkdown(col, MarkdownParser.Parse(PdfText.Clean(section.Content)));
                    }
                });

                page.Footer().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text($"{PdfText.Clean(report.DatabaseName)} · {report.EngineName}").FontSize(8).FontColor(Muted);
                    row.AutoItem().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
                        t.Span("Page ");
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            });
        });
    }

    // ------------------------------------------------------------------ hero & findings

    private static void ComposeHero(ColumnDescriptor col, AnalysisReport report)
    {
        col.Item().Background(Soft).CornerRadius(14).Padding(20).Row(row =>
        {
            row.RelativeItem().Column(c =>
            {
                c.Spacing(5);
                c.Item().Text(PdfText.Clean(report.DatabaseName)).FontSize(26).Bold();
                c.Item().Text("Database analysis report").FontSize(12).FontColor(Muted);
                c.Item().PaddingTop(6).Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(9.5f).FontColor(Muted));
                    t.Span($"{report.EngineName}   ·   {report.TableCount} tables   ·   {PdfText.Clean(report.Provider)} {PdfText.Clean(report.Model)}");
                });
            });

            if (report.HealthScore > 0)
            {
                string color = report.HealthScore >= 80 ? "#1F9D55" : report.HealthScore >= 55 ? "#C77700" : "#D92D20";
                row.ConstantItem(92).AlignRight().Column(c =>
                {
                    c.Item().AlignCenter().Text(report.HealthScore.ToString()).FontSize(36).Bold().FontColor(color);
                    c.Item().AlignCenter().Text($"Health · {report.HealthGrade}").FontSize(8.5f).FontColor(Muted);
                });
            }
        });
    }

    private static void ComposeFinding(ColumnDescriptor col, ReportFinding finding)
    {
        string color = finding.Severity switch
        {
            "Critical" => "#B42318",
            "High" => "#E5483E",
            "Medium" => "#D98A00",
            "Low" => "#2E7CF6",
            _ => "#7A7A85"
        };

        col.Item().ShowEntire().PaddingTop(4).Column(c =>
        {
            c.Spacing(3);
            c.Item().Row(r =>
            {
                r.AutoItem().Background(color).CornerRadius(6).PaddingHorizontal(6).PaddingVertical(1.5f)
                    .Text(finding.Severity.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Colors.White);
                r.RelativeItem().PaddingLeft(8).Text(PdfText.Clean(finding.Title)).FontSize(11.5f).Bold();
            });
            c.Item().Text(PdfText.Clean(finding.Description)).FontSize(9.5f).FontColor(Muted);
            if (finding.Objects.Count > 0)
                c.Item().PaddingLeft(10).Text(PdfText.Clean(string.Join("  •  ", finding.Objects.Take(8)))).FontSize(8.5f).FontFamily(MonoFamily);
            c.Item().Text(t =>
            {
                t.Span("Recommendation: ").Bold().FontSize(9.5f);
                t.Span(PdfText.Clean(finding.Recommendation)).FontSize(9.5f);
            });
        });
    }

    // ------------------------------------------------------------------ markdown

    private static void ComposeMarkdown(ColumnDescriptor col, IReadOnlyList<MdBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case MdHeading h:
                    float size = h.Level switch { 1 => 15, 2 => 13.5f, 3 => 12, _ => 11 };
                    col.Item().PaddingTop(6).Text(t => AppendInlines(t, h.Inlines, size, bold: true));
                    break;

                case MdParagraph p:
                    col.Item().Text(t => AppendInlines(t, p.Inlines, 10.5f));
                    break;

                case MdList l:
                    foreach (var item in l.Items)
                    {
                        string bullet = item.Ordered ? $"{Math.Max(item.Number, 1)}." : item.Level % 2 == 0 ? "•" : "–";
                        col.Item().PaddingLeft(6 + item.Level * 14).Row(r =>
                        {
                            r.ConstantItem(16).Text(bullet).FontColor(Accent).Bold();
                            r.RelativeItem().Text(t => AppendInlines(t, item.Inlines, 10.5f));
                        });
                    }
                    break;

                case MdCode c:
                    col.Item().Background(Soft).CornerRadius(8).Padding(9)
                        .Text(c.Text.Length > 6000 ? c.Text[..6000] + "\n…" : c.Text).FontSize(8.5f).FontFamily(MonoFamily);
                    break;

                case MdQuote q:
                    col.Item().BorderLeft(2.5f).BorderColor(Accent).PaddingLeft(10)
                        .Text(t => AppendInlines(t, q.Inlines, 10.5f, italic: true));
                    break;

                case MdRule:
                    col.Item().PaddingVertical(4).LineHorizontal(0.8f).LineColor("#D8DCE4");
                    break;

                case MdTable table:
                    ComposeTable(col, table);
                    break;
            }
        }
    }

    private static void ComposeTable(ColumnDescriptor col, MdTable table)
    {
        int columns = Math.Max(1, table.Header.Count);
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                for (int i = 0; i < columns; i++) c.RelativeColumn();
            });

            t.Header(h =>
            {
                foreach (var cell in table.Header)
                    h.Cell().Background(Soft).BorderBottom(0.8f).BorderColor("#C9CFDA").Padding(5)
                        .Text(tx => AppendInlines(tx, cell, 9, bold: true));
            });

            foreach (var row in table.Rows)
            {
                for (int i = 0; i < columns; i++)
                {
                    var cell = i < row.Count ? row[i] : Array.Empty<MdInline>();
                    t.Cell().BorderBottom(0.5f).BorderColor("#E3E6EC").Padding(5)
                        .Text(tx => AppendInlines(tx, cell, 9));
                }
            }
        });
    }

    private static void AppendInlines(TextDescriptor text, IEnumerable<MdInline> inlines, float size, bool bold = false, bool italic = false)
    {
        foreach (var inline in inlines)
        {
            string value = inline.Text;
            if (inline.Href is { } href && (href.StartsWith("http", StringComparison.OrdinalIgnoreCase) || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)))
            {
                text.Hyperlink(value, href).FontSize(size).FontColor(Accent).Underline();
                continue;
            }

            var span = text.Span(value).FontSize(inline.Code ? size - 1 : size);
            if (bold || inline.Bold) span.Bold();
            if (italic || inline.Italic) span.Italic();
            if (inline.Code) span.FontFamily(MonoFamily).BackgroundColor(Soft);
        }
    }
}

/// <summary>Removes characters PDF fonts usually cannot draw (emoji, dingbats) and maps a few symbols to ASCII.</summary>
internal static class PdfText
{
    public static string Clean(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";

        var sb = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsHighSurrogate(c))
            {
                i++; // skip the whole emoji / supplementary plane character
                continue;
            }

            switch (c)
            {
                case '️': case '‍': case '​': case '⁠': continue;
                case '✅': case '✔': case '✓': sb.Append("[OK]"); continue;
                case '❌': case '✖': sb.Append("[x]"); continue;
                case '⚠': sb.Append("[!]"); continue;
                case '→': case '⟶': case '⇒': sb.Append("->"); continue;
                case '←': sb.Append("<-"); continue;
                case '↔': case '↕': sb.Append("<->"); continue;
                case '≈': sb.Append('~'); continue;
                case '≤': sb.Append("<="); continue;
                case '≥': sb.Append(">="); continue;
            }

            // other arrows, dingbats and misc symbols
            if (c is >= '←' and <= '⇿' or >= '☀' and <= '➿' or >= '⬀' and <= '⯿') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
