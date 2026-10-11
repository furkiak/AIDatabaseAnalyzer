namespace AIDatabaseAnalyzer.Services.Markdown;

/// <summary>A run of text with uniform formatting.</summary>
public sealed record MdInline(string Text, bool Bold = false, bool Italic = false, bool Code = false, string? Href = null);

public abstract record MdBlock;

public sealed record MdHeading(int Level, IReadOnlyList<MdInline> Inlines) : MdBlock;

public sealed record MdParagraph(IReadOnlyList<MdInline> Inlines) : MdBlock;

public sealed record MdListItem(int Level, bool Ordered, int Number, IReadOnlyList<MdInline> Inlines);

public sealed record MdList(IReadOnlyList<MdListItem> Items) : MdBlock;

public sealed record MdCode(string? Language, string Text) : MdBlock;

public sealed record MdQuote(IReadOnlyList<MdInline> Inlines) : MdBlock;

public sealed record MdTable(IReadOnlyList<IReadOnlyList<MdInline>> Header, IReadOnlyList<IReadOnlyList<IReadOnlyList<MdInline>>> Rows) : MdBlock;

public sealed record MdRule : MdBlock;
