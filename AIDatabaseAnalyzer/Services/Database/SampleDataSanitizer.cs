using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AIDatabaseAnalyzer.Services.Database;

/// <summary>
/// Turns raw database values into small JSON friendly values and (optionally) masks sensitive looking data,
/// so sample rows can be sent to an AI model without leaking personal information.
/// </summary>
public static partial class SampleDataSanitizer
{
    public const int MaxValueChars = 120;

    private static readonly string[] SecretKeywords =
        { "password", "passwd", "pwd", "secret", "apikey", "token", "hash", "salt", "passphrase", "privatekey" };

    private static readonly string[] PersonalKeywords =
    {
        "email", "mail", "phone", "tel", "gsm", "mobile", "cellphone", "fax", "ssn", "tckn", "tcno", "tcid", "nationalid",
        "passport", "taxid", "taxno", "vkn", "iban", "cardnumber", "cardno", "creditcard", "ccnum", "cvv", "cvc",
        "firstname", "lastname", "surname", "fullname", "middlename", "givenname", "familyname", "address", "street",
        "birthdate", "dateofbirth", "dob", "birthday", "ipaddress", "licenseplate", "plate"
    };

    // ------------------------------------------------------------------ normalisation

    /// <summary>Converts a provider specific value to <c>null</c>, bool, number or string.</summary>
    public static object? Normalize(object? value)
    {
        switch (value)
        {
            case null:
            case DBNull:
                return null;
            case bool or byte or sbyte or short or ushort or int or uint or long or float or double or decimal:
                return value is float f && !float.IsFinite(f) || value is double d && !double.IsFinite(d)
                    ? Convert.ToString(value, CultureInfo.InvariantCulture)
                    : value;
            case ulong ul:
                return ul <= long.MaxValue ? (long)ul : ul.ToString(CultureInfo.InvariantCulture);
            case string s:
                return Truncate(s);
            case char c:
                return c.ToString();
            case byte[] bytes:
                return $"<binary {bytes.Length} bytes>";
            case DateTime dt:
                return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            case DateTimeOffset dto:
                return dto.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
            case DateOnly date:
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case TimeOnly time:
                return time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            case TimeSpan span:
                return span.ToString("c", CultureInfo.InvariantCulture);
            case Guid guid:
                return guid.ToString();
            case Array array:
                var parts = array.Cast<object?>().Take(8).Select(v => Convert.ToString(Normalize(v), CultureInfo.InvariantCulture));
                return Truncate("[" + string.Join(", ", parts) + (array.Length > 8 ? ", …" : "") + "]");
            default:
                return Truncate(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
        }
    }

    public static string Truncate(string value, int max = MaxValueChars) =>
        value.Length <= max ? value : value[..max] + "…";

    // ------------------------------------------------------------------ masking

    /// <summary>Masks <paramref name="value"/> when the column name or the value itself looks sensitive.</summary>
    public static object? Mask(string columnName, object? value)
    {
        if (value is null) return null;

        string key = NormalizeKey(columnName);

        if (SecretKeywords.Any(k => key.Contains(k, StringComparison.Ordinal)))
            return value is string secret ? $"<redacted {secret.Length} chars>" : "<redacted>";

        if (value is string text)
        {
            if (text.Length == 0) return text;

            if (EmailRegex().IsMatch(text)) return MaskEmail(text);
            if (LooksLikeCardNumber(text) || IbanRegex().IsMatch(text)) return MaskGeneric(text);

            if (PersonalKeywords.Any(k => MatchesKeyword(key, k)))
                return key.Contains("birth", StringComparison.Ordinal) || key == "dob"
                    ? MaskBirthDate(text)
                    : MaskGeneric(text);

            return text;
        }

        // Numeric identifiers (e.g. a national id stored in a bigint column) are masked by column name.
        if (value is long or int or decimal or short && PersonalKeywords.Any(k => MatchesKeyword(key, k)))
            return MaskGeneric(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");

        return value;
    }

    private static bool MatchesKeyword(string key, string keyword) =>
        // Short keywords ("tel", "dob", "ssn") must match the whole column name to avoid false hits such as "hotel".
        keyword.Length <= 3 ? key == keyword : key.Contains(keyword, StringComparison.Ordinal);

    private static string NormalizeKey(string columnName)
    {
        var sb = new StringBuilder(columnName.Length);
        foreach (char c in columnName)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    public static string MaskEmail(string email)
    {
        int at = email.IndexOf('@');
        if (at <= 0) return MaskGeneric(email);

        string local = email[..at];
        string domain = email[(at + 1)..];
        int dot = domain.LastIndexOf('.');
        string host = dot > 0 ? domain[..dot] : domain;
        string tld = dot > 0 ? domain[dot..] : "";
        return $"{local[0]}***@{(host.Length > 0 ? host[0] : '*')}***{tld}";
    }

    public static string MaskGeneric(string value)
    {
        if (value.Length <= 2) return new string('*', value.Length);
        if (value.Length <= 6) return value[0] + new string('*', value.Length - 2) + value[^1];

        int keep = value.Length >= 10 ? 2 : 1;
        return value[..keep] + new string('*', value.Length - 2 * keep) + value[^keep..];
    }

    private static string MaskBirthDate(string value) =>
        value.Length >= 4 && char.IsDigit(value[0]) ? value[..4] + "-**-**" : MaskGeneric(value);

    private static bool LooksLikeCardNumber(string text)
    {
        var digits = new StringBuilder(19);
        foreach (char c in text)
        {
            if (char.IsDigit(c)) digits.Append(c);
            else if (c != ' ' && c != '-') return false;
        }

        if (digits.Length is < 13 or > 19) return false;

        int sum = 0;
        bool alternate = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int n = digits[i] - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9) n -= 9;
            }
            sum += n;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^[A-Z]{2}\d{2}[A-Z0-9]{11,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex IbanRegex();
}
