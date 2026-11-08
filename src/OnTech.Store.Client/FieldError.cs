using System.Globalization;
using System.Text.RegularExpressions;

namespace OnTech.Store.Client;

public enum FieldErrorKind { Required, MaxLength, MinLength, Email, Range, Invalid, Other }

/// <summary>
/// One entry of an ASP.NET validation error ("errors": { "CompanyName": ["The CompanyName field is required."] }), read into
/// what went wrong and on which field, so the app can say it with the field's label and in the user's language (M-3).
/// </summary>
public sealed record FieldError(string Field, string Label, FieldErrorKind Kind, string? Limit, string? Limit2, string Raw)
{
    /// <summary>English text for each kind; {0} = field label, {1} and {2} = limits. The desktop app translates these.</summary>
    public static string Template(FieldErrorKind kind) => kind switch
    {
        FieldErrorKind.Required => "{0} is required.",
        FieldErrorKind.MaxLength => "{0} can be at most {1} characters.",
        FieldErrorKind.MinLength => "{0} must be at least {1} characters.",
        FieldErrorKind.Email => "{0} is not a valid email address.",
        FieldErrorKind.Range => "{0} must be between {1} and {2}.",
        FieldErrorKind.Invalid => "{0} has a value that is not valid.",
        _ => "{0}",
    };

    /// <summary>The message in English, with the field's label instead of the property name.</summary>
    public string English => Kind == FieldErrorKind.Other ? Raw : string.Format(CultureInfo.InvariantCulture, Template(Kind), Label, Limit, Limit2);

    /// <summary>Property name (as the server names it) → the label shown on the forms. English; the app translates it.</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CompanyName"] = "Company name",
        ["PhoneNumber"] = "Phone",
        ["TaxNumber"] = "Tax number",
        ["ContactPerson"] = "Contact person",
        ["Email"] = "Email",
        ["Address"] = "Address",
        ["Notes"] = "Notes",
        ["CustomerNumber"] = "Customer number",
        ["SupplierNumber"] = "Supplier number",
        ["Name"] = "Name",
        ["Code"] = "Code",
        ["Description"] = "Description",
        ["LowStockThreshold"] = "Low stock warning",
        ["CategoryId"] = "Category",
        ["CustomerId"] = "Customer",
        ["SupplierId"] = "Supplier",
        ["ItemId"] = "Item",
        ["Quantity"] = "Quantity",
        ["Lines"] = "Items",
        ["IssueVoucherNumber"] = "Issue voucher number",
        ["ReceiptNumber"] = "Receipt number",
        ["VoucherNumber"] = "Receipt voucher number",
        ["SupplierInvoiceNumber"] = "Supplier invoice number",
        ["OrderNumber"] = "Order number",
        ["Reason"] = "Reason",
        ["FullName"] = "Full name",
        ["Username"] = "Username",
        ["Password"] = "Password",
        ["CurrentPassword"] = "Current password",
        ["NewPassword"] = "New password",
        ["Permissions"] = "Permissions",
        ["Search"] = "Search",
        ["Page"] = "Page",
        ["PageSize"] = "Page size",
        ["Rows"] = "Rows",
        ["From"] = "From",
        ["To"] = "To",
        ["UtcOffsetMinutes"] = "Time zone",
    };

    // The default DataAnnotations / model-binding messages (English server). The field name is captured.
    private static readonly (Regex Pattern, FieldErrorKind Kind)[] Patterns =
    [
        (R(@"The (?<f>.+?) field is required\."), FieldErrorKind.Required),
        (R(@"The field (?<f>.+?) must be a string or array type with a maximum length of '(?<a>\d+)'\."), FieldErrorKind.MaxLength),
        (R(@"The field (?<f>.+?) must be a string with a maximum length of (?<a>\d+)\."), FieldErrorKind.MaxLength),
        (R(@"The field (?<f>.+?) must be a string or array type with a minimum length of '(?<a>\d+)'\."), FieldErrorKind.MinLength),
        (R(@"The field (?<f>.+?) must be a string with a minimum length of (?<a>\d+) and a maximum length of (?<b>\d+)\."), FieldErrorKind.Range),
        (R(@"The (?<f>.+?) field is not a valid e-mail address\."), FieldErrorKind.Email),
        (R(@"The field (?<f>.+?) must be between (?<a>-?[\d.,]+) and (?<b>-?[\d.,]+)\."), FieldErrorKind.Range),
        (R(@"The value '.*' is not valid for (?<f>.+?)\."), FieldErrorKind.Invalid),
        (R(@"The JSON value could not be converted to .*"), FieldErrorKind.Invalid),
        (R(@"The input was not valid\."), FieldErrorKind.Invalid),
        (R(@"The value '.*' is invalid\."), FieldErrorKind.Invalid),
    ];

    private static Regex R(string p) => new("^" + p + "$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>Reads one message of the "errors" dictionary. <paramref name="key"/> is the dictionary key ("CompanyName", "$.customerNumber", "req.Name", "Lines[0].Quantity").</summary>
    public static FieldError Parse(string key, string message)
    {
        var field = FieldName(key);
        foreach (var (pattern, kind) in Patterns)
        {
            var m = pattern.Match(message.Trim());
            if (!m.Success) continue;
            if (m.Groups["f"].Success && field.Length == 0) field = FieldName(m.Groups["f"].Value);
            if (field.Length == 0) break;
            return new FieldError(field, LabelFor(field), kind, Group(m, "a"), Group(m, "b"), message);
        }
        return new FieldError(field, LabelFor(field), FieldErrorKind.Other, null, null, message);
    }

    private static string? Group(Match m, string name) => m.Groups[name].Success ? m.Groups[name].Value : null;

    /// <summary>"$.lines[0].quantity" → "quantity"; "req.CompanyName" → "CompanyName"; "" stays "".</summary>
    public static string FieldName(string key)
    {
        var k = key.Trim();
        if (k.StartsWith("$", StringComparison.Ordinal)) k = k.TrimStart('$', '.');
        k = Regex.Replace(k, @"\[\d+\]", "");
        var dot = k.LastIndexOf('.');
        return dot >= 0 ? k[(dot + 1)..] : k;
    }

    /// <summary>The form label for a property, or the name split into words ("SomeField" → "Some field") when unknown.</summary>
    public static string LabelFor(string field)
    {
        if (field.Length == 0) return "";
        if (Labels.TryGetValue(field, out var label)) return label;
        var words = Regex.Replace(field, "(?<=[a-z0-9])([A-Z])", " $1").ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }
}
