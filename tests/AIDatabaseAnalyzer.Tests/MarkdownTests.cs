using AIDatabaseAnalyzer.Services.Markdown;

namespace AIDatabaseAnalyzer.Tests;

public class MarkdownTests
{
    [Fact]
    public void Parses_headings_paragraphs_lists_code_and_tables()
    {
        const string md = "# Title\n\nSome **bold** text.\n\n- one\n  - nested\n- two\n\n```sql\nSELECT 1;\n```\n\n| a | b |\n|---|---|\n| 1 | 2 |\n";
        var blocks = MarkdownParser.Parse(md);

        Assert.IsType<MdHeading>(blocks[0]);
        Assert.Equal(1, ((MdHeading)blocks[0]).Level);
        Assert.IsType<MdParagraph>(blocks[1]);

        var list = Assert.IsType<MdList>(blocks[2]);
        Assert.Equal(3, list.Items.Count);
        Assert.Equal(1, list.Items[1].Level);

        var code = Assert.IsType<MdCode>(blocks[3]);
        Assert.Equal("sql", code.Language);
        Assert.Equal("SELECT 1;", code.Text);

        var table = Assert.IsType<MdTable>(blocks[4]);
        Assert.Equal(2, table.Header.Count);
        Assert.Single(table.Rows);
    }

    [Fact]
    public void Snake_case_words_are_not_treated_as_emphasis()
    {
        var inlines = MarkdownParser.ParseInlines("use snake_case_word and `code_here` and *italic*");
        Assert.Contains(inlines, i => i.Text.Contains("snake_case_word") && !i.Italic);
        Assert.Contains(inlines, i => i.Code && i.Text == "code_here");
        Assert.Contains(inlines, i => i.Italic && i.Text == "italic");
    }

    [Fact]
    public void Html_output_is_always_escaped()
    {
        string html = MarkdownHtmlRenderer.Render("<script>alert(1)</script> and [x](javascript:alert(1)) and [ok](https://example.com)");
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("href=\"javascript", html);
        Assert.Contains("href=\"https://example.com\"", html);
    }

    [Fact]
    public void Nested_lists_render_as_nested_html_lists()
    {
        string html = MarkdownHtmlRenderer.Render("- a\n  - b\n- c");
        Assert.Equal(2, html.Split("<ul>").Length - 1);
        Assert.Contains("<li>a<ul><li>b</li></ul></li>", html);
    }
}
