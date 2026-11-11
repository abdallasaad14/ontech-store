using System.Globalization;
using System.Text;

namespace OnTech.Store.Client;

/// <summary>
/// Reads numbers typed by a user. An Arabic keyboard types Arabic-Indic digits (٠-٩, U+0660-0669) or, with a Persian/Urdu layout,
/// extended digits (۰-۹, U+06F0-06F9); .NET's parsers only accept 0-9. Every number the user types goes through here.
/// </summary>
public static class Numbers
{
    /// <summary>The Arabic decimal separator (٫, U+066B).</summary>
    public const char ArabicDecimalSeparator = '٫';
    /// <summary>The Arabic thousands separator (٬, U+066C).</summary>
    public const char ArabicThousandsSeparator = '٬';

    /// <summary>Replaces Arabic-Indic and extended Arabic-Indic digits with 0-9 and drops direction marks that RTL text can carry. Other text is unchanged.</summary>
    public static string NormalizeDigits(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        StringBuilder? sb = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            char? mapped = c switch
            {
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                _ => null,
            };
            var isMark = c is '‎' or '‏' or '؜' or '‪' or '‫' or '‬' or '‭' or '‮';
            if (mapped is null && !isMark)
            {
                sb?.Append(c);
                continue;
            }
            sb ??= new StringBuilder(text, 0, i, text.Length);
            if (mapped is { } m) sb.Append(m);
        }
        return sb?.ToString() ?? text;
    }

    /// <summary>A whole number in Western or Arabic digits, with spaces around it allowed. No separators, no decimals.</summary>
    public static bool TryParseInt(string? text, out int value) =>
        int.TryParse(NormalizeDigits(text).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    /// <summary>A whole number (64-bit), same rules as <see cref="TryParseInt"/>.</summary>
    public static bool TryParseLong(string? text, out long value) =>
        long.TryParse(NormalizeDigits(text).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// A decimal number in Western or Arabic digits. The decimal point may be "." or "٫"; thousands may be grouped with "," or "٬".
    /// </summary>
    public static bool TryParseDecimal(string? text, out decimal value)
    {
        var s = NormalizeDigits(text).Trim()
            .Replace(ArabicDecimalSeparator, '.')
            .Replace(ArabicThousandsSeparator.ToString(), "");
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>A decimal number as a double, same rules as <see cref="TryParseDecimal"/> plus exponents (for spreadsheet cells).</summary>
    public static bool TryParseDouble(string? text, out double value)
    {
        var s = NormalizeDigits(text).Trim()
            .Replace(ArabicDecimalSeparator, '.')
            .Replace(ArabicThousandsSeparator.ToString(), "");
        return double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
    }
}
