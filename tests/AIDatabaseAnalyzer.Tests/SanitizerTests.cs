using AIDatabaseAnalyzer.Services.Database;

namespace AIDatabaseAnalyzer.Tests;

public class SanitizerTests
{
    [Fact]
    public void Emails_keep_their_shape_but_hide_the_details()
    {
        Assert.Equal("j***@g***.com", SampleDataSanitizer.Mask("contact", "john.doe@gmail.com"));
    }

    [Fact]
    public void Card_numbers_that_pass_luhn_are_masked_in_any_column()
    {
        object? masked = SampleDataSanitizer.Mask("notes", "4111 1111 1111 1111");
        Assert.NotEqual("4111 1111 1111 1111", masked);
        Assert.Equal("4111 1111 1111 1112", SampleDataSanitizer.Mask("notes", "4111 1111 1111 1112")); // fails Luhn → plain text
    }

    [Fact]
    public void Password_like_columns_are_redacted()
    {
        Assert.Equal("<redacted 4 chars>", SampleDataSanitizer.Mask("user_password", "hunt"));
    }

    [Fact]
    public void Harmless_columns_are_untouched_and_short_keywords_need_an_exact_match()
    {
        Assert.Equal("Grand Hotel", SampleDataSanitizer.Mask("hotel", "Grand Hotel")); // contains "tel" but is not a phone column
        Assert.Equal("Widget", SampleDataSanitizer.Mask("product_name", "Widget"));
        Assert.Equal(42L, SampleDataSanitizer.Mask("quantity", 42L));
    }

    [Fact]
    public void Values_are_normalized_to_small_json_friendly_values()
    {
        Assert.Equal("<binary 3 bytes>", SampleDataSanitizer.Normalize(new byte[] { 1, 2, 3 }));
        Assert.Equal("2024-02-03 04:05:06", SampleDataSanitizer.Normalize(new DateTime(2024, 2, 3, 4, 5, 6)));
        Assert.Null(SampleDataSanitizer.Normalize(DBNull.Value));
        string longText = new('x', 500);
        Assert.True(((string)SampleDataSanitizer.Normalize(longText)!).Length <= SampleDataSanitizer.MaxValueChars + 1);
    }
}
