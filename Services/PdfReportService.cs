using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Colors = QuestPDF.Helpers.Colors;

namespace AIDatabaseAnalyzer.Services;
public class ReportSection
{
    public string Title { get; set; }
    public string Content { get; set; }
}

public class PdfReportService
{
    public void GenerateReport(List<ReportSection> sections, string outputPath, string dbName)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                page.Header().Text($"Database Analysis Report: {dbName}")
                    .SemiBold().FontSize(20).FontColor(Colors.Blue.Darken2);

                page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                {
                    col.Spacing(20);
                     
                    for (int i = 0; i < sections.Count; i++)
                    {
                        col.Item().Text($"{i + 1}. {sections[i].Title}")
                           .Bold().FontSize(14).FontColor(Colors.Grey.Darken3);
                        col.Item().Text(sections[i].Content);
                    }
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("AI-Assisted DB Refactoring Tool - Page ");
                    x.CurrentPageNumber();
                });
            });
        }).GeneratePdf(outputPath);
    }
}